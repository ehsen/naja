using Naja.CodeGen;
using Naja.CodeGen.Emitters.Statements;
using Naja.Inference;
using Naja.Parser;
using Naja.Semantics;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using NajaParserModule = Naja.Parser.Module;
using SysModule = System.Reflection.Module;

namespace Naja.CodeGen;

/// <summary>
/// Partial class: Module-level IL emission logic.
/// Orchestrates three-pass compilation: Pass 1 (declare stubs), Pass 1.5 (class methods),
/// Pass 2 (Main() entry point), Pass 3 (function/class bodies).
/// </summary>
public sealed partial class AssemblyEmitter
{
    private TypeBuilder EmitModule(
        NajaParserModule module,
        ModuleBuilder modBuilder,
        CompilationProfile profile)
    {
        // Use assembly name as the module type name so entry point resolution works correctly.
        // The type name must match the assembly name for proper entry point lookup.
        var typeBuilder = modBuilder.DefineType(
            _assemblyName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        // ── Build import map ──────────────────────────────────────────────────
        // Must happen before Pass 1 because DeclareClass needs it to resolve base types.
        // 
        // Three import categories:
        // 1. Python StdLib (sys, math, os, etc.) → resolved via StdLibResolver
        // 2. .NET Framework types (System.*, etc.) → resolved via FrameworkTypeResolver
        // 3. Namespace imports (import System) → resolved on-demand at access time
        //
        // We search StdLib first (modular assemblies), then AppDomain (strong-named types),
        // then FrameworkTypeResolver (disk-based resolution for framework types).
        var importMap = new Dictionary<string, (string TypeName, string AssemblyName)>();
        var namespaceImports = new Dictionary<string, string>();
        var usedStdLibAssemblies = new HashSet<string>();  // Track which stdlib assemblies are needed

        foreach (var stmt in module.Body)
        {
            if (stmt is FromImportStatement fis)
            {
                // Check if this is a stdlib module import (e.g., "from sys import argv")
                if (StdLibResolver.TryResolve(fis.Module, out var stdLibModule))
                {
                    foreach (var alias in fis.Names)
                    {
                        var localName = alias.Alias ?? alias.Name;
                        // For stdlib modules, the type is the singleton class (e.g., NajaSys)
                        // and we access members via reflection at codegen time
                        importMap[localName] = (stdLibModule.TypeName, stdLibModule.AssemblyName);
                    }
                    usedStdLibAssemblies.Add(stdLibModule.AssemblyName);
                }
                else
                {
                    // Standard .NET type import
                    foreach (var alias in fis.Names)
                    {
                        var localName = alias.Alias ?? alias.Name;
                        // Handle both "from System import X" and "from System.X import Y"
                        var typeName = fis.Module.Contains('.')
                            ? fis.Module + "." + alias.Name
                            : fis.Module + "." + alias.Name;

                        // Resolve the assembly that owns this type.
                        // Strategy (mirrors Roslyn): ask the disk-based FrameworkTypeResolver
                        // first so the compiler process never needs the target framework loaded.
                        // Fall back to AppDomain only for types the host itself has loaded
                        // (e.g. mscorlib / System.Private.CoreLib primitives).
                        string asmShortName = _typeResolver.ResolveAssemblyName(typeName)
                            ?? AppDomain.CurrentDomain.GetAssemblies()
                                   .Select(a => { try { return a.GetType(typeName, false, true); } catch { return null; } })
                                   .FirstOrDefault(t => t is not null)
                                   ?.Assembly.GetName().Name
                            ?? fis.Module;   // last-resort: use the namespace itself

                        importMap[localName] = (typeName, asmShortName);
                    }
                }
            }
            else if (stmt is ImportStatement imp)
            {
                // Handle both stdlib and namespace imports:
                // "import sys" → register as stdlib singleton
                // "import System" → register as namespace import for on-demand resolution
                foreach (var alias in imp.Names)
                {
                    var baseName = alias.Name.Split('.')[0];  // e.g., "sys" from "sys.path"
                    var localName = alias.Alias ?? baseName;

                    if (StdLibResolver.TryResolve(baseName, out var stdLibModule))
                    {
                        // Python stdlib module: create a mapping to the singleton instance
                        importMap[localName] = (stdLibModule.TypeName, stdLibModule.AssemblyName);
                        usedStdLibAssemblies.Add(stdLibModule.AssemblyName);

                        // ALSO register in namespaceImports so Pass 3 (function/class bodies)
                        // resolves the import identically to Pass 2 (Main). StatementEmitter's
                        // ImportStatement arm adds ALL plain imports to NamespaceImports at
                        // statement-emit time; without this, module-level code resolves stdlib
                        // names via the NamespaceImports→singleton-instance path while function
                        // bodies fall through to the ImportMap→Type-object path, breaking
                        // instance-method dispatch (e.g. os.getcwd() inside a def).
                        namespaceImports[localName] = "";
                    }
                    else
                    {
                        // Namespace import: types resolved on-demand at access time
                        namespaceImports[localName] = "";
                    }
                }
            }
        }

        // ── Deep scan: imports nested inside try/if/for/while/with bodies ──────
        // The CPython optional-import pattern (`try: import _winapi
        // except ImportError: _winapi = None`) binds module names inside control
        // flow that the top-level walk above never sees. Register those the same
        // way so every scope resolves them identically.
        foreach (var (moduleName, localName) in
                 Emitters.Statements.StatementAnalyzer.CollectImportBindings(module.Body))
        {
            if (importMap.ContainsKey(localName) || namespaceImports.ContainsKey(localName))
                continue;   // already registered from the top-level walk
            var baseName2 = moduleName.Split('.')[0];
            if (StdLibResolver.TryResolve(baseName2, out var stdLibModule2))
            {
                importMap[localName] = (stdLibModule2.TypeName, stdLibModule2.AssemblyName);
                usedStdLibAssemblies.Add(stdLibModule2.AssemblyName);
                namespaceImports[localName] = "";
            }
            else
            {
                namespaceImports[localName] = "";
            }
        }

        // Store used stdlib assemblies for smart reference injection
        // (only the assemblies actually imported get referenced in the generated code)
        var usedAssemblies = usedStdLibAssemblies.ToList();

        // ── Pass 1: declare ALL stubs before emitting any IL ──────────────────
        // EmitName searches ctx.Fields / ctx.Methods / ctx.ClassTypes at emit time.
        // If a name is used before its definition in the file (forward reference,
        // or simply inside "if __name__ == '__main__':") and the stub is not here,
        // EmitName throws "Undefined name".  Pass 1 prevents that entirely.
        var fields = new Dictionary<string, FieldBuilder>();
        var methods = new Dictionary<string, MethodBuilder>();
        var paramTypes = new Dictionary<string, Type[]>();
        var classTypes = new Dictionary<string, TypeBuilder>();
        var classCtors = new Dictionary<string, ConstructorBuilder>();

        var functionDefs = new Dictionary<string, FunctionDef>();
        foreach (var stmt in module.Body)
        {
            switch (stmt)
            {
                case FunctionDef fn:
                    {
                        var (mb, pts) = TypeDeclaration.DeclareMethod(_model, fn, typeBuilder);
                        methods[fn.Name] = mb;
                        paramTypes[fn.Name] = pts;
                        functionDefs[fn.Name] = fn;
                        break;
                    }
                case ClassDef cls:
                    {
                        var ct = DeclareClass(cls, modBuilder, importMap, module.Body, out var ctor, namespaceImports, overrideName: null, localClassTypes: classTypes);
                        classTypes[cls.Name] = ct;
                        classCtors[cls.Name] = ctor;
                        _classTypes[cls.Name] = ct;
                        _classConstructors[cls.Name] = ctor;

                        // Store parameter count for later inheritance lookups.
                        // Only set when __init__ is present — if absent, DeclareClass may have
                        // already recorded a better value (e.g. autoExceptionCtor = 1).
                        var initFn = cls.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
                        if (initFn != null)
                        {
                            bool hasSelf = initFn.Params.Count > 0 && initFn.Params[0].Name is "self" or "cls";
                            int ac = hasSelf ? initFn.Params.Count - 1 : initFn.Params.Count;
                            _classCtorArgCounts[cls.Name] = ac;
                        }

                        // PASS 1.5: Handle class-level attributes like __match_args__
                        foreach (var member in cls.Body)
                        {
                            if (member is FunctionDef cfn)
                            {
                                _classMethodNames.Add($"{cls.Name}.{cfn.Name}");
                            }
                            else if (member is AssignStatement cas)
                            {
                                foreach (var target in cas.Targets)
                                {
                                    if (target is NameExpr cn)
                                    {
                                        // Emit as public static field on the class
                                        var sfb = ct.DefineField(cn.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static);
                                        if (!_classStaticFieldBuilders.ContainsKey(cls.Name))
                                            _classStaticFieldBuilders[cls.Name] = new();
                                        _classStaticFieldBuilders[cls.Name][cn.Name] = sfb;
                                    }
                                }
                            }
                             else if (member is AnnAssignStatement ann && ann.Target is NameExpr an)
                            {
                                // Handle class-level annotated assignments
                                // typing.Final fields should be instance fields with InitOnly
                                bool isFinal =
                                    ann.Annotation is NameExpr { Name: "Final" }
                                    || (ann.Annotation is AttributeExpr fa
                                        && fa.Attribute == "Final"
                                        && fa.Object is NameExpr { Name: "typing" })
                                    || (ann.Annotation is SubscriptExpr sub
                                        && (sub.Object is NameExpr { Name: "Final" }
                                            || (sub.Object is AttributeExpr sa
                                                && sa.Attribute == "Final"
                                                && sa.Object is NameExpr { Name: "typing" })));

                                if (isFinal)
                                {
                                    // typing.Final fields are instance fields with InitOnly flag
                                    var fbAttrs = FieldAttributes.Public | FieldAttributes.InitOnly;
                                    ct.DefineField(an.Name, typeof(object), fbAttrs);
                                }
                                else
                                {
                                    // Non-Final class variables are static
                                    var fbAttrs = FieldAttributes.Public | FieldAttributes.Static;
                                    ct.DefineField(an.Name, typeof(object), fbAttrs);
                                }
                            }
                        }
                        break;
                    }
                case AssignStatement assign:
                    {
                        foreach (var target in assign.Targets)
                        {
                            if (target is NameExpr n && !fields.ContainsKey(n.Name))
                            {
                                var sym = _model.ModuleScope.Lookup(n.Name);
                                var clrT = sym is not null
                                    ? TypeMapper.ToClrType(sym.Type)
                                    : typeof(object);
                                if (clrT == typeof(void)) clrT = typeof(object);

                                fields[n.Name] = typeBuilder.DefineField(
                                    n.Name, clrT,
                                    FieldAttributes.Public | FieldAttributes.Static);
                            }
                        }
                        break;
                    }
                case AnnAssignStatement ann when ann.Target is NameExpr an:
                    {
                        if (!fields.ContainsKey(an.Name))
                        {
                            var clrT = typeof(object);
                            if (ann.Annotation is NameExpr annot)
                                clrT = TypeMapper.ToClrType(NajaTypes.FromAnnotation(annot.Name))
                                    ?? typeof(object);
                            fields[an.Name] = typeBuilder.DefineField(
                                an.Name, clrT,
                                FieldAttributes.Public | FieldAttributes.Static);
                        }
                        break;
                    }
            }
        }

        // Deep scan: hoist module-level control-flow variables referenced by nested functions
        // (e.g. `for i in range(3): def f(): return i` — `i` must be a static field, not a local)
        {
            var moduleNestedRefs = StatementAnalyzer.CollectNamesReferencedByNestedFunctions(module.Body);
            var moduleAssigned = StatementAnalyzer.CollectAssignedNames(module.Body);
            foreach (var r in moduleNestedRefs.Intersect(moduleAssigned))
            {
                // Don't hoist names that are resolved via the import map — they are .NET types,
                // not module-level variables. Creating a null static field would shadow the ImportMap
                // lookup in NameEmitters and cause ldsfld to push null instead of the Type object.
                if (importMap.ContainsKey(r) || namespaceImports.ContainsKey(r)) continue;
                if (!fields.ContainsKey(r))
                    fields[r] = typeBuilder.DefineField(r, typeof(object),
                        FieldAttributes.Public | FieldAttributes.Static);
            }
        }

        // ── Pass 1 (nested): declare TypeBuilders for ClassDefs inside function bodies ──
        // Top-level classes were handled above; this covers classes defined inside functions
        // or class methods (e.g. `def test(self): class Foo: ...`).
        // Each gets a unique IL type name: "{cls.Name}_L{cls.Line}" to avoid clashes.
        DeclareNestedClassesInBody(module.Body, modBuilder, importMap, module.Body, namespaceImports, classTypes, classCtors);

        // ── Pass 1.5: declare ALL class methods ───────────────────────────────
        foreach (var stmt in module.Body)
        {
            if (stmt is ClassDef cls && classTypes.TryGetValue(cls.Name, out var ct))
            {
                foreach (var member in cls.Body)
                {
                    if (member is not FunctionDef fnd) continue;

                    var (mb, pts, uniqueName) = TypeDeclaration.DeclareInstanceMethod(fnd, ct);
                    string key = $"{cls.Name}.{uniqueName}";
                    _classMethods[key] = mb;
                    _classMethodParamTypes[key] = pts;
                    _classMethodNames.Add(key);
                }
            }
        }

        // ── Pass 1.5 (nested): declare method stubs for classes inside functions ──
        foreach (var (nestedUniqueName, cls) in _nestedClassDefs)
        {
            if (classTypes.TryGetValue(nestedUniqueName, out var ct))
            {
                foreach (var member in cls.Body)
                {
                    if (member is not FunctionDef fnd) continue;

                    var (mb, pts, uname) = TypeDeclaration.DeclareInstanceMethod(fnd, ct);
                    string key = $"{nestedUniqueName}.{uname}";
                    _classMethods[key] = mb;
                    _classMethodParamTypes[key] = pts;
                    _classMethodNames.Add(key);
                }
            }
        }


        // Module builtins — always declared so "if __name__ == '__main__':" works
        if (!fields.ContainsKey("__name__"))
            fields["__name__"] = typeBuilder.DefineField(
                "__name__", typeof(string), FieldAttributes.Public | FieldAttributes.Static);
        if (!fields.ContainsKey("__file__"))
            fields["__file__"] = typeBuilder.DefineField(
                "__file__", typeof(string), FieldAttributes.Public | FieldAttributes.Static);

        // ── WinForms preamble bookkeeping ─────────────────────────────────────
        // Record which Application.* methods we will emit in the preamble so that
        // StatementEmitter can skip them when it encounters the same calls in source.
        if (profile == CompilationProfile.WinForms)
        {
            var appType = Type.GetType("System.Windows.Forms.Application, System.Windows.Forms");
            if (appType != null)
            {
                if (appType.GetMethod("SetHighDpiMode") != null)
                    _winFormsPreambleEmitted.Add("SetHighDpiMode");
                if (appType.GetMethod("EnableVisualStyles", Type.EmptyTypes) != null)
                    _winFormsPreambleEmitted.Add("EnableVisualStyles");
                if (appType.GetMethod("SetCompatibleTextRenderingDefault", new[] { typeof(bool) }) != null)
                    _winFormsPreambleEmitted.Add("SetCompatibleTextRenderingDefault");
            }
        }

        // ── Pass 2: define and emit Main() ────────────────────────────────────
        var mainBuilder = typeBuilder.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);

        // Stash for EmitToFile — PersistedAssemblyBuilder needs the MethodBuilder
        // handle to wire the PE entry point via MetadataTokens.MethodDefinitionHandle.
        _mainMethod = mainBuilder;

        var mainIL = mainBuilder.GetILGenerator();

        // For GUI profiles wrap the entire Main body in a try-catch that calls
        // MessageBox.Show on unhandled exceptions.  Without this, any crash
        // (e.g. a missing runtime DLL) dies completely silently: WindowsGui
        // apps have no console, so the CLR's default exception text goes nowhere.
        bool isGuiMain = profile is CompilationProfile.WinForms or CompilationProfile.Wpf;
        System.Reflection.Emit.Label guiExBlockEnd = default;
        if (isGuiMain)
            guiExBlockEnd = mainIL.BeginExceptionBlock();

        // WinForms preamble IL — emitted once here; StatementEmitter skips these
        // calls when it walks the Python source (double-emit prevention).
        if (profile == CompilationProfile.WinForms)
        {
            var appType = Type.GetType("System.Windows.Forms.Application, System.Windows.Forms");
            if (appType != null)
            {
                // Order matters: SetHighDpiMode must come before EnableVisualStyles
                var shdpi = appType.GetMethod("SetHighDpiMode");
                if (shdpi != null)
                {
                    mainIL.Emit(OpCodes.Ldc_I4_2);   // HighDpiMode.SystemAware = 2
                    mainIL.Emit(OpCodes.Call, shdpi);

                    // FIX: SetHighDpiMode returns a bool. We MUST pop it so the stack stays balanced!
                    if (shdpi.ReturnType != typeof(void))
                    {
                        mainIL.Emit(OpCodes.Pop);
                    }
                }
                var evs = appType.GetMethod("EnableVisualStyles", Type.EmptyTypes);
                if (evs != null)
                    mainIL.Emit(OpCodes.Call, evs);

                var sctrd = appType.GetMethod("SetCompatibleTextRenderingDefault", new[] { typeof(bool) });
                if (sctrd != null)
                {
                    mainIL.Emit(OpCodes.Ldc_I4_0);
                    mainIL.Emit(OpCodes.Call, sctrd);
                }
            }
        }

        // Initialise __name__ / __file__ so "if __name__ == '__main__':" evaluates
        mainIL.Emit(OpCodes.Ldstr, "__main__");
        mainIL.Emit(OpCodes.Stsfld, fields["__name__"]);
        mainIL.Emit(OpCodes.Ldstr, "");
        mainIL.Emit(OpCodes.Stsfld, fields["__file__"]);

        // Build emit context with all Pass-1 dictionaries populated
        var mainCtx = new EmitContext(mainIL, _model, typeBuilder, modBuilder, typeof(void), []);
        foreach (var (k, v) in fields) mainCtx.Fields[k] = v;
        foreach (var (k, v) in methods) mainCtx.Methods[k] = v;
        foreach (var (k, v) in paramTypes) mainCtx.MethodParamTypes[k] = v;
        foreach (var (k, v) in classTypes) mainCtx.ClassTypes[k] = v;
        foreach (var (k, v) in classCtors) mainCtx.ClassConstructors[k] = v;
        foreach (var (k, v) in _classCtorArgCounts) mainCtx.ClassCtorArgCounts[k] = v;
        foreach (var (k, v) in importMap) mainCtx.ImportMap[k] = v;
        foreach (var (k, v) in functionDefs) mainCtx.FunctionDefs[k] = v;

        foreach (var (k, v) in namespaceImports) mainCtx.NamespaceImports[k] = v;
        foreach (var mn in _classMethodNames) mainCtx.ClassMethods.Add(mn);
        foreach (var (k, v) in _classMethods) mainCtx.AllClassMethods[k] = v;
        foreach (var (k, v) in _classMethodParamTypes) mainCtx.AllClassMethodParamTypes[k] = v;
        foreach (var mn in _winFormsPreambleEmitted) mainCtx.WinFormsPreambleEmitted.Add(mn);


        // Walk module body — skip FunctionDef and ClassDef (handled in Pass 3)
        var stmtEmitter = new StatementEmitter(mainCtx);
        foreach (var stmt in module.Body)
        {
            if (stmt is FunctionDef or ClassDef) continue;
            stmtEmitter.Emit(stmt);
        }

        if (isGuiMain)
        {
            // Exit the try block cleanly.
            mainIL.Emit(OpCodes.Leave, guiExBlockEnd);

            // catch (Exception ex) — ex is on the evaluation stack.
            mainIL.BeginCatchBlock(typeof(Exception));

            // Use Dup so we can call ex.ToString() twice (once for the log file and once
            // for the MessageBox) without needing a local variable.  PersistedAssemblyBuilder
            // has known quirks with Stloc/Ldloc in catch blocks; Dup avoids them entirely.
            //
            // Stack before Dup: [ex]
            // After Dup:        [ex, ex]
            mainIL.Emit(OpCodes.Dup);
            mainIL.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);     // [ex, exString]

            // ── Write full trace to <exe-dir>/naja.error.log ──────────────────────
            // NajaBuiltins.WriteErrorLog lives in Naja.CodeGen.dll which is always copied
            // alongside the generated exe; no new assembly references needed.
            var writeErrorLog = typeof(NajaBuiltins).GetMethod("WriteErrorLog", [typeof(string)])!;
            mainIL.Emit(OpCodes.Call, writeErrorLog);                                 // [ex]

            // ── MessageBox with the same full text ────────────────────────────────
            mainIL.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);     // [exString]
            var msgBoxType = Type.GetType("System.Windows.Forms.MessageBox, System.Windows.Forms");
            var showMethod = msgBoxType?.GetMethod("Show", [typeof(string), typeof(string)]);
            if (showMethod != null)
            {
                mainIL.Emit(OpCodes.Ldstr, "Naja Application Error");
                mainIL.Emit(OpCodes.Call, showMethod);
                mainIL.Emit(OpCodes.Pop); // discard DialogResult
            }
            else
            {
                mainIL.Emit(OpCodes.Pop); // discard exString — no MessageBox available
            }

            mainIL.Emit(OpCodes.Leave, guiExBlockEnd);
            mainIL.EndExceptionBlock();
        }

        mainIL.Emit(OpCodes.Ret);

        // ── Pass 3: emit function and class bodies ────────────────────────────
        foreach (var stmt in module.Body)
        {
            if (stmt is FunctionDef fn && methods.TryGetValue(fn.Name, out var mb))
                EmitFunctionBody(fn, mb, typeBuilder, modBuilder, fields, methods, paramTypes, importMap);
            else if (stmt is ClassDef cls && classTypes.TryGetValue(cls.Name, out var ct))
                EmitClassBody(cls, ct, modBuilder, fields, methods, paramTypes, classTypes, classCtors, importMap, namespaceImports);
        }

        // ── Pass 3 (nested): emit class bodies for classes defined inside functions ──
        // Build merged dicts: module-level entries + anything registered inside function
        // bodies (e.g. `make_decorator` defined inside test_eval_order — needed by the
        // NameLookupTracer class methods that reference it as a closure).
        var mergedFields = new Dictionary<string, FieldBuilder>(fields);
        foreach (var (k, v) in _innerFunctionFields) mergedFields.TryAdd(k, v);
        var mergedMethods = new Dictionary<string, MethodBuilder>(methods);
        foreach (var (k, v) in _innerFunctionMethods) mergedMethods.TryAdd(k, v);
        var mergedClassTypes = new Dictionary<string, TypeBuilder>(classTypes);
        foreach (var (k, v) in _innerFunctionClassTypes) mergedClassTypes.TryAdd(k, v);
        var mergedClassCtors = new Dictionary<string, ConstructorBuilder>(classCtors);
        foreach (var (k, v) in _innerFunctionClassCtors) mergedClassCtors.TryAdd(k, v);

        foreach (var (uniqueName, cls) in _nestedClassDefs)
        {
            if (mergedClassTypes.TryGetValue(uniqueName, out var ct))
            {
                // Start with the global merged fields, then overlay the per-class snapshot
                // captured at the exact point this class was defined inside its enclosing
                // function body.  This resolves the TryAdd collision where multiple methods
                // on the same class hoist different static fields under the same variable
                // name (e.g. 'x' in testExtraNesting vs testNonLocalMethod on ScopeTests).
                var classFields = mergedFields;
                if (_nestedClassFieldContexts.TryGetValue(uniqueName, out var specificFields))
                {
                    classFields = new Dictionary<string, FieldBuilder>(mergedFields);
                    foreach (var (k, v) in specificFields) classFields[k] = v;
                }
                EmitClassBody(cls, ct, modBuilder, classFields, mergedMethods, paramTypes,
                              mergedClassTypes, mergedClassCtors, importMap, namespaceImports, classKeyOverride: uniqueName);
            }
        }

        // Finalise class TypeBuilders created in this module
        foreach (var ct in classTypes.Values)
        {
            try { ct.CreateType(); } catch { /* already created */ }
        }

        return typeBuilder;
    }

    // ── Nested class scanner ──────────────────────────────────────────────────

    /// <summary>
    /// Recursively walks all statements looking for ClassDef nodes nested inside
    /// FunctionDef bodies (and control-flow blocks). Declares each as a TypeBuilder
    /// using a unique IL name "{cls.Name}_L{cls.Line}" to avoid clashes.
    /// </summary>
    private void DeclareNestedClassesInBody(
        IReadOnlyList<Statement> body,
        ModuleBuilder modBuilder,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap,
        IReadOnlyList<Statement> moduleBody,
        Dictionary<string, string> namespaceImports,
        Dictionary<string, TypeBuilder> classTypes,
        Dictionary<string, ConstructorBuilder> classCtors)
    {
        foreach (var stmt in body)
        {
            switch (stmt)
            {
                case ClassDef cls when classTypes.ContainsKey(cls.Name):
                    // Top-level class already declared — just recurse into method bodies
                    foreach (var m in cls.Body.OfType<FunctionDef>())
                        DeclareNestedClassesInBody(m.Body, modBuilder, importMap, moduleBody,
                                                   namespaceImports, classTypes, classCtors);
                    break;

                case ClassDef cls:
                {
                    var uniqueName = $"{cls.Name}_L{cls.Line}";
                    if (!_nestedClassDefs.ContainsKey(uniqueName))
                    {
                        var ct = DeclareClass(cls, modBuilder, importMap, moduleBody, out var ctor,
                                              namespaceImports, overrideName: uniqueName, localClassTypes: classTypes);
                        _nestedClassDefs[uniqueName] = cls;
                        classTypes[uniqueName] = ct;
                        classCtors[uniqueName] = ctor;
                        _classTypes[uniqueName] = ct;
                        _classConstructors[uniqueName] = ctor;

                        // Inherit ctor arg count for nested class
                        var initFn = cls.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
                        if (initFn != null)
                        {
                            bool hasSelf = initFn.Params.Count > 0 && initFn.Params[0].Name is "self" or "cls";
                            int ac = hasSelf ? initFn.Params.Count - 1 : initFn.Params.Count;
                            _classCtorArgCounts[uniqueName] = ac;
                        }

                        // Recurse into class method bodies for further nesting
                        foreach (var m in cls.Body.OfType<FunctionDef>())
                            DeclareNestedClassesInBody(m.Body, modBuilder, importMap, moduleBody,
                                                       namespaceImports, classTypes, classCtors);
                    }
                    break;
                }

                case FunctionDef fn:
                    DeclareNestedClassesInBody(fn.Body, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    break;

                case IfStatement ifs:
                    DeclareNestedClassesInBody(ifs.Then, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    foreach (var (_, elifBody) in ifs.Elifs)
                        DeclareNestedClassesInBody(elifBody, modBuilder, importMap, moduleBody,
                                                   namespaceImports, classTypes, classCtors);
                    DeclareNestedClassesInBody(ifs.Else, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    break;

                case ForStatement forS:
                    DeclareNestedClassesInBody(forS.Body, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    DeclareNestedClassesInBody(forS.Else, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    break;

                case WhileStatement whileS:
                    DeclareNestedClassesInBody(whileS.Body, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    DeclareNestedClassesInBody(whileS.Else, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    break;

                case TryStatement tryS:
                    DeclareNestedClassesInBody(tryS.Body, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    foreach (var h in tryS.Handlers)
                        DeclareNestedClassesInBody(h.Body, modBuilder, importMap, moduleBody,
                                                   namespaceImports, classTypes, classCtors);
                    DeclareNestedClassesInBody(tryS.Else, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    DeclareNestedClassesInBody(tryS.Finally, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    break;

                case WithStatement withS:
                    DeclareNestedClassesInBody(withS.Body, modBuilder, importMap, moduleBody,
                                               namespaceImports, classTypes, classCtors);
                    break;
            }
        }
    }
}

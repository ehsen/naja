using Naja.CodeGen;
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
        // We search AppDomain loaded assemblies first so strong-named WinForms types
        // (e.g. System.Windows.Forms.Form) resolve correctly without guessing AQNs.
        var importMap = new Dictionary<string, (string TypeName, string AssemblyName)>();
        var namespaceImports = new Dictionary<string, string>();
        foreach (var stmt in module.Body)
        {
            if (stmt is FromImportStatement fis)
            {
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
            else if (stmt is ImportStatement imp)
            {
                // Handle "import System" style namespace imports
                // We don't scan assemblies here - types are resolved on-demand at access time
                foreach (var alias in imp.Names)
                {
                    var nsName = alias.Alias ?? alias.Name;
                    // Store the namespace import with null assembly - we'll search for the type at access time
                    namespaceImports[nsName] = "";
                }
            }
        }

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

        foreach (var stmt in module.Body)
        {
            switch (stmt)
            {
                case FunctionDef fn:
                    {
                        var (mb, pts) = TypeDeclaration.DeclareMethod(_model, fn, typeBuilder);
                        methods[fn.Name] = mb;
                        paramTypes[fn.Name] = pts;
                        break;
                    }
                case ClassDef cls:
                    {
                        var ct = DeclareClass(cls, modBuilder, importMap, module.Body, out var ctor);
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
                                        ct.DefineField(cn.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static);
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

        mainIL.Emit(OpCodes.Ret);

        // ── Pass 3: emit function and class bodies ────────────────────────────
        foreach (var stmt in module.Body)
        {
            if (stmt is FunctionDef fn && methods.TryGetValue(fn.Name, out var mb))
                EmitFunctionBody(fn, mb, typeBuilder, modBuilder, fields, methods, paramTypes, importMap);
            else if (stmt is ClassDef cls && classTypes.TryGetValue(cls.Name, out var ct))
                EmitClassBody(cls, ct, modBuilder, fields, methods, paramTypes, classTypes, classCtors, importMap);
        }

        // Finalise class TypeBuilders created in this module
        foreach (var ct in classTypes.Values)
        {
            try { ct.CreateType(); } catch { /* already created */ }
        }

        return typeBuilder;
    }
}

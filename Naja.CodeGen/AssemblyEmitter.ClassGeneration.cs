using Naja.CodeGen;
using Naja.Inference;
using Naja.Parser;
using Naja.Semantics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using NajaParserModule = Naja.Parser.Module;

namespace Naja.CodeGen;

/// <summary>
/// Partial class for class generation logic: TypeBuilder creation, constructor wiring,
/// instance field scanning, and dunder override generation (ToString, Equals, GetHashCode,
/// property/indexer/iterator/bool/copy support, IDisposable, ICloneable, IEnumerable).
/// </summary>
public sealed partial class AssemblyEmitter
{
    // ── Class declaration stub ────────────────────────────────────────────────

    private TypeBuilder DeclareClass(
        ClassDef cls,
        ModuleBuilder modBuilder,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap,
        IReadOnlyList<Statement> moduleBody,
        out ConstructorBuilder defaultCtor)
    {
        // Resolve base class — search loaded assemblies first to handle strong-named
        // WinForms types correctly (Type.GetType with AQN is unreliable for them).
        Type baseType = typeof(object);
        if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr baseExpr)
        {
            if (importMap.TryGetValue(baseExpr.Name, out var imp))
            {
                // Resolve from disk metadata first (Roslyn-style), then fall back
                // to AppDomain for types the host already has loaded (BCL etc.).
                baseType =
                    _typeResolver.ResolveType(imp.TypeName)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                           .Select(a => { try { return a.GetType(imp.TypeName, false, true); } catch { return null; } })
                           .FirstOrDefault(t => t is not null)
                    ?? Type.GetType($"{imp.TypeName}, {imp.AssemblyName}")
                    ?? Type.GetType(imp.TypeName)
                    ?? typeof(object);
            }
            else if (_classTypes.TryGetValue(baseExpr.Name, out var localBase))
            {
                baseType = localBase;
            }
            else
            {
                baseType = baseExpr.Name switch
                {
                    "Exception" or "BaseException" => typeof(Exception),
                    "ValueError" => typeof(ArgumentException),
                    "TypeError" => typeof(InvalidCastException),
                    "RuntimeError" => typeof(InvalidOperationException),
                    "NotImplementedError" => typeof(NotImplementedException),
                    _ => typeof(object)
                };
            }

        }

        // Check for @typing.final or @final decorator to make class sealed
        bool isFinal = cls.Decorators.Any(d =>
            d is NameExpr { Name: "final" }
            || (d is AttributeExpr a && a.Attribute == "final" && a.Object is NameExpr { Name: "typing" }));

        var typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (isFinal) typeAttrs |= TypeAttributes.Sealed;

        var tb = modBuilder.DefineType(
            cls.Name,
            typeAttrs,
            baseType);

        // ── Determine constructor parameter count ────────────────────────────
        var initFn = cls.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
        int argCount = 0;
        bool isProxy = false;

        if (initFn != null)
        {
            bool hasSelf = initFn.Params.Count > 0
                        && initFn.Params[0].Name is "self" or "cls";
            argCount = hasSelf ? initFn.Params.Count - 1 : initFn.Params.Count;
        }
        else if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr bExpr)
        {
            // Inherit signature from Naja base class if the class has NO __init__
            var baseClsDef = moduleBody.OfType<ClassDef>().FirstOrDefault(c => c.Name == bExpr.Name);
            if (baseClsDef != null)
            {
                var baseInit = baseClsDef.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
                if (baseInit != null)
                {
                    bool hasSelf = baseInit.Params.Count > 0 && baseInit.Params[0].Name is "self" or "cls";
                    argCount = hasSelf ? baseInit.Params.Count - 1 : baseInit.Params.Count;
                    isProxy = true;
                    initFn = baseInit; // Use base init for parameter names
                }
            }
        }

        var ctorParams = Enumerable.Repeat(typeof(object), argCount).ToArray();
        defaultCtor = tb.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            ctorParams);


        // Name parameters for debuggability
        if (initFn != null)
        {
            bool hasSelf = initFn.Params.Count > 0 && initFn.Params[0].Name is "self" or "cls";
            int skip = hasSelf ? 1 : 0;
            for (int i = 0; i < argCount; i++)
                defaultCtor.DefineParameter(i + 1, ParameterAttributes.None,
                    initFn.Params[i + skip].Name);
        }

        var ctorIL = defaultCtor.GetILGenerator();

        // ── Call base constructor ────────────────────────────────────────────
        // We must push the correct number of arguments to the base constructor
        // to avoid InvalidProgramException (stack imbalance).

        ConstructorInfo? baseCtor = null;
        int baseParamCount = 0;
        bool foundNajaBase = false;

        if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr bExprResolve && _classConstructors.TryGetValue(bExprResolve.Name, out var bc))
        {
            baseCtor = bc;
            _classCtorArgCounts.TryGetValue(bExprResolve.Name, out baseParamCount);
            foundNajaBase = true;
        }

        if (isProxy && baseCtor == null)
        {
            // Proxy: call base constructor with EXACTLY the same signature
            baseCtor = baseType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance, null, ctorParams, null);
            if (baseCtor != null) baseParamCount = argCount;
        }

        // Fallback or non-proxy: find any accessible constructor
        if (baseCtor == null)
        {
            var baseCtors = baseType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            baseCtor = baseCtors.FirstOrDefault(c => c.GetParameters().Length == 0)
                    ?? baseCtors.FirstOrDefault()
                    ?? typeof(object).GetConstructor(Type.EmptyTypes)!;
            baseParamCount = baseCtor.GetParameters().Length;
        }

        ctorIL.Emit(OpCodes.Ldarg_0); // this
        if (isProxy && foundNajaBase && baseParamCount == argCount)
        {
            // Pass through all arguments
            for (int i = 0; i < argCount; i++)
            {
                int argIdx = i + 1;
                if (argIdx == 1) ctorIL.Emit(OpCodes.Ldarg_1);
                else if (argIdx == 2) ctorIL.Emit(OpCodes.Ldarg_2);
                else if (argIdx == 3) ctorIL.Emit(OpCodes.Ldarg_3);
                else ctorIL.Emit(OpCodes.Ldarg_S, (byte)argIdx);
            }
        }
        else
        {
            // Push nulls for any required arguments
            for (int i = 0; i < baseParamCount; i++)
                ctorIL.Emit(OpCodes.Ldnull);
        }

        ctorIL.Emit(OpCodes.Call, baseCtor);

        if (isProxy)
        {
            ctorIL.Emit(OpCodes.Ret);
        }
        else
        {
            // ── Deferred constructor completion ──────────────────────────────
            // At Pass 1 time the __init__ MethodBuilder does not exist yet —
            // it is declared in Pass 1.5.  Emitting Call on a MethodBuilder
            // that belongs to a type not yet created causes InvalidProgramException
            // with both AssemblyBuilder.Run and PersistedAssemblyBuilder.
            //
            // Solution: leave the ILGenerator open (no Ret here) and store it
            // so EmitClassBody (Pass 3) can complete the constructor after
            // __init__ is available. EmitClassBody calls CompleteConstructor().
            _pendingCtorIL[cls.Name] = (ctorIL, argCount);
            // Ret is emitted by CompleteConstructor — NOT here.
        }

        return tb;

    }

    // ── Deferred constructor completion ───────────────────────────────────────

    /// <summary>
    /// Completes the constructor body left open by DeclareClass (Pass 1).
    /// Called at the START of EmitClassBody (Pass 3) after Pass 1.5 has
    /// populated _classMethods with the __init__ MethodBuilder.
    ///
    /// Emits:
    ///   ldarg.0         (self)
    ///   ldarg.1 … N    (constructor parameters)
    ///   call __init__
    ///   [pop if non-void]
    ///   ret
    ///
    /// If no __init__ exists, emits ret only so the constructor is valid IL.
    /// </summary>
    private void CompleteConstructor(string className)
    {
        if (!_pendingCtorIL.TryGetValue(className, out var pending))
            return; // proxy ctor or already completed

        var (ctorIL, argCount) = pending;
        _pendingCtorIL.Remove(className);

        string key = $"{className}.__init__";
        if (_classMethods.TryGetValue(key, out var initMb))
        {
            ctorIL.Emit(OpCodes.Ldarg_0); // self
            for (int i = 0; i < argCount; i++)
            {
                int argIdx = i + 1;
                if (argIdx == 1) ctorIL.Emit(OpCodes.Ldarg_1);
                else if (argIdx == 2) ctorIL.Emit(OpCodes.Ldarg_2);
                else if (argIdx == 3) ctorIL.Emit(OpCodes.Ldarg_3);
                else ctorIL.Emit(OpCodes.Ldarg_S, (byte)argIdx);
            }
            ctorIL.Emit(OpCodes.Call, initMb);
            if (initMb.ReturnType != typeof(void))
                ctorIL.Emit(OpCodes.Pop);
        }

        ctorIL.Emit(OpCodes.Ret);
    }

    // ── Instance-field scanner ────────────────────────────────────────────────

    /// <summary>
    /// Recursively walks all statements looking for self.x = ... assignments
    /// so every instance field used anywhere in a method body gets declared on
    /// the TypeBuilder before IL emission begins.
    /// </summary>
    private static void ScanForInstanceFields(
        IReadOnlyList<Statement> stmts,
        Dictionary<string, FieldBuilder> instanceFields,
        TypeBuilder ct)
    {
        foreach (var stmt in stmts)
        {
            switch (stmt)
            {
                case AssignStatement assign:
                    foreach (var t in assign.Targets)
                        TryDeclareInstanceField(t, instanceFields, ct);
                    break;

                case AnnAssignStatement ann:
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
                    TryDeclareInstanceField(ann.Target, instanceFields, ct, isFinal);
                    break;

                case IfStatement ifs:
                    ScanForInstanceFields(ifs.Then, instanceFields, ct);
                    foreach (var (_, body) in ifs.Elifs)
                        ScanForInstanceFields(body, instanceFields, ct);
                    ScanForInstanceFields(ifs.Else, instanceFields, ct);
                    break;

                case ForStatement forS:
                    ScanForInstanceFields(forS.Body, instanceFields, ct);
                    ScanForInstanceFields(forS.Else, instanceFields, ct);
                    break;

                case WhileStatement whileS:
                    ScanForInstanceFields(whileS.Body, instanceFields, ct);
                    ScanForInstanceFields(whileS.Else, instanceFields, ct);
                    break;

                case TryStatement tryS:
                    ScanForInstanceFields(tryS.Body, instanceFields, ct);
                    foreach (var h in tryS.Handlers)
                        ScanForInstanceFields(h.Body, instanceFields, ct);
                    ScanForInstanceFields(tryS.Else, instanceFields, ct);
                    ScanForInstanceFields(tryS.Finally, instanceFields, ct);
                    break;

                case WithStatement withS:
                    ScanForInstanceFields(withS.Body, instanceFields, ct);
                    break;
            }
        }
    }

    private static void TryDeclareInstanceField(
    Expression target,
    Dictionary<string, FieldBuilder> instanceFields,
    TypeBuilder ct,
    bool isFinal = false)
    {
        // Matches: self.x = ...  or  cls.x = ...
        if (target is AttributeExpr attr
            && attr.Object is NameExpr { Name: "self" or "cls" }
            && !instanceFields.ContainsKey(attr.Attribute))
        {
            // 1. PREVENT SHADOWING BASE CLASS PROPERTIES (e.g. Form.Text, Form.Size)
            if (ct.BaseType != null)
            {
                var existingMembers = ct.BaseType.GetMember(
                    attr.Attribute,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (existingMembers.Length > 0)
                {
                    // Base class already has this member. By returning here, we force
                    // StatementEmitter to fall back to NajaBuiltins.SetAttr at runtime, 
                    // which will correctly trigger the WinForms property setters!
                    return;
                }
            }

            // 2. Define the field normally for custom Python state
            bool isPriv = attr.Attribute.StartsWith('_')
                       && !(attr.Attribute.StartsWith("__") && attr.Attribute.EndsWith("__"));
            var fbAttrs = isPriv ? FieldAttributes.Private : FieldAttributes.Public;
            if (isFinal) fbAttrs |= FieldAttributes.InitOnly;

            instanceFields[attr.Attribute] = ct.DefineField(attr.Attribute, typeof(object), fbAttrs);
        }
    }

    // ── Class body emission ───────────────────────────────────────────────────

    private void EmitClassBody(
        ClassDef cls,
        TypeBuilder ct,
        ModuleBuilder modBuilder,
        Dictionary<string, FieldBuilder> moduleFields,
        Dictionary<string, MethodBuilder> moduleMethods,
        Dictionary<string, Type[]> moduleParamTypes,
        Dictionary<string, TypeBuilder> moduleClassTypes,
        Dictionary<string, ConstructorBuilder> moduleClassCtors,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap)
    {
        var instanceFields = new Dictionary<string, FieldBuilder>();
        var classMethods = new Dictionary<string, MethodBuilder>();
        var classParamTs = new Dictionary<string, Type[]>();

        // ── Scan for class-level field declarations (AssignStatement and AnnAssignStatement) ──
        foreach (var member in cls.Body)
        {
            switch (member)
            {
                case AssignStatement assign:
                    foreach (var target in assign.Targets)
                        TryDeclareInstanceField(target, instanceFields, ct);
                    break;

                case AnnAssignStatement ann:
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
                    TryDeclareInstanceField(ann.Target, instanceFields, ct, isFinal);
                    break;
            }
        }

        // ── Scan for all instance fields (deep — covers if/for/try/with) ──────
        foreach (var member in cls.Body)
            if (member is FunctionDef fn)
                ScanForInstanceFields(fn.Body, instanceFields, ct);

        // ── Use pre-declared instance method stubs (from Pass 1.5) ────────────
        foreach (var member in cls.Body)
        {
            if (member is not FunctionDef fn) continue;

            // Resolve unique name (get_/set_ if property)
            bool isPropStub = fn.Decorators.Any(d => d is NameExpr { Name: "property" });
            bool isSetterStub = fn.Decorators.Any(d => d is AttributeExpr a && a.Attribute == "setter");
            string uname = isPropStub ? "get_" + fn.Name : isSetterStub ? "set_" + fn.Name : fn.Name;

            string key = $"{cls.Name}.{uname}";
            if (_classMethods.TryGetValue(key, out var mb))
            {
                classMethods[uname] = mb;
                classParamTs[uname] = _classMethodParamTypes[key];
            }
        }

        // ── Complete the deferred constructor now that __init__ is available ───
        // DeclareClass (Pass 1) left the constructor ILGenerator open after the
        // base ctor call. Now that _classMethods has the __init__ MethodBuilder
        // from Pass 1.5, we can emit the __init__ call and Ret.
        CompleteConstructor(cls.Name);


        // ── Dispose / IDisposable wiring ──────────────────────────────────────
        var delFn = cls.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__del__");
        var disposeBoolBase = (ct.BaseType != null && ct.BaseType is not TypeBuilder)
            ? ct.BaseType.GetMethod("Dispose",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null)
            : null;


        if (delFn != null || disposeBoolBase != null)
        {
            if (disposeBoolBase != null
                && (delFn != null
                    || ct.BaseType?.FullName?.StartsWith("System.Windows.Forms") == true))
            {
                var dispMb = ct.DefineMethod("Dispose",
                    MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    typeof(void), new[] { typeof(bool) });
                dispMb.DefineParameter(1, ParameterAttributes.None, "disposing");

                var dil = dispMb.GetILGenerator();
                if (delFn != null && classMethods.TryGetValue("__del__", out var delMb))
                {
                    var lbl = dil.DefineLabel();
                    dil.Emit(OpCodes.Ldarg_1);
                    dil.Emit(OpCodes.Brfalse, lbl);
                    dil.Emit(OpCodes.Ldarg_0);
                    dil.Emit(OpCodes.Call, delMb);
                    if (delMb.ReturnType != typeof(void)) dil.Emit(OpCodes.Pop);
                    dil.MarkLabel(lbl);
                }
                dil.Emit(OpCodes.Ldarg_0);
                dil.Emit(OpCodes.Ldarg_1);
                dil.Emit(OpCodes.Call, disposeBoolBase);
                dil.Emit(OpCodes.Ret);
                ct.DefineMethodOverride(dispMb, disposeBoolBase);
            }
            else if (delFn != null
                     && classMethods.TryGetValue("__del__", out var delMb2)
                     && !typeof(IDisposable).IsAssignableFrom(ct.BaseType ?? typeof(object)))
            {
                ct.AddInterfaceImplementation(typeof(IDisposable));
                var dispMb = ct.DefineMethod("Dispose",
                    MethodAttributes.Public | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                    typeof(void), Type.EmptyTypes);
                var dil = dispMb.GetILGenerator();
                dil.Emit(OpCodes.Ldarg_0);
                dil.Emit(OpCodes.Call, delMb2);
                if (delMb2.ReturnType != typeof(void)) dil.Emit(OpCodes.Pop);
                dil.Emit(OpCodes.Ret);
            }
        }

        // ── Emit method bodies ────────────────────────────────────────────────
        foreach (var member in cls.Body)
        {
            if (member is not FunctionDef fn) continue;

            bool isProp = fn.Decorators.Any(d => d is NameExpr { Name: "property" });
            bool isSetter = fn.Decorators.Any(d => d is AttributeExpr a && a.Attribute == "setter");
            string uname = isProp ? "get_" + fn.Name : isSetter ? "set_" + fn.Name : fn.Name;

            if (classMethods.TryGetValue(uname, out var mb))
                EmitMethodBody(fn, mb, ct, modBuilder,
                               moduleFields, moduleMethods, moduleParamTypes,
                               moduleClassTypes, moduleClassCtors,
                               instanceFields, classMethods, classParamTs, importMap,
                               new Dictionary<string, string>()); // Namespace imports not yet passed to class methods
        }

        // ── Generate CLR property wrappers ────────────────────────────────────
        var propNames = cls.Body.OfType<FunctionDef>()
            .Where(f => f.Decorators.Any(d =>
                d is NameExpr { Name: "property" }
                || (d is AttributeExpr a && a.Attribute == "setter")))
            .Select(f => f.Name)
            .Distinct();

        foreach (var pName in propNames)
        {
            classMethods.TryGetValue("get_" + pName, out var getMb);
            classMethods.TryGetValue("set_" + pName, out var setMb);
            if (getMb == null && setMb == null) continue;
            var pb = ct.DefineProperty(pName, PropertyAttributes.None, typeof(object), null);
            if (getMb != null) pb.SetGetMethod(getMb);
            if (setMb != null) pb.SetSetMethod(setMb);
        }

        // ── Dunder overrides ──────────────────────────────────────────────────
        if (classMethods.TryGetValue("__str__", out var strMb))
        {
            var tsMb = ct.DefineMethod("ToString",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(string), Type.EmptyTypes);
            var dil = tsMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, strMb);
            dil.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToString", new[] { typeof(object) })!);
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(tsMb, typeof(object).GetMethod("ToString")!);
        }

        if (classMethods.TryGetValue("__eq__", out var eqMb))
        {
            var eqOvr = ct.DefineMethod("Equals",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(bool), new[] { typeof(object) });
            var dil = eqOvr.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldarg_1);
            dil.Emit(OpCodes.Call, eqMb);
            dil.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToBoolean", new[] { typeof(object) })!);
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(eqOvr, typeof(object).GetMethod("Equals", new[] { typeof(object) })!);
        }

        if (classMethods.TryGetValue("__hash__", out var hashMb))
        {
            var ghMb = ct.DefineMethod("GetHashCode",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(int), Type.EmptyTypes);
            var dil = ghMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, hashMb);
            dil.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", new[] { typeof(object) })!);
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(ghMb, typeof(object).GetMethod("GetHashCode")!);
        }

        // __len__ → Count property (for ICollection compatibility)
        if (classMethods.TryGetValue("__len__", out var lenMb))
        {
            var countProp = ct.DefineProperty("Count", PropertyAttributes.None, typeof(int), null);
            var getCountMb = ct.DefineMethod("get_Count",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                typeof(int), Type.EmptyTypes);
            var dil = getCountMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, lenMb);
            dil.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", new[] { typeof(object) })!);
            dil.Emit(OpCodes.Ret);
            countProp.SetGetMethod(getCountMb);
        }

        // __bool__ → op_True, op_False, op_Implicit
        // Python __bool__ maps to three .NET operator methods:
        //   op_True     → result of __bool__
        //   op_False    → logical negation of __bool__
        //   op_Implicit → same as op_True (implicit bool conversion)
        if (classMethods.TryGetValue("__bool__", out var boolMb))
        {
            var selfParam = new[] { ct.AsType() };

            foreach (var opName in new[] { "op_True", "op_False", "op_Implicit" })
            {
                var opMb = ct.DefineMethod(
                    opName,
                    MethodAttributes.Public | MethodAttributes.Static
                        | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    typeof(bool),
                    selfParam);
                opMb.DefineParameter(1, ParameterAttributes.None, "obj");

                var oil = opMb.GetILGenerator();
                oil.Emit(OpCodes.Ldarg_0);      // push obj (self)
                oil.Emit(OpCodes.Call, boolMb); // call __bool__(self)

                // __bool__ returns object — convert to bool
                if (boolMb.ReturnType != typeof(bool))
                    oil.Emit(OpCodes.Call,
                        typeof(Convert).GetMethod("ToBoolean", new[] { typeof(object) })!);

                if (opName == "op_False")
                {
                    // Negate: ldc.i4.0 + ceq is the standard IL NOT pattern
                    oil.Emit(OpCodes.Ldc_I4_0);
                    oil.Emit(OpCodes.Ceq);
                }

                oil.Emit(OpCodes.Ret);
            }
        }

        // __copy__ → ICloneable.Clone
        if (classMethods.TryGetValue("__copy__", out var copyMb))
        {
            ct.AddInterfaceImplementation(typeof(ICloneable));
            var cloneMb = ct.DefineMethod("Clone",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(object), Type.EmptyTypes);
            var dil = cloneMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, copyMb);
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(cloneMb, typeof(ICloneable).GetMethod("Clone")!);
        }

        // __getitem__/__setitem__ → Indexer with DefaultMemberAttribute
        bool hasGetItem = classMethods.TryGetValue("__getitem__", out var getItemMb);
        bool hasSetItem = classMethods.TryGetValue("__setitem__", out var setItemMb);
        if (hasGetItem || hasSetItem)
        {
            // Add DefaultMemberAttribute for indexer support
            var defaultMemberCtor = typeof(DefaultMemberAttribute).GetConstructor(new[] { typeof(string) })!;
            ct.SetCustomAttribute(new CustomAttributeBuilder(defaultMemberCtor, new object[] { "Item" }));

            var indexerProp = ct.DefineProperty("Item", PropertyAttributes.None, typeof(object), new[] { typeof(object) });

            if (hasGetItem)
            {
                var getIndexerMb = ct.DefineMethod("get_Item",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    typeof(object), new[] { typeof(object) });
                getIndexerMb.DefineParameter(1, ParameterAttributes.None, "key");
                var dil = getIndexerMb.GetILGenerator();
                dil.Emit(OpCodes.Ldarg_0);
                dil.Emit(OpCodes.Ldarg_1);
                dil.Emit(OpCodes.Call, getItemMb);
                dil.Emit(OpCodes.Ret);
                indexerProp.SetGetMethod(getIndexerMb);
            }

            if (hasSetItem)
            {
                var setIndexerMb = ct.DefineMethod("set_Item",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    typeof(void), new[] { typeof(object), typeof(object) });
                setIndexerMb.DefineParameter(1, ParameterAttributes.None, "key");
                setIndexerMb.DefineParameter(2, ParameterAttributes.None, "value");
                var dil = setIndexerMb.GetILGenerator();
                dil.Emit(OpCodes.Ldarg_0);
                dil.Emit(OpCodes.Ldarg_1);
                dil.Emit(OpCodes.Ldarg_2);
                dil.Emit(OpCodes.Call, setItemMb);
                if (setItemMb.ReturnType != typeof(void)) dil.Emit(OpCodes.Pop);
                dil.Emit(OpCodes.Ret);
                indexerProp.SetSetMethod(setIndexerMb);
            }
        }

        // __iter__/__next__ → IEnumerable/IEnumerator
        bool hasIter = classMethods.TryGetValue("__iter__", out var iterMb);
        bool hasNext = classMethods.TryGetValue("__next__", out var nextMb);
        if (hasIter)
        {
            ct.AddInterfaceImplementation(typeof(IEnumerable));
            var getEnumMb = ct.DefineMethod("GetEnumerator",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(System.Collections.IEnumerator), Type.EmptyTypes);
            var dil = getEnumMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, iterMb);
            // If __iter__ returns 'this', we need to box it for IEnumerator
            if (hasNext)
            {
                // The class implements IEnumerator via __next__
                ct.AddInterfaceImplementation(typeof(IEnumerator));
                if (nextMb.ReturnType.IsValueType)
                    dil.Emit(OpCodes.Box, nextMb.ReturnType);
            }
            dil.Emit(OpCodes.Castclass, typeof(IEnumerator));
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(getEnumMb, typeof(IEnumerable).GetMethod("GetEnumerator")!);
        }

        // Implement IEnumerator interface if __next__ is present
        if (hasNext)
        {
            ct.AddInterfaceImplementation(typeof(IEnumerator));

            // Define fields to store the current value and exhausted state
            var currentValueField = ct.DefineField("__current_value", typeof(object), FieldAttributes.Private);
            var exhaustedField = ct.DefineField("__exhausted", typeof(bool), FieldAttributes.Private);

            // Current property
            var currentProp = ct.DefineProperty("Current", PropertyAttributes.None, typeof(object), null);

            // get_Current - returns the stored current value
            var getCurrentMb = ct.DefineMethod("get_Current",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(object), Type.EmptyTypes);
            var curIl = getCurrentMb.GetILGenerator();
            curIl.Emit(OpCodes.Ldarg_0);
            curIl.Emit(OpCodes.Ldfld, currentValueField);
            curIl.Emit(OpCodes.Ret);
            currentProp.SetGetMethod(getCurrentMb);

            // MoveNext - call __next__ and handle StopIteration via runtime helper
            var moveNextMb = ct.DefineMethod("MoveNext",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(bool), Type.EmptyTypes);
            var mnIl = moveNextMb.GetILGenerator();

            // Call NajaBuiltins.IteratorMoveNext(__next__ method delegate, currentField, exhaustedField)
            mnIl.Emit(OpCodes.Ldarg_0);  // this (for method call)
            mnIl.Emit(OpCodes.Ldftn, nextMb);  // method pointer
            mnIl.Emit(OpCodes.Newobj, typeof(Func<object>).GetConstructor(new[] { typeof(object), typeof(IntPtr) })!);
            mnIl.Emit(OpCodes.Ldarg_0);  // this for field addresses
            mnIl.Emit(OpCodes.Ldflda, currentValueField);  // ref to current value field
            mnIl.Emit(OpCodes.Ldarg_0);
            mnIl.Emit(OpCodes.Ldflda, exhaustedField);  // ref to exhausted field
            mnIl.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.IteratorMoveNext))!);
            mnIl.Emit(OpCodes.Ret);

            // Reset - throw NotSupportedException
            var resetMb = ct.DefineMethod("Reset",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(void), Type.EmptyTypes);
            var rIl = resetMb.GetILGenerator();
            rIl.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
            rIl.Emit(OpCodes.Throw);

            ct.DefineMethodOverride(getCurrentMb, typeof(IEnumerator).GetProperty("Current")!.GetMethod!);
            ct.DefineMethodOverride(moveNextMb, typeof(IEnumerator).GetMethod("MoveNext")!);
            ct.DefineMethodOverride(resetMb, typeof(IEnumerator).GetMethod("Reset")!);
        }
    }
}

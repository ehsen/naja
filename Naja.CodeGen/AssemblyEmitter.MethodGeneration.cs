using Naja.CodeGen;
using Naja.Inference;
using Naja.Parser;
using Naja.Semantics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NajaParserModule = Naja.Parser.Module;

namespace Naja.CodeGen;

/// <summary>
/// Partial class for method and function generation logic: instance method declaration/emission,
/// module-level function declaration/emission with generator and closure support.
/// </summary>
public sealed partial class AssemblyEmitter
{
    // ── Instance method declaration ───────────────────────────────────────────

    private (MethodBuilder mb, Type[] paramTypes, string uniqueName) DeclareInstanceMethod(
        FunctionDef fn, TypeBuilder ct)
    {
        if (fn.IsAsync)
            throw new CodeGenException("async/await is not yet supported.", fn.Line, fn.Column);

        bool hasSelf = fn.Params.Count > 0 && fn.Params[0].Name is "self" or "cls";
        var clrParams = fn.Params
            .Skip(hasSelf ? 1 : 0)
            .Select(_ => typeof(object))
            .ToArray();

        bool isStatic = fn.Decorators.Any(d => d is NameExpr { Name: "staticmethod" or "classmethod" });
        bool isFinal = fn.Decorators.Any(d =>
            d is NameExpr { Name: "final" }
            || (d is AttributeExpr a && a.Attribute == "final" && a.Object is NameExpr { Name: "typing" }));
        bool isProp = fn.Decorators.Any(d => d is NameExpr { Name: "property" });
        bool isSetter = fn.Decorators.Any(d => d is AttributeExpr a2 && a2.Attribute == "setter");

        string uniqueName = isProp ? "get_" + fn.Name
                          : isSetter ? "set_" + fn.Name
                          : fn.Name;
        string emitName = uniqueName;

        bool isPriv = fn.Name.StartsWith('_')
                   && !(fn.Name.StartsWith("__") && fn.Name.EndsWith("__"));

        var attrs = isPriv ? MethodAttributes.Private : MethodAttributes.Public;

        if (isStatic)
        {
            attrs |= MethodAttributes.Static;
        }
        else
        {
            attrs |= MethodAttributes.Virtual | MethodAttributes.HideBySig;

            // Avoid reflection on TypeBuilder base
            MethodInfo? baseMethod = null;
            if (ct.BaseType != null && ct.BaseType is not TypeBuilder)
            {
                baseMethod = ct.BaseType.GetMethod(
                    emitName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, clrParams, null);
            }

            if (baseMethod == null || !baseMethod.IsVirtual || baseMethod.IsFinal)
                attrs |= MethodAttributes.NewSlot;
        }


        if (isFinal && !isStatic) attrs |= MethodAttributes.Final;
        if (isProp || isSetter) attrs |= MethodAttributes.SpecialName;

        var mb = ct.DefineMethod(emitName, attrs, typeof(object), clrParams);

        if (!isStatic)
            for (int i = 0; i < clrParams.Length; i++)
                mb.DefineParameter(i + 1, ParameterAttributes.None,
                    fn.Params[i + (hasSelf ? 1 : 0)].Name);

        return (mb, clrParams, uniqueName);
    }

    // ── Instance method body emission ─────────────────────────────────────────

    private void EmitMethodBody(
        FunctionDef fn,
        MethodBuilder mb,
        TypeBuilder ct,
        ModuleBuilder modBuilder,
        Dictionary<string, FieldBuilder> moduleFields,
        Dictionary<string, MethodBuilder> moduleMethods,
        Dictionary<string, Type[]> moduleParamTypes,
        Dictionary<string, TypeBuilder> moduleClassTypes,
        Dictionary<string, ConstructorBuilder> moduleClassCtors,
        Dictionary<string, FieldBuilder> instanceFields,
        Dictionary<string, MethodBuilder> classMethods,
        Dictionary<string, Type[]> classParamTs,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap,
        Dictionary<string, string> namespaceImports)
    {
        var il = mb.GetILGenerator();
        bool isStatic = fn.Decorators.Any(d => d is NameExpr { Name: "staticmethod" or "classmethod" });
        bool hasSelf = !isStatic && fn.Params.Count > 0 && fn.Params[0].Name is "self" or "cls";

        var paramNames = fn.Params.Skip(hasSelf ? 1 : 0).Select(p => p.Name).ToList();

        var ctx = new EmitContext(il, _model, ct, modBuilder, typeof(object), paramNames);
        ctx.IsInstanceMethod = !isStatic;
        ctx.SelfName = hasSelf ? fn.Params[0].Name : null;
        ctx.IsInsideFunction = true;  // Fix 19: Mark as inside function for yield check

        foreach (var (k, v) in moduleFields) ctx.Fields[k] = v;
        foreach (var (k, v) in moduleMethods) ctx.Methods[k] = v;
        foreach (var (k, v) in moduleParamTypes) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in moduleClassTypes) ctx.ClassTypes[k] = v;
        foreach (var (k, v) in moduleClassCtors) ctx.ClassConstructors[k] = v;
        foreach (var (k, v) in _classCtorArgCounts) ctx.ClassCtorArgCounts[k] = v;
        foreach (var (k, v) in _classMethods) ctx.AllClassMethods[k] = v;

        foreach (var (k, v) in _classMethodParamTypes) ctx.AllClassMethodParamTypes[k] = v;
        foreach (var (k, v) in instanceFields) ctx.InstanceFields[k] = v;
        foreach (var (k, v) in classMethods) ctx.Methods[k] = v;
        foreach (var (k, v) in classParamTs) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in importMap) ctx.ImportMap[k] = v;
        foreach (var (k, v) in namespaceImports) ctx.NamespaceImports[k] = v;
        foreach (var mn in _classMethodNames) ctx.ClassMethods.Add(mn);

        // Hoist variables referenced by nested functions into static fields,
        // mirroring the same pass that EmitFunctionBody does for module-level functions.
        // Without this, inner defs inside class methods cannot reach enclosing locals.
        var nestedRefsM    = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.CollectNamesReferencedByNestedFunctions(fn.Body);
        var assignedM      = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.CollectAssignedNames(fn.Body);
        var paramNamesSetM = new HashSet<string>(fn.Params.Select(p => p.Name));
        foreach (var r in nestedRefsM.Intersect(assignedM.Union(paramNamesSetM)))
        {
            if (!ctx.Fields.ContainsKey(r))
            {
                var fb = ct.DefineField($"__nl_{fn.Name}_{r}", typeof(object),
                                        FieldAttributes.Public | FieldAttributes.Static);
                ctx.Fields[r] = fb;
            }
            if (paramNamesSetM.Contains(r))
                ctx.HoistedParams.Add(r);
        }
        // Copy hoisted parameter values into their static fields at method entry.
        // Arg indices mirror fn.Params: for instance methods arg 0 = self, arg 1 = first real param.
        for (int hi = 0; hi < fn.Params.Count; hi++)
        {
            if (!ctx.HoistedParams.Contains(fn.Params[hi].Name)) continue;
            switch (hi) { case 0: il.Emit(OpCodes.Ldarg_0); break; case 1: il.Emit(OpCodes.Ldarg_1); break; case 2: il.Emit(OpCodes.Ldarg_2); break; case 3: il.Emit(OpCodes.Ldarg_3); break; default: il.Emit(OpCodes.Ldarg_S, (byte)hi); break; }
            il.Emit(OpCodes.Stsfld, ctx.Fields[fn.Params[hi].Name]);
        }

        var emitter = new StatementEmitter(ctx);
        emitter.EmitAll(fn.Body);

        // Capture any methods/types/fields registered by inner defs (nested functions,
        // nested classes, hoisted locals) so nested class bodies in Pass 3 can resolve them.
        foreach (var (k, v) in ctx.Methods) _innerFunctionMethods.TryAdd(k, v);
        foreach (var (k, v) in ctx.ClassTypes) _innerFunctionClassTypes.TryAdd(k, v);
        foreach (var (k, v) in ctx.ClassConstructors) _innerFunctionClassCtors.TryAdd(k, v);
        foreach (var (k, v) in ctx.Fields) _innerFunctionFields.TryAdd(k, v);

        EmitMethodEpilog(il, ctx, typeof(object));
    }

    // ── Module-level function declaration stub ────────────────────────────────

    private (MethodBuilder mb, Type[] paramTypes) DeclareMethod(FunctionDef fn, TypeBuilder tb)
    {
        if (fn.IsAsync)
            throw new CodeGenException("async/await is not yet supported.", fn.Line, fn.Column);

        // Reject duplicate parameter names
        var seenParams = new HashSet<string>();
        foreach (var p in fn.Params)
            if (!seenParams.Add(p.Name))
                throw new CodeGenException(
                    $"duplicate argument '{p.Name}' in function definition", fn.Line, fn.Column);

        var sym = _model.ModuleScope.Lookup(fn.Name);
        var fnType = sym?.Type as FunctionType;
        var retType = fnType is not null
            ? TypeMapper.ToReturnType(fnType.ReturnType)
            : typeof(object);

        var pts = fn.Params.Select((p, i) =>
        {
            var pType = fnType?.ParamTypes.ElementAtOrDefault(i);
            var clr = pType is not null ? TypeMapper.ToClrType(pType) : typeof(object);
            return clr == typeof(void) ? typeof(object) : clr;
        }).ToArray();

        var mb = tb.DefineMethod(
            fn.Name,
            MethodAttributes.Public | MethodAttributes.Static,
            retType,
            pts);

        for (int i = 0; i < fn.Params.Count; i++)
            mb.DefineParameter(i + 1, ParameterAttributes.None, fn.Params[i].Name);

        return (mb, pts);
    }

    // ── Module-level function body emission ───────────────────────────────────

    private void EmitFunctionBody(
        FunctionDef fn,
        MethodBuilder mb,
        TypeBuilder tb,
        ModuleBuilder modBuilder,
        Dictionary<string, FieldBuilder> fields,
        Dictionary<string, MethodBuilder> methods,
        Dictionary<string, Type[]> paramTypes,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap)
    {
        var il = mb.GetILGenerator();
        var sym = _model.ModuleScope.Lookup(fn.Name);
        var fnType = sym?.Type as FunctionType;
        var returnType = fnType is not null
            ? TypeMapper.ToReturnType(fnType.ReturnType)
            : typeof(object);

        var paramNames = fn.Params.Select(p => p.Name).ToList();
        var ctx = new EmitContext(il, _model, tb, modBuilder, returnType, paramNames);
        ctx.IsInsideFunction = true;  // Fix 19: Mark as inside function for yield check

        foreach (var (k, v) in fields) ctx.Fields[k] = v;
        foreach (var (k, v) in methods) ctx.Methods[k] = v;
        foreach (var (k, v) in paramTypes) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in _classTypes) ctx.ClassTypes[k] = v;
        foreach (var (k, v) in _classConstructors) ctx.ClassConstructors[k] = v;
        foreach (var mn in _classMethodNames) ctx.ClassMethods.Add(mn);
        foreach (var (k, v) in importMap) ctx.ImportMap[k] = v;

        // LEGB: locally-assigned names shadow module-level fields unless declared 'global'.
        // Remove the module field from ctx so the emitter creates a proper local instead.
        var fnGlobals = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.CollectGlobalNames(fn.Body);
        var fnAssigned = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.CollectAssignedNames(fn.Body);
        foreach (var name in fnAssigned)
            if (!fnGlobals.Contains(name))
                ctx.Fields.Remove(name);

        // Hoist variables referenced by nested functions, including parameters:
        // if an inner function references a name that is assigned *or passed as a
        // parameter* in this function, promote it to a static field so the inner
        // function can reach the enclosing binding (Python LEGB / closure semantics).
        var nestedRefs = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.CollectNamesReferencedByNestedFunctions(fn.Body);
        var assigned = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.CollectAssignedNames(fn.Body);
        var paramNamesSet = new HashSet<string>(fn.Params.Select(p => p.Name));
        var cellVarNames = new List<string>();
        foreach (var r in nestedRefs.Intersect(assigned.Union(paramNamesSet)))
        {
            if (paramNamesSet.Contains(r))
            {
                // Parameter captured by inner function → static field (value snapshot via NajaFunction)
                var fb = tb.DefineField($"__nl_{r}", typeof(object), FieldAttributes.Private | FieldAttributes.Static);
                ctx.Fields[r] = fb;
                ctx.HoistedParams.Add(r);
            }
            else
            {
                // Local var captured by inner function → per-call object[1] cell
                cellVarNames.Add(r);
            }
        }
        // Copy hoisted parameter values into their static fields at function entry
        for (int hi = 0; hi < fn.Params.Count; hi++)
        {
            if (!ctx.HoistedParams.Contains(fn.Params[hi].Name)) continue;
            switch (hi) { case 0: il.Emit(OpCodes.Ldarg_0); break; case 1: il.Emit(OpCodes.Ldarg_1); break; case 2: il.Emit(OpCodes.Ldarg_2); break; case 3: il.Emit(OpCodes.Ldarg_3); break; default: il.Emit(OpCodes.Ldarg_S, (byte)hi); break; }
            il.Emit(OpCodes.Stsfld, ctx.Fields[fn.Params[hi].Name]);
        }
        // Allocate per-call cells for local vars captured by nested functions
        foreach (var cv in cellVarNames)
        {
            var cellLocal = ctx.Locals.Declare($"__cell_{cv}", typeof(object[]));
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, typeof(object));
            il.Emit(OpCodes.Stloc, cellLocal);
            ctx.CellLocals[cv] = cellLocal;
        }

        // Check if this function contains yield statements (is a generator)
        bool isGenerator = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.ContainsYield(fn.Body);
        if (isGenerator)
        {
            // ── Generator: two-method compilation ─────────────────────────────────
            // Wrapper (mb): packs original args into object[] and returns a NajaGenerator.
            // Body (__gen_body__): void (NajaGenerator, object[]) — runs on a background
            // thread managed by NajaGenerator; yield → gen.Yield(v); return v → throw NajaGeneratorReturn(v).

            var bodyMethod = tb.DefineMethod(
                fn.Name + "__gen_body__",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(void),
                new Type[] { typeof(NajaGenerator), typeof(object[]) });
            bodyMethod.DefineParameter(1, ParameterAttributes.None, "__gen");
            bodyMethod.DefineParameter(2, ParameterAttributes.None, "__args");

            // Emit wrapper: build args[], create delegate, new NajaGenerator, ret
            // Compute param CLR types so value types (long, double, bool) can be
            // boxed to object before storing into the object[] args array.
            var wrapperPts = fn.Params.Select((p, i) =>
            {
                var pType = fnType?.ParamTypes.ElementAtOrDefault(i);
                var clr = pType is not null ? TypeMapper.ToClrType(pType) : typeof(object);
                return clr == typeof(void) ? typeof(object) : clr;
            }).ToArray();
            il.Emit(OpCodes.Ldc_I4, fn.Params.Count);
            il.Emit(OpCodes.Newarr, typeof(object));
            for (int wi = 0; wi < fn.Params.Count; wi++)
            {
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4, wi);
                switch (wi) { case 0: il.Emit(OpCodes.Ldarg_0); break; case 1: il.Emit(OpCodes.Ldarg_1); break; case 2: il.Emit(OpCodes.Ldarg_2); break; case 3: il.Emit(OpCodes.Ldarg_3); break; default: il.Emit(OpCodes.Ldarg_S, (byte)wi); break; }
                if (wrapperPts[wi].IsValueType)
                    il.Emit(OpCodes.Box, wrapperPts[wi]);
                il.Emit(OpCodes.Stelem_Ref);
            }
            var wrapperArgsLocal = ctx.Locals.Declare("__gen_args", typeof(object[]));
            il.Emit(OpCodes.Stloc, wrapperArgsLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, bodyMethod);
            il.Emit(OpCodes.Newobj, typeof(Action<NajaGenerator, object[]>).GetConstructors()[0]);
            il.Emit(OpCodes.Ldloc, wrapperArgsLocal);
            il.Emit(OpCodes.Newobj, NajaBuiltinsMethodCache.NajaGenerator_Ctor);
            il.Emit(OpCodes.Ret);

            // Build body method context
            var bodyIL = bodyMethod.GetILGenerator();
            var bodyCtx = new EmitContext(bodyIL, _model, tb, modBuilder, typeof(void),
                                          Array.Empty<string>());
            bodyCtx.IsInsideFunction = true;
            bodyCtx.IsGeneratorBody = true;

            foreach (var (k, v) in fields) bodyCtx.Fields[k] = v;
            foreach (var (k, v) in methods) bodyCtx.Methods[k] = v;
            foreach (var (k, v) in paramTypes) bodyCtx.MethodParamTypes[k] = v;
            foreach (var (k, v) in _classTypes) bodyCtx.ClassTypes[k] = v;
            foreach (var (k, v) in _classConstructors) bodyCtx.ClassConstructors[k] = v;
            foreach (var (k, v) in _classCtorArgCounts) bodyCtx.ClassCtorArgCounts[k] = v;
            foreach (var (k, v) in _classMethods) bodyCtx.AllClassMethods[k] = v;
            foreach (var (k, v) in _classMethodParamTypes) bodyCtx.AllClassMethodParamTypes[k] = v;
            foreach (var mn in _classMethodNames) bodyCtx.ClassMethods.Add(mn);
            foreach (var (k, v) in importMap) bodyCtx.ImportMap[k] = v;

            // LEGB: remove locally-assigned non-global names
            foreach (var name in fnAssigned)
                if (!fnGlobals.Contains(name))
                    bodyCtx.Fields.Remove(name);

            // Re-apply hoisted fields so nested closures inside the body can see them
            foreach (var r in nestedRefs.Intersect(assigned.Union(paramNamesSet)))
            {
                if (ctx.Fields.TryGetValue(r, out var hf))
                    bodyCtx.Fields[r] = hf;
            }

            // Unpack original params from __args[i] into locals; also init hoisted fields
            for (int bi = 0; bi < fn.Params.Count; bi++)
            {
                var pname = fn.Params[bi].Name;
                var plocal = bodyCtx.Locals.Declare(pname, typeof(object));
                bodyIL.Emit(OpCodes.Ldarg_1);
                bodyIL.Emit(OpCodes.Ldc_I4, bi);
                bodyIL.Emit(OpCodes.Ldelem_Ref);
                bodyIL.Emit(OpCodes.Stloc, plocal);
                if (bodyCtx.Fields.TryGetValue(pname, out var hField))
                {
                    bodyIL.Emit(OpCodes.Ldloc, plocal);
                    bodyIL.Emit(OpCodes.Stsfld, hField);
                }
            }

            var bodyEmitter = new StatementEmitter(bodyCtx);
            bodyEmitter.EmitAll(fn.Body);

            if (bodyCtx.MethodReturnLabel.HasValue)
                bodyIL.MarkLabel(bodyCtx.MethodReturnLabel.Value);
            bodyIL.Emit(OpCodes.Ret);

            return;  // wrapper already emitted `ret`
        }

        var emitter = new StatementEmitter(ctx);
        emitter.EmitAll(fn.Body);

        // Capture any methods/types registered by inner defs (e.g. nested functions,
        // nested classes) so that nested class bodies compiled in Pass 3 can resolve them.
        foreach (var (k, v) in ctx.Methods) _innerFunctionMethods.TryAdd(k, v);
        foreach (var (k, v) in ctx.ClassTypes) _innerFunctionClassTypes.TryAdd(k, v);
        foreach (var (k, v) in ctx.ClassConstructors) _innerFunctionClassCtors.TryAdd(k, v);
        foreach (var (k, v) in ctx.Fields) _innerFunctionFields.TryAdd(k, v);

        EmitMethodEpilog(il, ctx, returnType);
    }

    /// <summary>
    /// Emits the function epilog after all statement emission is complete.
    /// If any <c>return</c> inside an exception block used <c>leave</c> to the
    /// <see cref="EmitContext.MethodReturnLabel"/>, we mark that label here and
    /// emit the final <c>ret</c>.  Otherwise we just emit a fall-through <c>ret</c>.
    /// </summary>
    private static void EmitMethodEpilog(ILGenerator il, EmitContext ctx, Type returnType)
    {
        if (ctx.MethodReturnLabel.HasValue)
        {
            // At least one `return` inside an exception block used `leave` to jump here.
            // The fall-through path (function body exits without an explicit return) also
            // needs to reach the epilog. Since we're outside exception blocks at this point
            // a plain `br` is valid.
            if (ctx.ReturnValueLocal != null)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Stloc, ctx.ReturnValueLocal);
            }
            il.Emit(OpCodes.Br, ctx.MethodReturnLabel.Value);

            il.MarkLabel(ctx.MethodReturnLabel.Value);
            if (ctx.ReturnValueLocal != null)
                il.Emit(OpCodes.Ldloc, ctx.ReturnValueLocal);
            else if (returnType != typeof(void))
                il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // No leave-based returns. Emit a plain fallthrough ret.
            if (returnType == typeof(object))
                il.Emit(OpCodes.Ldnull);
            else if (returnType != typeof(void))
            {
                il.Emit(OpCodes.Ldc_I4_0);
                if (returnType == typeof(long)) il.Emit(OpCodes.Conv_I8);
                if (returnType == typeof(double)) il.Emit(OpCodes.Conv_R8);
            }
            il.Emit(OpCodes.Ret);
        }
    }
}

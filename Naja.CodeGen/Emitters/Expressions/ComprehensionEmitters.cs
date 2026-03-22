using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Specialist emitter for comprehension expressions (list, set, dict) and generators.
/// Handles Python 3's comprehension scoping by creating isolated helper methods
/// that receive outer iterables as parameters, preventing loop variable leakage.
/// </summary>
public sealed class ComprehensionEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public ComprehensionEmitters(EmitContext ctx, ExpressionEmitter mainEmitter)
        : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    public override NajaType Emit(Expression expr) =>
        throw new CodeGenException(
            "ComprehensionEmitters does not support direct Emit(). " +
            "Use EmitListComp, EmitSetComp, EmitDictComp, or EmitGenerator.",
            expr.Line, expr.Column);

    // ── List/Set/Dict/Generator Comprehensions ────────────────────────────

    public NajaType EmitListComp(ListCompExpr e)
    {
        return EmitComprehensionHelper(e.Line, e.Column, e.Generators, e.Element, isSet: false, isDict: false, null, null);
    }

    public NajaType EmitSetComp(SetCompExpr e)
    {
        return EmitComprehensionHelper(e.Line, e.Column, e.Generators, e.Element, isSet: true, isDict: false, null, null);
    }

    public NajaType EmitDictComp(DictCompExpr e)
    {
        return EmitComprehensionHelper(e.Line, e.Column, e.Generators, null, isSet: false, isDict: true, e.Key, e.Value);
    }

    public NajaType EmitGenerator(GeneratorExpr e)
    {
        // Lazy generator expression: create a helper method with generator semantics
        // (yields one element at a time) instead of collecting everything eagerly.
        // The helper has signature: void <gencomp>_L_C__gen_body__(NajaGenerator, object[])
        // and the wrapper builds an object[] from the outer iterable and returns NajaGenerator.

        // Evaluate the outermost iterable in the CALLING context (Python scoping rule)
        var outerIterType = _mainEmitter.Emit(e.Generators[0].Iter);
        TypeMapper.EmitBox(IL, outerIterType);
        var outerIterLocal = _ctx.Locals.Declare($"__gc_iter_{e.Line}_{e.Column}", typeof(object));
        IL.Emit(OpCodes.Stloc, outerIterLocal);

        // Define the body method (NajaGenerator coroutine)
        var bodyName = $"<gencomp>_{e.Line}_{e.Column}__gen_body__";
        var bodyMethod = _ctx.TypeBuilder.DefineMethod(
            bodyName,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(void),
            new System.Type[] { typeof(NajaGenerator), typeof(object[]) });
        bodyMethod.DefineParameter(1, ParameterAttributes.None, "__gen");
        bodyMethod.DefineParameter(2, ParameterAttributes.None, "__args");

        // Emit wrapper inline: build args[], create delegate, new NajaGenerator
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Newarr, typeof(object));
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Ldloc, outerIterLocal);
        IL.Emit(OpCodes.Stelem_Ref);
        var argsLocal = _ctx.Locals.Declare($"__gc_args_{e.Line}_{e.Column}", typeof(object[]));
        IL.Emit(OpCodes.Stloc, argsLocal);
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldftn, bodyMethod);
        IL.Emit(OpCodes.Newobj, typeof(Action<NajaGenerator, object[]>).GetConstructors()[0]);
        IL.Emit(OpCodes.Ldloc, argsLocal);
        IL.Emit(OpCodes.Newobj, NajaBuiltinsMethodCache.NajaGenerator_Ctor);

        // Build body context
        var bodyIL = bodyMethod.GetILGenerator();
        var bodyCtx = new EmitContext(bodyIL, _ctx.Model, _ctx.TypeBuilder, _ctx.Module,
                                      typeof(void), new[] { "__gen", "__args" });
        bodyCtx.IsInsideFunction = true;
        bodyCtx.IsGeneratorBody = true;
        bodyCtx.IsInstanceMethod = false;
        foreach (var (k, v) in _ctx.Fields) bodyCtx.Fields[k] = v;
        foreach (var (k, v) in _ctx.Methods) bodyCtx.Methods[k] = v;
        foreach (var (k, v) in _ctx.MethodParamTypes) bodyCtx.MethodParamTypes[k] = v;
        foreach (var (k, v) in _ctx.ClassTypes) bodyCtx.ClassTypes[k] = v;
        foreach (var (k, v) in _ctx.ClassConstructors) bodyCtx.ClassConstructors[k] = v;
        foreach (var (k, v) in _ctx.AllClassMethods) bodyCtx.AllClassMethods[k] = v;
        foreach (var (k, v) in _ctx.AllClassMethodParamTypes) bodyCtx.AllClassMethodParamTypes[k] = v;
        foreach (var mn in _ctx.ClassMethods) bodyCtx.ClassMethods.Add(mn);
        foreach (var (k, v) in _ctx.ImportMap) bodyCtx.ImportMap[k] = v;
        foreach (var (k, v) in _ctx.NamespaceImports) bodyCtx.NamespaceImports[k] = v;
        bodyCtx.SelfName = _ctx.SelfName;

        // Unpack outer iterable from __args[0]
        var iterParam = bodyCtx.Locals.Declare("__iter0", typeof(object));
        bodyIL.Emit(OpCodes.Ldarg_1);
        bodyIL.Emit(OpCodes.Ldc_I4_0);
        bodyIL.Emit(OpCodes.Ldelem_Ref);
        bodyIL.Emit(OpCodes.Stloc, iterParam);

        // Emit the comprehension loop using the body context's ExpressionEmitter.
        // We delegate to EmitComprehensionHelper but give it the body context
        // and treat it as a generator (each element calls gen.Yield instead of list.Add).
        var scopeId = $"comp_{e.Line}_{e.Column}";
        bodyCtx.ComprehensionScopeId = scopeId;
        var bodyExpr = new ExpressionEmitter(bodyCtx);
        EmitGeneratorBodyLoop(bodyIL, bodyCtx, bodyExpr, e.Generators, e.Element, iterParam, scopeId, 0);

        bodyIL.Emit(OpCodes.Ret);

        return NajaTypes.Unknown;  // NajaGenerator on stack
    }

    /// <summary>
    /// Recursively emits the nested for/if loops of a generator expression body,
    /// yielding each matching element via NajaGenerator.Yield.
    /// </summary>
    private void EmitGeneratorBodyLoop(
        ILGenerator bodyIL,
        EmitContext bodyCtx,
        ExpressionEmitter bodyExpr,
        IReadOnlyList<Comprehension> generators,
        Expression element,
        LocalBuilder outerEnumOrIter,
        string scopeId,
        int depth)
    {
        var gen = generators[depth];

        // Get enumerator from the iterable (depth 0 uses the pre-loaded param)
        LocalBuilder enumLocal;
        if (depth == 0)
        {
            bodyIL.Emit(OpCodes.Ldloc, outerEnumOrIter);
            bodyIL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetForLoopEnumerator_Method);
            enumLocal = bodyCtx.Locals.Declare($"__gc_e{depth}_{scopeId}", typeof(System.Collections.IEnumerator));
            bodyIL.Emit(OpCodes.Stloc, enumLocal);
        }
        else
        {
            var innerIterType = bodyExpr.Emit(gen.Iter);
            TypeMapper.EmitBox(bodyIL, innerIterType);
            bodyIL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetForLoopEnumerator_Method);
            enumLocal = bodyCtx.Locals.Declare($"__gc_e{depth}_{scopeId}", typeof(System.Collections.IEnumerator));
            bodyIL.Emit(OpCodes.Stloc, enumLocal);
        }

        var loopStart = bodyIL.DefineLabel();
        var loopEnd   = bodyIL.DefineLabel();
        var moveNext  = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var getCurrent = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        bodyIL.MarkLabel(loopStart);
        bodyIL.Emit(OpCodes.Ldloc, enumLocal);
        bodyIL.Emit(OpCodes.Callvirt, moveNext);
        bodyIL.Emit(OpCodes.Brfalse, loopEnd);

        // Assign loop variable
        var loopVarName = gen.Target is NameExpr nv ? nv.Name : $"__gcv{depth}";
        var loopVarLocal = bodyCtx.Locals.Declare($"{loopVarName}_{scopeId}", typeof(object));
        bodyIL.Emit(OpCodes.Ldloc, enumLocal);
        bodyIL.Emit(OpCodes.Callvirt, getCurrent);
        bodyIL.Emit(OpCodes.Stloc, loopVarLocal);
        // Make accessible by name in body context
        var hoistedName = $"__hoisted_{loopVarName}_{scopeId}";
        if (!bodyCtx.Fields.ContainsKey(hoistedName))
        {
            var hf = bodyCtx.TypeBuilder.DefineField(hoistedName, typeof(object), FieldAttributes.Private | FieldAttributes.Static);
            bodyCtx.Fields[hoistedName] = hf;
        }
        bodyIL.Emit(OpCodes.Ldloc, loopVarLocal);
        bodyIL.Emit(OpCodes.Stsfld, bodyCtx.Fields[hoistedName]);

        // Apply filters if present
        foreach (var cond in gen.Conditions)
        {
            var condType = bodyExpr.Emit(cond);
            TypeMapper.EmitBox(bodyIL, condType);
            bodyIL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToBool_Method);
            bodyIL.Emit(OpCodes.Brfalse, loopStart);
        }

        if (depth + 1 < generators.Count)
        {
            // Recurse into next generator
            EmitGeneratorBodyLoop(bodyIL, bodyCtx, bodyExpr, generators, element, outerEnumOrIter, scopeId, depth + 1);
        }
        else
        {
            // Innermost loop: yield the element
            bodyIL.Emit(OpCodes.Ldarg_0);   // NajaGenerator __gen
            var elemType = bodyExpr.Emit(element);
            TypeMapper.EmitBox(bodyIL, elemType);
            bodyIL.Emit(OpCodes.Callvirt, NajaBuiltinsMethodCache.NajaGenerator_Yield_Method);
            bodyIL.Emit(OpCodes.Pop);        // discard send value
        }

        bodyIL.Emit(OpCodes.Br, loopStart);
        bodyIL.MarkLabel(loopEnd);
    }

    /// <summary>
    /// Emits a comprehension (list/set/dict/generator) by creating a private static helper
    /// method on the same type.  The helper method receives the outer iterable(s) as
    /// parameters so that comprehension-scoped loop variables never leak into the
    /// calling method's locals — exactly matching Python 3's comprehension scoping rules.
    /// </summary>
    private NajaType EmitComprehensionHelper(
        int line, int col,
        IReadOnlyList<Comprehension> generators,
        Expression? element,
        bool isSet, bool isDict,
        Expression? dictKey, Expression? dictValue)
    {
        // Determine return type
        Type returnClrType = isSet
            ? typeof(System.Collections.Generic.HashSet<object>)
            : isDict
                ? typeof(System.Collections.Generic.Dictionary<object, object>)
                : typeof(System.Collections.Generic.List<object>);

        // The outermost generator's iterable is evaluated in the CALLER's scope.
        // We pass it as a parameter to the helper method.
        // All other iterables and the element expression are evaluated inside the helper.
        // We also pass captured outer variables as additional object parameters.

        // --- Emit outer iterable in the CALLING context ---
        var outerIterType = _mainEmitter.Emit(generators[0].Iter);
        TypeMapper.EmitBox(IL, outerIterType);

        // --- Define helper method ---
        var helperName = $"<comp>_{line}_{col}";
        var helperPts = new[] { typeof(object) };  // single param: the outer iterable
        var helperMb = _ctx.TypeBuilder.DefineMethod(
            helperName,
            MethodAttributes.Private | MethodAttributes.Static,
            returnClrType,
            helperPts);
        helperMb.DefineParameter(1, ParameterAttributes.None, "__iter0");

        // --- Build EmitContext for helper (NO outer locals — scope isolation) ---
        var hIL = helperMb.GetILGenerator();
        var hCtx = new EmitContext(hIL, _ctx.Model, _ctx.TypeBuilder, _ctx.Module,
                                   returnClrType, new[] { "__iter0" });
        hCtx.IsInsideFunction = true;
        // Copy module-level fields, methods, class info — but NOT locals
        foreach (var (k, v) in _ctx.Fields) hCtx.Fields[k] = v;
        // Set the comprehension scope ID for hoisted loop variables
        var scopeId = $"comp_{line}_{col}";
        hCtx.ComprehensionScopeId = scopeId;

        foreach (var (k, v) in _ctx.Methods) hCtx.Methods[k] = v;
        foreach (var (k, v) in _ctx.MethodParamTypes) hCtx.MethodParamTypes[k] = v;
        foreach (var (k, v) in _ctx.ClassTypes) hCtx.ClassTypes[k] = v;
        foreach (var (k, v) in _ctx.ClassConstructors) hCtx.ClassConstructors[k] = v;
        foreach (var (k, v) in _ctx.InstanceFields) hCtx.InstanceFields[k] = v;
        foreach (var (k, v) in _ctx.AllClassMethods) hCtx.AllClassMethods[k] = v;
        foreach (var (k, v) in _ctx.AllClassMethodParamTypes) hCtx.AllClassMethodParamTypes[k] = v;
        foreach (var mn in _ctx.ClassMethods) hCtx.ClassMethods.Add(mn);
        foreach (var (k, v) in _ctx.ImportMap) hCtx.ImportMap[k] = v;
        foreach (var (k, v) in _ctx.NamespaceImports) hCtx.NamespaceImports[k] = v;
        hCtx.SelfName = _ctx.SelfName;
        hCtx.IsInstanceMethod = false;  // helper is static, no 'self'

        // --- Build the collection inside the helper ---
        var hExpr = new ExpressionEmitter(hCtx);
        var resultLocal = hCtx.Locals.Declare("__result", returnClrType);

        if (isSet)
        {
            hIL.Emit(OpCodes.Newobj, returnClrType.GetConstructor(Type.EmptyTypes)!);
        }
        else if (isDict)
        {
            hIL.Emit(OpCodes.Newobj, returnClrType.GetConstructor(Type.EmptyTypes)!);
        }
        else
        {
            hIL.Emit(OpCodes.Newobj, returnClrType.GetConstructor(Type.EmptyTypes)!);
        }
        hIL.Emit(OpCodes.Stloc, resultLocal);

        // Rewrite the first generator to use the __iter0 parameter
        var rewrittenGenerators = new List<Comprehension>(generators);

        hExpr.EmitComprehensionLoopsHelper(
            rewrittenGenerators, 0, isFirstFromParam: true,
            body: () =>
            {
                hIL.Emit(OpCodes.Ldloc, resultLocal);
                if (isDict)
                {
                    var kt = hExpr.Emit(dictKey!); TypeMapper.EmitBox(hIL, kt);
                    var vt = hExpr.Emit(dictValue!); TypeMapper.EmitBox(hIL, vt);
                    hIL.Emit(OpCodes.Callvirt, returnClrType.GetMethod("set_Item")!);
                }
                else if (isSet)
                {
                    var t = hExpr.Emit(element!);
                    TypeMapper.EmitBox(hIL, t);
                    hIL.Emit(OpCodes.Callvirt, returnClrType.GetMethod("Add")!);
                    hIL.Emit(OpCodes.Pop); // HashSet.Add returns bool
                }
                else
                {
                    var t = hExpr.Emit(element!);
                    TypeMapper.EmitBox(hIL, t);
                    hIL.Emit(OpCodes.Callvirt, returnClrType.GetMethod("Add")!);
                }
            });

        hIL.Emit(OpCodes.Ldloc, resultLocal);
        hIL.Emit(OpCodes.Ret);

        // Propagate fields newly defined inside the helper (e.g. walrus := targets) back
        // to the calling context so the enclosing scope can read them (PEP 572 semantics).
        // Only plain Python-identifier names are propagated; __hoisted_* loop-var fields
        // and other internal __ names must stay scoped to the helper to avoid leaking.
        foreach (var (k, v) in hCtx.Fields)
            if (!k.StartsWith("__") && !_ctx.Fields.ContainsKey(k))
                _ctx.Fields[k] = v;

        // --- Call helper from the original context (outer iterable already on stack) ---
        IL.Emit(OpCodes.Call, helperMb);

        return isSet ? (NajaType)new SetType(NajaTypes.Unknown)
             : isDict ? new DictType(NajaTypes.Unknown, NajaTypes.Unknown)
             : new ListType(NajaTypes.Unknown);
    }

    /// <summary>
    /// Variant of EmitComprehensionLoops used inside isolated helper methods.
    /// When <paramref name="isFirstFromParam"/> is true, the first generator reads
    /// from ldarg.0 (the passed-in iterable) instead of evaluating its Iter expression.
    /// </summary>
    internal void EmitComprehensionLoopsHelper(
        IReadOnlyList<Comprehension> generators,
        int depth,
        bool isFirstFromParam,
        Action body)
    {
        if (depth >= generators.Count)
        {
            body();
            return;
        }

        var gen = generators[depth];
        var loopStart = IL.DefineLabel();
        var loopEnd = IL.DefineLabel();

        if (depth == 0 && isFirstFromParam)
        {
            // Load the iterable from the method parameter (ldarg.0)
            IL.Emit(OpCodes.Ldarg_0);
        }
        else
        {
            var iterType = _mainEmitter.Emit(gen.Iter);
            TypeMapper.EmitBox(IL, iterType);
        }

        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetForLoopEnumerator_Method);

        var enumLocal = _ctx.Locals.Declare($"__cenum_{depth}_{gen.Iter.Line}_{gen.Iter.Column}",
            typeof(System.Collections.IEnumerator));
        IL.Emit(OpCodes.Stloc, enumLocal);

        IL.MarkLabel(loopStart);
        IL.Emit(OpCodes.Ldloc, enumLocal);
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, loopEnd);

        IL.Emit(OpCodes.Ldloc, enumLocal);
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;
        IL.Emit(OpCodes.Callvirt, current);

        StoreComprehensionTarget(gen.Target);

        foreach (var cond in gen.Conditions)
        {
            var condType = _mainEmitter.Emit(cond);
            if (condType is not (IntType or FloatType or BoolType))
                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToBool_Method);
            var condTrueLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue_S, condTrueLabel);
            IL.Emit(OpCodes.Br, loopStart);
            IL.MarkLabel(condTrueLabel);
        }

        EmitComprehensionLoopsHelper(generators, depth + 1, false, body);

        IL.Emit(OpCodes.Br, loopStart);
        IL.MarkLabel(loopEnd);
    }

    /// <summary>
    /// Recursively emit nested for-loops for each comprehension generator.
    /// Each generator may have filter conditions (if clauses).
    /// </summary>
    private void EmitComprehensionLoops(
        IReadOnlyList<Comprehension> generators,
        int depth,
        Action emitBody)
    {
        if (depth >= generators.Count)
        {
            emitBody();
            return;
        }

        var gen = generators[depth];
        var loopStart = IL.DefineLabel();
        var loopEnd = IL.DefineLabel();

        // Get enumerator
        var iterType = _mainEmitter.Emit(gen.Iter);
        TypeMapper.EmitBox(IL, iterType);
        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetForLoopEnumerator_Method);

        var enumLocal = _ctx.Locals.Declare($"__cenum_{depth}_{generators[depth].Iter.Line}",
            typeof(System.Collections.IEnumerator));
        IL.Emit(OpCodes.Stloc, enumLocal);

        IL.MarkLabel(loopStart);
        IL.Emit(OpCodes.Ldloc, enumLocal);
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, loopEnd);

        // Assign loop variable
        IL.Emit(OpCodes.Ldloc, enumLocal);
        var current = typeof(System.Collections.IEnumerator)
            .GetProperty("Current")!.GetGetMethod()!;
        IL.Emit(OpCodes.Callvirt, current);

        // Store — target might be Name or Tuple
        StoreComprehensionTarget(gen.Target);

        // Emit filter conditions (if clauses)
        foreach (var cond in gen.Conditions)
        {
            var condType = _mainEmitter.Emit(cond);
            if (condType is not (IntType or FloatType or BoolType))
                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToBool_Method);
            var condTrueLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue_S, condTrueLabel);
            IL.Emit(OpCodes.Br, loopStart);
            IL.MarkLabel(condTrueLabel);
        }

        // Recurse for next generator or emit body
        EmitComprehensionLoops(generators, depth + 1, emitBody);

        IL.Emit(OpCodes.Br, loopStart);
        IL.MarkLabel(loopEnd);
    }

    private void StoreComprehensionTarget(Expression target)
    {
        if (target is NameExpr n)
        {
            // Hoist comprehension loop targets to static fields for late binding semantics.
            // This allows lambdas created during comprehension iteration to all reference
            // the same shared variable location, matching Python's late-binding behavior.
            string fieldKey;
            if (!string.IsNullOrEmpty(_ctx.ComprehensionScopeId))
            {
                fieldKey = $"__hoisted_{n.Name}_{_ctx.ComprehensionScopeId}";
            }
            else
            {
                // Fallback: use just the variable name (for regular comprehensions)
                fieldKey = $"__hoisted_{n.Name}";
            }

            if (!_ctx.Fields.TryGetValue(fieldKey, out var field))
            {
                var fb = _ctx.TypeBuilder.DefineField(fieldKey, typeof(object),
                    FieldAttributes.Private | FieldAttributes.Static);
                _ctx.Fields[fieldKey] = fb;
                field = fb;
            }
            _ctx.IL.Emit(OpCodes.Stsfld, field);
        }
        else if (target is TupleExpr t)
        {
            // Unpack tuple — value is object on stack
            var tmp = _ctx.Locals.Declare($"__ctmp_{target.Line}", typeof(object));
            IL.Emit(OpCodes.Stloc, tmp);
            for (int i = 0; i < t.Elements.Count; i++)
            {
                var unpackHelper = NajaBuiltinsMethodCache.GetItem_Method;
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Box, typeof(int));
                IL.Emit(OpCodes.Call, unpackHelper);
                StoreComprehensionTarget(t.Elements[i]);
            }
        }
        else
        {
            IL.Emit(OpCodes.Pop);
        }
    }
}

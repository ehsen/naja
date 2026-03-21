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
        // Compile generators eagerly as List<object> for now
        return EmitComprehensionHelper(e.Line, e.Column, e.Generators, e.Element, isSet: false, isDict: false, null, null);
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

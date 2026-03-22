using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Specialist emitter for yield expressions and generator delegation.
/// Handles `yield` and `yield from` expressions which add values to the 
/// generator list and implement Python generator protocol basics.
/// </summary>
public sealed class GeneratorEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public GeneratorEmitters(EmitContext ctx, ExpressionEmitter mainEmitter)
        : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    public override NajaType Emit(Expression expr) =>
        throw new CodeGenException(
            "GeneratorEmitters does not support direct Emit(). " +
            "Use EmitYield directly from ExpressionEmitter dispatcher.",
            expr.Line, expr.Column);

    // ── Yield  (coroutine-based generator support) ────────────────────────────

    public NajaType EmitYield(YieldExpr e)
    {
        if (!_ctx.IsInsideFunction)
            throw new CodeGenException("'yield' outside function", e.Line, e.Column);

        if (e.IsFrom)
            return EmitYieldFrom(e);

        if (_ctx.IsGeneratorBody)
        {
            // Thread-based coroutine: ldarg.0 = NajaGenerator __gen; call gen.Yield(value)
            // Returns the sent value (from generator.send(v)), which becomes the result
            // of the yield expression in the body.
            IL.Emit(OpCodes.Ldarg_0);   // load NajaGenerator __gen
            var valueType = _mainEmitter.Emit(e.Value);
            TypeMapper.EmitBox(IL, valueType);
            IL.Emit(OpCodes.Callvirt, NajaBuiltinsMethodCache.NajaGenerator_Yield_Method);
            return NajaTypes.Unknown;   // send value (or None)
        }

        // Fallback for contexts where IsGeneratorBody wasn't set (should not happen in normal use)
        var vt = _mainEmitter.Emit(e.Value);
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldnull);
        return NajaTypes.None;
    }

    // ── Yield from  (generator delegation) ────────────────────────────────────

    private NajaType EmitYieldFrom(YieldExpr e)
    {
        if (!_ctx.IsGeneratorBody)
            throw new CodeGenException("'yield from' used in non-generator function", e.Line, e.Column);

        // Iterate the sub-iterable and forward every value to our caller via gen.Yield().
        // If the sub-iterable is a NajaGenerator, capture its ReturnValue after exhaustion
        // and return it as the result of the yield-from expression (PEP 380).

        // Evaluate the sub-iterable
        var iterType = _mainEmitter.Emit(e.Value);
        TypeMapper.EmitBox(IL, iterType);
        // Keep a reference so we can read ReturnValue after the loop
        var subIterLocal = _ctx.Locals.Declare($"__yf_sub_{e.Line}_{e.Column}", typeof(object));
        IL.Emit(OpCodes.Stloc, subIterLocal);

        // Get enumerator
        IL.Emit(OpCodes.Ldloc, subIterLocal);
        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetForLoopEnumerator_Method);
        var enumLocal = _ctx.Locals.Declare($"__yf_enum_{e.Line}_{e.Column}", typeof(System.Collections.IEnumerator));
        IL.Emit(OpCodes.Stloc, enumLocal);

        var loopStart = IL.DefineLabel();
        var loopEnd   = IL.DefineLabel();
        var moveNext  = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var getCurrent = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        IL.MarkLabel(loopStart);
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, loopEnd);

        // Forward current element: __gen.Yield(current) — discard send value
        IL.Emit(OpCodes.Ldarg_0);   // NajaGenerator __gen
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, getCurrent);
        IL.Emit(OpCodes.Callvirt, NajaBuiltinsMethodCache.NajaGenerator_Yield_Method);
        IL.Emit(OpCodes.Pop);       // discard send value for now

        IL.Emit(OpCodes.Br, loopStart);
        IL.MarkLabel(loopEnd);

        // Return sub-generator's ReturnValue (PEP 380), or null for plain iterables
        var retValProp = typeof(NajaGenerator).GetProperty(nameof(NajaGenerator.ReturnValue))!.GetGetMethod()!;
        var afterCheck = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, subIterLocal);
        IL.Emit(OpCodes.Isinst, typeof(NajaGenerator));
        IL.Emit(OpCodes.Brfalse, afterCheck);
        IL.Emit(OpCodes.Ldloc, subIterLocal);
        IL.Emit(OpCodes.Castclass, typeof(NajaGenerator));
        IL.Emit(OpCodes.Callvirt, retValProp);
        var retLocal = _ctx.Locals.Declare($"__yf_ret_{e.Line}_{e.Column}", typeof(object));
        IL.Emit(OpCodes.Stloc, retLocal);
        var doneLabel = IL.DefineLabel();
        IL.Emit(OpCodes.Br, doneLabel);
        IL.MarkLabel(afterCheck);
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Stloc, retLocal);
        IL.MarkLabel(doneLabel);
        IL.Emit(OpCodes.Ldloc, retLocal);
        return NajaTypes.Unknown;
    }
}

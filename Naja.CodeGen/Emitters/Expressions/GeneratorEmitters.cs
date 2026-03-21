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

    // ── Yield  (Basic generator support) ──────────────────────────────────────

    public NajaType EmitYield(YieldExpr e)
    {
        if (!_ctx.IsInsideFunction)
            throw new CodeGenException("'yield' outside function", e.Line, e.Column);

        if (e.IsFrom)
            return EmitYieldFrom(e);

        // Add yielded value to the generator list that was initialized at function entry
        if (_ctx.GeneratorListLocal != null)
        {
            IL.Emit(OpCodes.Ldloc, _ctx.GeneratorListLocal);
            var valueType = _mainEmitter.Emit(e.Value);
            TypeMapper.EmitBox(IL, valueType);
            var addMethod = typeof(System.Collections.Generic.List<object>).GetMethod("Add")!;
            IL.Emit(OpCodes.Callvirt, addMethod);
        }
        else
        {
            // Fallback: generator list not initialized, just discard the value
            var valueType = _mainEmitter.Emit(e.Value);
            IL.Emit(OpCodes.Pop);
        }

        // Yield expressions leave None on the stack (the send() value — not yet supported)
        IL.Emit(OpCodes.Ldnull);
        return NajaTypes.None;
    }

    // ── Yield from  (generator delegation) ────────────────────────────────────

    private NajaType EmitYieldFrom(YieldExpr e)
    {
        if (_ctx.GeneratorListLocal is null)
            throw new CodeGenException("'yield from' used in non-generator function", e.Line, e.Column);

        // Iterate the sub-iterable and add every value to the generator list.
        // Use GetForLoopEnumerator so strings yield single-char strings, not chars.
        var iterType = _mainEmitter.Emit(e.Value);
        TypeMapper.EmitBox(IL, iterType);
        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetForLoopEnumerator_Method);

        var enumLocal = _ctx.Locals.Declare($"__yf_enum_{e.Line}_{e.Column}", typeof(System.Collections.IEnumerator));
        IL.Emit(OpCodes.Stloc, enumLocal);

        var loopStart = IL.DefineLabel();
        var loopEnd = IL.DefineLabel();
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;
        var addMethod = typeof(System.Collections.Generic.List<object>).GetMethod("Add")!;

        IL.MarkLabel(loopStart);
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, loopEnd);

        IL.Emit(OpCodes.Ldloc, _ctx.GeneratorListLocal);
        IL.Emit(OpCodes.Ldloc, enumLocal);
        IL.Emit(OpCodes.Callvirt, current);
        IL.Emit(OpCodes.Callvirt, addMethod);

        IL.Emit(OpCodes.Br, loopStart);
        IL.MarkLabel(loopEnd);

        IL.Emit(OpCodes.Ldnull);
        return NajaTypes.None;
    }
}

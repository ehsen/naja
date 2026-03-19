using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Emits IL for literal expressions: integers, floats, strings, bools, None, Ellipsis.
/// </summary>
public sealed class LiteralEmitters : ExpressionEmitterBase
{
    public LiteralEmitters(EmitContext ctx) : base(ctx) { }

    /// <summary>
    /// Main dispatch for literal expressions.
    /// </summary>
    public NajaType EmitLiteral(Expression expr)
    {
        return expr switch
        {
            IntLiteral e => EmitInt(e),
            FloatLiteral e => EmitFloat(e),
            StringLiteral e => EmitString(e),
            BoolLiteral e => EmitBool(e),
            NoneLiteral e => EmitNone(e),
            EllipsisLiteral e => EmitEllipsis(e),
            _ => throw new CodeGenException($"Not a literal: {expr.GetType().Name}", expr.Line, expr.Column)
        };
    }

    private NajaType EmitInt(IntLiteral e)
    {
        if (e.Value >= int.MinValue && e.Value <= int.MaxValue)
        {
            // Emit as int32 using efficient opcodes, then widen to int64
            switch (e.Value)
            {
                case -1: IL.Emit(OpCodes.Ldc_I4_M1); break;
                case 0: IL.Emit(OpCodes.Ldc_I4_0); break;
                case 1: IL.Emit(OpCodes.Ldc_I4_1); break;
                case 2: IL.Emit(OpCodes.Ldc_I4_2); break;
                case 3: IL.Emit(OpCodes.Ldc_I4_3); break;
                case 4: IL.Emit(OpCodes.Ldc_I4_4); break;
                case 5: IL.Emit(OpCodes.Ldc_I4_5); break;
                case 6: IL.Emit(OpCodes.Ldc_I4_6); break;
                case 7: IL.Emit(OpCodes.Ldc_I4_7); break;
                case 8: IL.Emit(OpCodes.Ldc_I4_8); break;
                default: IL.Emit(OpCodes.Ldc_I4, (int)e.Value); break;
            }
            IL.Emit(OpCodes.Conv_I8);  // Python int is always int64
        }
        else
        {
            IL.Emit(OpCodes.Ldc_I8, e.Value);
        }

        return NajaTypes.Int;
    }

    private NajaType EmitFloat(FloatLiteral e)
    {
        IL.Emit(OpCodes.Ldc_R8, e.Value);
        return NajaTypes.Float;
    }

    private NajaType EmitString(StringLiteral e)
    {
        IL.Emit(OpCodes.Ldstr, e.Value);
        return NajaTypes.Str;
    }

    private NajaType EmitBool(BoolLiteral e)
    {
        IL.Emit(e.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        return NajaTypes.Bool;
    }

    private NajaType EmitNone(NoneLiteral e)
    {
        IL.Emit(OpCodes.Ldnull);
        return NajaTypes.None;
    }

    private NajaType EmitEllipsis(EllipsisLiteral e)
    {
        IL.Emit(OpCodes.Ldnull);   // treat ... as None for now
        return NajaTypes.None;
    }

    /// <summary>
    /// Not implemented in this emitter - used by ExpressionEmitter for dispatch
    /// </summary>
    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException("Use EmitLiteral() for literal expressions");
    }
}

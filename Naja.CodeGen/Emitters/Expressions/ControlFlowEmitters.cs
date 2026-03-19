using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Specialist emitter for control flow expressions: ternary conditionals and walrus operator.
/// Handles conditional branches and assignment-with-return semantics.
/// </summary>
public sealed class ControlFlowEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public ControlFlowEmitters(EmitContext ctx, ExpressionEmitter mainEmitter)
        : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    /// <summary>
    /// Emits IL for ternary conditional expressions: a if condition else b
    /// Evaluates condition, branches to then or else branch, and widens return type.
    /// </summary>
    public NajaType EmitIfExpr(IfExpr e)
    {
        var elseLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        _mainEmitter.Emit(e.Condition);
        IL.Emit(OpCodes.Brfalse, elseLabel);
        var thenType = _mainEmitter.Emit(e.Then);
        IL.Emit(OpCodes.Br, endLabel);
        IL.MarkLabel(elseLabel);
        var elseType = _mainEmitter.Emit(e.Else);
        IL.MarkLabel(endLabel);

        return NajaTypes.Widen(thenType, elseType);
    }

    /// <summary>
    /// Emits IL for walrus operator expressions: (target := value)
    /// Evaluates value, stores in local, leaves value on stack.
    /// Used for assignment-as-expression semantics in comprehensions and conditions.
    /// </summary>
    public NajaType EmitWalrus(WalrusExpr e)
    {
        var type = _mainEmitter.Emit(e.Value);
        // Declare local and store a copy, then leave value on stack
        var clrType = TypeMapper.ToClrType(type);
        if (clrType == typeof(void)) clrType = typeof(object);
        if (!_ctx.Locals.Contains(e.Target))
            _ctx.Locals.Declare(e.Target, clrType);
        IL.Emit(OpCodes.Dup);
        _ctx.Locals.EmitStore(e.Target);
        return type;
    }

    /// <summary>
    /// Abstract override (required by base class but not used directly).
    /// Routes to specific emitter based on expression type.
    /// </summary>
    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException(
            $"Use EmitIfExpr() or EmitWalrus() directly. " +
            $"Expression type: {expr.GetType().Name}");
    }
}

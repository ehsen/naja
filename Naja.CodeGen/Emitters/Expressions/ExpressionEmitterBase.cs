using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Base class for all expression emitters.
/// Provides shared access to context, IL generator, and common utilities.
/// Every Emit method leaves exactly ONE value on the evaluation stack.
/// </summary>
public abstract class ExpressionEmitterBase
{
    protected readonly EmitContext _ctx;
    protected ILGenerator IL => _ctx.IL;

    protected ExpressionEmitterBase(EmitContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>
    /// Emits IL for an expression and returns its NajaType.
    /// Implementations must leave exactly one value on the evaluation stack.
    /// </summary>
    public abstract NajaType Emit(Expression expr);

    /// <summary>
    /// Emits a LabelTarget at the current position for control flow.
    /// </summary>
    protected void EmitLabel(System.Reflection.Emit.Label label)
    {
        IL.MarkLabel(label);
    }

    /// <summary>
    /// Emits a branch to the given label.
    /// </summary>
    protected void EmitBranch(System.Reflection.Emit.Label target)
    {
        IL.Emit(OpCodes.Br, target);
    }

    /// <summary>
    /// Emits a branch if false to the given label.
    /// Pops one value from the stack.
    /// </summary>
    protected void EmitBranchIfFalse(System.Reflection.Emit.Label target)
    {
        IL.Emit(OpCodes.Brfalse, target);
    }

    /// <summary>
    /// Emits a branch if true to the given label.
    /// Pops one value from the stack.
    /// </summary>
    protected void EmitBranchIfTrue(System.Reflection.Emit.Label target)
    {
        IL.Emit(OpCodes.Brtrue, target);
    }

    /// <summary>
    /// Creates a new label for control flow.
    /// </summary>
    protected System.Reflection.Emit.Label DefineLabel()
    {
        return IL.DefineLabel();
    }
}

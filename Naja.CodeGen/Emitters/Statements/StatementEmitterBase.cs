using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Statements;

/// <summary>
/// Base class for all statement emitters.
/// Provides shared access to context, IL generator, and common utilities.
/// </summary>
public abstract class StatementEmitterBase
{
    protected readonly EmitContext _ctx;
    protected readonly ExpressionEmitter _exprEmitter;
    protected ILGenerator IL => _ctx.IL;

    protected StatementEmitterBase(EmitContext ctx, ExpressionEmitter exprEmitter)
    {
        _ctx = ctx;
        _exprEmitter = exprEmitter;
    }

    /// <summary>
    /// Main dispatcher for statement emission.
    /// </summary>
    public abstract void Emit(Statement stmt);

    /// <summary>
    /// Emits a list of statements in sequence.
    /// </summary>
    public virtual void EmitAll(IReadOnlyList<Statement> stmts)
    {
        foreach (var stmt in stmts)
        {
            Emit(stmt);
        }
    }

    /// <summary>
    /// Emits a label for control flow.
    /// </summary>
    protected void EmitLabel(System.Reflection.Emit.Label label)
    {
        IL.MarkLabel(label);
    }

    /// <summary>
    /// Creates a new label for control flow.
    /// </summary>
    protected System.Reflection.Emit.Label DefineLabel()
    {
        return IL.DefineLabel();
    }

    /// <summary>
    /// Emits a branch to a label.
    /// </summary>
    protected void EmitBranch(System.Reflection.Emit.Label target)
    {
        IL.Emit(OpCodes.Br, target);
    }

    /// <summary>
    /// Emits a branch-if-false to a label (pops one value).
    /// </summary>
    protected void EmitBranchIfFalse(System.Reflection.Emit.Label target)
    {
        IL.Emit(OpCodes.Brfalse, target);
    }

    /// <summary>
    /// Emits a branch-if-true to a label (pops one value).
    /// </summary>
    protected void EmitBranchIfTrue(System.Reflection.Emit.Label target)
    {
        IL.Emit(OpCodes.Brtrue, target);
    }

    /// <summary>
    /// Collects all names assigned in a statement (for scoping).
    /// </summary>
    protected static ISet<string> CollectAssignedNames(Statement stmt)
    {
        var names = new HashSet<string>();

        if (stmt is AssignStatement assign)
        {
            foreach (var target in assign.Targets)
                CollectTargetNames(target, names);
        }
        else if (stmt is AnnAssignStatement annAssign)
        {
            CollectTargetNames(annAssign.Target, names);
        }
        else if (stmt is ForStatement forStmt)
        {
            CollectTargetNames(forStmt.Target, names);
        }

        return names;
    }

    /// <summary>
    /// Collects target names from an assignment target expression.
    /// </summary>
    private static void CollectTargetNames(Expression target, ISet<string> names)
    {
        if (target is NameExpr ne)
            names.Add(ne.Name);
        else if (target is TupleExpr te)
            foreach (var elem in te.Elements)
                CollectTargetNames(elem, names);
    }

    /// <summary>
    /// Collects all names referenced in a statement (for scoping analysis).
    /// </summary>
    protected static ISet<string> CollectReferencedNames(Statement stmt)
    {
        var names = new HashSet<string>();
        // This would recursively walk the AST to collect referenced names
        // Simplified for now - full implementation would need expression visitor
        return names;
    }
}

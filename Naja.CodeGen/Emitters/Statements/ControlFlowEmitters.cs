using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

// Disambiguate between System.Reflection.Emit.Label and System.Windows.Forms.Label
using ILLabel = System.Reflection.Emit.Label;

namespace Naja.CodeGen.Emitters.Statements;

/// <summary>
/// Emits IL for control flow statements (if, while, for, break, continue).
/// Manages label stacks for break/continue loop targets.
/// </summary>
public class ControlFlowEmitters : StatementEmitterBase
{
    private readonly Stack<ILLabel> _breakLabels = new();
    private readonly Stack<ILLabel> _continueLabels = new();
    // Exception-block depth at the point each loop was entered.
    // If current depth > entry depth the branch must use `leave` instead of `br`.
    private readonly Stack<int> _breakDepths = new();
    private readonly Stack<int> _continueDepths = new();

    public ControlFlowEmitters(EmitContext ctx, ExpressionEmitter exprEmitter, Action<Statement> emitStatement)
        : base(ctx, exprEmitter, emitStatement)
    {
    }

    public Stack<ILLabel> BreakLabels => _breakLabels;
    public Stack<ILLabel> ContinueLabels => _continueLabels;

    // ── If statement ──────────────────────────────────────────────────────────

    public void EmitIf(IfStatement s)
    {
        var endLabel = IL.DefineLabel();
        var elseLabel = IL.DefineLabel();

        var condType = _exprEmitter.Emit(s.Condition);
        EmitBrFalse(condType, s.Elifs.Count > 0 || s.Else.Count > 0 ? elseLabel : endLabel);

        EmitAll(s.Then);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(elseLabel);

        foreach (var (elifCond, elifBody) in s.Elifs)
        {
            var nextLabel = IL.DefineLabel();
            var elifCondType = _exprEmitter.Emit(elifCond);
            EmitBrFalse(elifCondType, nextLabel);
            EmitAll(elifBody);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(nextLabel);
        }

        EmitAll(s.Else);
        IL.MarkLabel(endLabel);
    }

    // ── While statement ───────────────────────────────────────────────────────

    public void EmitWhile(WhileStatement s)
    {
        var loopStart = IL.DefineLabel();
        // Python while/else: else runs only if loop exits normally (no break).
        var normalEnd = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        // Push labels for break/continue
        _breakLabels.Push(endLabel);      // break skips else
        _breakDepths.Push(_ctx.ExceptionBlockDepth);
        _continueLabels.Push(loopStart);
        _continueDepths.Push(_ctx.ExceptionBlockDepth);

        IL.MarkLabel(loopStart);
        var whileCondType = _exprEmitter.Emit(s.Condition);
        EmitBrFalse(whileCondType, normalEnd);

        EmitAll(s.Body);
        EmitLoopBack(loopStart);

        // Normal termination: run else
        IL.MarkLabel(normalEnd);

        _breakLabels.Pop();
        _breakDepths.Pop();
        _continueLabels.Pop();
        _continueDepths.Pop();

        EmitAll(s.Else);
        IL.MarkLabel(endLabel);
    }

    // ── For statement

    public void EmitFor(ForStatement s)
    {
        if (s.IsAsync)
            throw new CodeGenException("async for is not yet supported in Naja.", s.Line, s.Column);
        // for x in iterable:
        //   Compile to:
        //   iter = GetEnumerator(iterable)
        //   while iter.MoveNext():
        //     x = iter.Current
        //     body

        var loopStart = IL.DefineLabel();
        // Python for/else: else runs only if loop exits normally (no break).
        var normalEnd = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        _breakLabels.Push(endLabel);      // break skips else
        _breakDepths.Push(_ctx.ExceptionBlockDepth);
        _continueLabels.Push(loopStart);
        _continueDepths.Push(_ctx.ExceptionBlockDepth);

        // Get the iterable
        var iterType = _exprEmitter.Emit(s.Iter);

        // Box and get Python-aware enumerator (string→chars, dict→keys, else normal IEnumerable)
        TypeMapper.EmitBox(IL, iterType);
        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetForLoopEnumerator_Method);

        // Store enumerator in local
        var enumLocal = _ctx.Locals.Declare($"__enum_{s.Line}_{s.Column}", typeof(System.Collections.IEnumerator));
        IL.Emit(OpCodes.Stloc, enumLocal);

        // Loop condition
        IL.MarkLabel(loopStart);
        IL.Emit(OpCodes.Ldloc, enumLocal);
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, normalEnd);

        // Load Current — IEnumerator.Current always returns object
        IL.Emit(OpCodes.Ldloc, enumLocal);
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;
        IL.Emit(OpCodes.Callvirt, current);
        // If the loop variable targets a value-typed storage location (local or module static field),
        // unbox the Current so we can store it correctly. Otherwise store as object.
        if (s.Target is NameExpr loopVar)
        {
            // 1) Module-level static field (module variables are often emitted as static fields).
            if (_ctx.Fields.TryGetValue(loopVar.Name, out var staticField) && staticField.FieldType.IsValueType)
            {
                // Stack has object (boxed value), must unbox before storing into long/double/bool field
                IL.Emit(OpCodes.Unbox_Any, staticField.FieldType);
                EmitStoreInFor(
                    s.Target,
                    staticField.FieldType == typeof(long) ? NajaTypes.Int :
                    staticField.FieldType == typeof(double) ? NajaTypes.Float :
                    staticField.FieldType == typeof(bool) ? NajaTypes.Bool :
                    NajaTypes.Unknown);
            }
            else
            {
                var existingLocal = _ctx.Locals.TryGet(loopVar.Name);
                if (existingLocal != null && existingLocal.LocalType.IsValueType)
                {
                    // Stack has object (boxed value), must unbox before storing into long/double local
                    IL.Emit(OpCodes.Unbox_Any, existingLocal.LocalType);
                    EmitStoreInFor(s.Target, existingLocal.LocalType == typeof(long) ? NajaTypes.Int :
                                            existingLocal.LocalType == typeof(double) ? NajaTypes.Float :
                                            NajaTypes.Unknown);
                }
                else
                {
                    // Store as object (allows iterating strings, lists, tuples, etc.)
                    EmitStoreInFor(s.Target, NajaTypes.Unknown);
                }
            }
        }
        else
        {
            EmitStoreInFor(s.Target, NajaTypes.Unknown);
        }

        // Body
        EmitAll(s.Body);
        EmitLoopBack(loopStart);

        // Normal termination: run else
        IL.MarkLabel(normalEnd);

        _breakLabels.Pop();
        _breakDepths.Pop();
        _continueLabels.Pop();
        _continueDepths.Pop();

        EmitAll(s.Else);
        IL.MarkLabel(endLabel);
    }

    // Store method for for-loop targets (simpler than full EmitStore)
    private void EmitStoreInFor(Expression target, NajaType valueType)
    {
        switch (target)
        {
            case NameExpr n:
                if (_ctx.Fields.TryGetValue(n.Name, out var staticField))
                {
                    if (staticField.FieldType == typeof(object))
                        TypeMapper.EmitBox(IL, valueType);
                    else if (staticField.FieldType.IsValueType && valueType is UnknownType)
                        IL.Emit(OpCodes.Unbox_Any, staticField.FieldType);
                    else if (staticField.FieldType == typeof(string) && valueType is UnknownType)
                        IL.Emit(OpCodes.Castclass, typeof(string));
                    IL.Emit(OpCodes.Stsfld, staticField);
                }
                else
                {
                    var clrType = TypeMapper.ToClrType(valueType);
                    if (clrType == typeof(void)) clrType = typeof(object);
                    if (!_ctx.Locals.Contains(n.Name))
                        _ctx.Locals.Declare(n.Name, clrType);
                    _ctx.Locals.EmitStore(n.Name);
                }
                break;

            case TupleExpr t:
            {
                // Tuple unpacking: for k, v in pairs — store the element, then index into it
                var tmpTuple = _ctx.Locals.Declare($"__unpack_{t.Line}_{t.Column}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmpTuple);
                for (int idx = 0; idx < t.Elements.Count; idx++)
                {
                    IL.Emit(OpCodes.Ldloc, tmpTuple);
                    IL.Emit(OpCodes.Ldc_I4, idx);
                    IL.Emit(OpCodes.Box, typeof(int));
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetItem_Method);
                    EmitStoreInFor(t.Elements[idx], NajaTypes.Unknown);
                }
                break;
            }

            default:
                IL.Emit(OpCodes.Pop);
                break;
        }
    }

    // ── Break / Continue ──────────────────────────────────────────────────────

    public void EmitBreak(BreakStatement s)
    {
        if (_breakLabels.Count == 0)
            throw new CodeGenException("break outside loop", s.Line, s.Column);
        EmitJumpToLoopTarget(_breakLabels.Peek(), _breakDepths.Peek());
    }

    public void EmitContinue(ContinueStatement s)
    {
        if (_continueLabels.Count == 0)
            throw new CodeGenException("continue outside loop", s.Line, s.Column);
        EmitJumpToLoopTarget(_continueLabels.Peek(), _continueDepths.Peek());
    }

    /// <summary>
    /// Emit a branch back to a loop start/end label.
    /// Uses `leave` when the current exception block depth is greater than it was
    /// when the loop was entered (the branch must exit one or more protected regions).
    /// </summary>
    private void EmitJumpToLoopTarget(ILLabel target, int loopEntryDepth)
    {
        if (_ctx.ExceptionBlockDepth > loopEntryDepth)
            IL.Emit(OpCodes.Leave, target);
        else
            IL.Emit(OpCodes.Br, target);
    }

    /// <summary>
    /// Emit the loop back-edge (end of body → loop-start label).
    /// </summary>
    private void EmitLoopBack(ILLabel loopStart)
    {
        // The loop back-edge always targets a label at the same depth as loop entry
        // (inside the same try block, or outside any try block), so `br` is fine.
        IL.Emit(OpCodes.Br, loopStart);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Emit a Brfalse respecting Python truthiness.
    // Primitive numeric types (int/float/bool) use Brfalse directly (zero check).
    // All other types (string, object, collections) call ToBool first.
    private void EmitBrFalse(NajaType condType, ILLabel label)
    {
        if (condType is not (IntType or FloatType or BoolType))
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToBool_Method);
        IL.Emit(OpCodes.Brfalse, label);
    }
}

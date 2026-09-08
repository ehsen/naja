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

        var condType = _mainEmitter.Emit(e.Condition);
        EmitBrFalse(condType, elseLabel);
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
    /// When inside a comprehension helper, the variable is stored to a static field
    /// so it is visible in the enclosing module scope (PEP 572 semantics).
    /// </summary>
    public NajaType EmitWalrus(WalrusExpr e)
    {
        var type = _mainEmitter.Emit(e.Value);

        // PEP 572: walrus target binds in the ENCLOSING scope, not the comprehension scope.
        // When ComprehensionScopeId is set, we are inside a comprehension helper method;
        // store to a static field so the enclosing scope can read the last value.
        if (_ctx.ComprehensionScopeId != null)
        {
            IL.Emit(OpCodes.Dup);
            if (!_ctx.Fields.TryGetValue(e.Target, out var walrusField))
            {
                walrusField = _ctx.TypeBuilder.DefineField(
                    e.Target, typeof(object),
                    System.Reflection.FieldAttributes.Public | System.Reflection.FieldAttributes.Static);
                _ctx.Fields[e.Target] = walrusField;
            }
            TypeMapper.EmitBox(IL, type);
            IL.Emit(OpCodes.Stsfld, walrusField);
            return type;
        }

        // Declare local and store a copy, then leave value on stack.
        // STORE/LOAD CONSISTENCY: when Pass 1 hoisted the target to a static
        // field, reads of that name resolve the FIELD first (NameEmitters
        // priority) — storing only a local leaves the field stale (a read of
        // `d` after `while a > (d := 3)` returned 0). Store BOTH: field first
        // (authoritative), then the local for any existing local readers.
        var clrType = TypeMapper.ToClrType(type);
        if (clrType == typeof(void)) clrType = typeof(object);

        if (_ctx.Fields.TryGetValue(e.Target, out var walrusHoistedField))
        {
            IL.Emit(OpCodes.Dup);
            if (walrusHoistedField.FieldType == typeof(object))
            {
                TypeMapper.EmitBox(IL, type);
            }
            else if (walrusHoistedField.FieldType == typeof(double) &&
                     type is IntType or BoolType)
            {
                IL.Emit(OpCodes.Conv_R8);
            }
            else if (walrusHoistedField.FieldType == typeof(long) && type is FloatType)
            {
                IL.Emit(OpCodes.Conv_I8);
            }
            else if (walrusHoistedField.FieldType.IsValueType && type is UnknownType)
            {
                IL.Emit(OpCodes.Unbox_Any, walrusHoistedField.FieldType);
            }
            else if (walrusHoistedField.FieldType == typeof(string) && type is UnknownType)
            {
                IL.Emit(OpCodes.Castclass, typeof(string));
            }
            else if (walrusHoistedField.FieldType != clrType && !walrusHoistedField.FieldType.IsValueType)
            {
                TypeMapper.EmitBox(IL, type);
                IL.Emit(OpCodes.Castclass, walrusHoistedField.FieldType);
            }
            IL.Emit(OpCodes.Stsfld, walrusHoistedField);
        }

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

    // Emit Brfalse respecting Python truthiness.
    // Primitives (int/float/bool) use Brfalse directly; everything else calls ToBool first.
    private void EmitBrFalse(NajaType condType, System.Reflection.Emit.Label label)
    {
        if (condType is not (IntType or FloatType or BoolType))
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToBool_Method);
        IL.Emit(OpCodes.Brfalse, label);
    }
}

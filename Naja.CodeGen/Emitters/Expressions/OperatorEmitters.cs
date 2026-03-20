using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Emits IL opcodes for all operator expressions: binary, unary, boolean, and comparison.
/// Handles binary ops (Add, Sub, Mul, Div, FloorDiv, Mod, Pow, bitwise, shifts),
/// unary ops (Neg, Pos, Invert, Not), boolean ops (And, Or), and comparisons.
/// </summary>
public sealed class OperatorEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public OperatorEmitters(EmitContext ctx, ExpressionEmitter mainEmitter) : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    public NajaType EmitBinary(BinaryExpr e)
    {
        switch (e.Op)
        {
            case BinaryOp.Add:
                {
                    var leftType = _mainEmitter.Emit(e.Left);
                    var rightType = _mainEmitter.Emit(e.Right);
                    if (leftType is StrType && rightType is StrType)
                    {
                        var concat = typeof(string).GetMethod("Concat",
                            new[] { typeof(string), typeof(string) })!;
                        IL.Emit(OpCodes.Call, concat);
                        return NajaTypes.Str;
                    }
                    if (leftType is UnknownType || rightType is UnknownType)
                    {
                        TypeMapper.EmitBox(IL, rightType);
                        var tmpR = _ctx.Locals.Declare($"__dynr_{e.Line}", typeof(object));
                        IL.Emit(OpCodes.Stloc, tmpR);
                        TypeMapper.EmitBox(IL, leftType);
                        IL.Emit(OpCodes.Ldloc, tmpR);

                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicAdd_Method);
                        return NajaTypes.Unknown;
                    }
                    IL.Emit(OpCodes.Add);
                    return (leftType is FloatType || rightType is FloatType)
                        ? NajaTypes.Float : NajaTypes.Int;
                }

            case BinaryOp.Sub:
                {
                    var l = _mainEmitter.Emit(e.Left);
                    var r = _mainEmitter.Emit(e.Right);
                    if (l is UnknownType || r is UnknownType)
                    {
                        TypeMapper.EmitBox(IL, r);
                        var tmpR = _ctx.Locals.Declare($"__dynr_{e.Line}", typeof(object));
                        IL.Emit(OpCodes.Stloc, tmpR);
                        TypeMapper.EmitBox(IL, l);
                        IL.Emit(OpCodes.Ldloc, tmpR);

                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicSub_Method);
                        return NajaTypes.Unknown;
                    }
                    IL.Emit(OpCodes.Sub);
                    return (l is FloatType || r is FloatType) ? NajaTypes.Float : NajaTypes.Int;
                }

            case BinaryOp.Mul:
                {
                    var l = _mainEmitter.Emit(e.Left);
                    var r = _mainEmitter.Emit(e.Right);
                    if (l is UnknownType || r is UnknownType)
                    {
                        TypeMapper.EmitBox(IL, r);
                        var tmpR = _ctx.Locals.Declare($"__dynr_{e.Line}", typeof(object));
                        IL.Emit(OpCodes.Stloc, tmpR);
                        TypeMapper.EmitBox(IL, l);
                        IL.Emit(OpCodes.Ldloc, tmpR);

                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicMul_Method);
                        return NajaTypes.Unknown;
                    }
                    if (l is FloatType && r is IntType)
                    {
                        var tmpR = _ctx.Locals.Declare($"__mulr_{e.Line}", typeof(long));
                        var tmpL = _ctx.Locals.Declare($"__mull_{e.Line}", typeof(double));
                        IL.Emit(OpCodes.Stloc, tmpR);
                        IL.Emit(OpCodes.Stloc, tmpL);
                        IL.Emit(OpCodes.Ldloc, tmpL);
                        IL.Emit(OpCodes.Ldloc, tmpR);
                        IL.Emit(OpCodes.Conv_R8);
                    }
                    else if (l is IntType && r is FloatType)
                    {
                        var tmpR = _ctx.Locals.Declare($"__mulr_{e.Line}", typeof(double));
                        IL.Emit(OpCodes.Stloc, tmpR);
                        IL.Emit(OpCodes.Conv_R8);
                        IL.Emit(OpCodes.Ldloc, tmpR);
                    }
                    IL.Emit(OpCodes.Mul);
                    return (l is FloatType || r is FloatType) ? NajaTypes.Float : NajaTypes.Int;
                }

            case BinaryOp.Div:
                {
                    var l = _mainEmitter.Emit(e.Left);

                    if (l is UnknownType)
                    {
                        TypeMapper.EmitBox(IL, l);
                        var tmpL = _ctx.Locals.Declare($"__divl_{e.Line}", typeof(object));
                        IL.Emit(OpCodes.Stloc, tmpL);

                        var rDyn = _mainEmitter.Emit(e.Right);
                        TypeMapper.EmitBox(IL, rDyn);
                        var tmpR = _ctx.Locals.Declare($"__divr_{e.Line}", typeof(object));
                        IL.Emit(OpCodes.Stloc, tmpR);

                        IL.Emit(OpCodes.Ldloc, tmpL);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
                        IL.Emit(OpCodes.Ldloc, tmpR);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
                    }
                    else
                    {
                        if (l is IntType) IL.Emit(OpCodes.Conv_R8);

                        var r = _mainEmitter.Emit(e.Right);
                        if (r is UnknownType)
                        {
                            TypeMapper.EmitBox(IL, r);
                            var tmpR = _ctx.Locals.Declare($"__divr_{e.Line}", typeof(object));
                            IL.Emit(OpCodes.Stloc, tmpR);
                            IL.Emit(OpCodes.Ldloc, tmpR);
                            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
                        }
                        else
                        {
                            if (r is IntType) IL.Emit(OpCodes.Conv_R8);
                        }
                    }

                    var tmpDivR = _ctx.Locals.Declare($"__divd_{e.Line}", typeof(double));
                    IL.Emit(OpCodes.Stloc, tmpDivR);
                    IL.Emit(OpCodes.Ldloc, tmpDivR);
                    IL.Emit(OpCodes.Ldc_R8, 0.0);
                    IL.Emit(OpCodes.Ceq);
                    var notZeroLbl = DefineLabel();
                    IL.Emit(OpCodes.Brfalse, notZeroLbl);
                    var dbzCtor = typeof(System.DivideByZeroException).GetConstructor(Type.EmptyTypes)!;
                    IL.Emit(OpCodes.Newobj, dbzCtor);
                    IL.Emit(OpCodes.Throw);
                    EmitLabel(notZeroLbl);
                    IL.Emit(OpCodes.Ldloc, tmpDivR);
                    IL.Emit(OpCodes.Div);
                    return NajaTypes.Float;
                }

            case BinaryOp.FloorDiv:
                {
                    var l = _mainEmitter.Emit(e.Left);
                    var r = _mainEmitter.Emit(e.Right);

                    if (l is UnknownType || r is UnknownType)
                    {
                        var tmpR = _ctx.Locals.Declare($"__fdr_dyn_{e.Line}", typeof(object));
                        TypeMapper.EmitBox(IL, r);
                        IL.Emit(OpCodes.Stloc, tmpR);

                        TypeMapper.EmitBox(IL, l);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);

                        IL.Emit(OpCodes.Ldloc, tmpR);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);

                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyFloorDivF_Method);
                        return NajaTypes.Float;
                    }

                    if (l is FloatType || r is FloatType)
                    {
                        var tmpFdr = _ctx.Locals.Declare($"__fdr_{e.Line}", typeof(double));
                        if (r is IntType) IL.Emit(OpCodes.Conv_R8);
                        IL.Emit(OpCodes.Stloc, tmpFdr);
                        if (l is IntType) IL.Emit(OpCodes.Conv_R8);
                        IL.Emit(OpCodes.Ldloc, tmpFdr);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyFloorDivF_Method);
                        return NajaTypes.Float;
                    }

                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyFloorDiv_Method);
                    return NajaTypes.Int;
                }

            case BinaryOp.Mod:
                {
                    var l = _mainEmitter.Emit(e.Left);
                    var r = _mainEmitter.Emit(e.Right);
                    if (l is UnknownType || r is UnknownType)
                    {
                        var tmpModR = _ctx.Locals.Declare($"__modr_{e.Line}", typeof(object));
                        TypeMapper.EmitBox(IL, r);
                        IL.Emit(OpCodes.Stloc, tmpModR);
                        TypeMapper.EmitBox(IL, l);
                        IL.Emit(OpCodes.Ldloc, tmpModR);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicMod_Method);
                        return NajaTypes.Unknown;
                    }
                    if (l is FloatType || r is FloatType)
                    {
                        var tmpFmod = _ctx.Locals.Declare($"__fmod_{e.Line}", typeof(double));
                        if (r is IntType) IL.Emit(OpCodes.Conv_R8);
                        IL.Emit(OpCodes.Stloc, tmpFmod);
                        if (l is IntType) IL.Emit(OpCodes.Conv_R8);
                        IL.Emit(OpCodes.Ldloc, tmpFmod);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyModF_Method);
                        return NajaTypes.Float;
                    }
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyMod_Method);
                    return NajaTypes.Int;
                }

            case BinaryOp.Pow:
                {
                    var l = _mainEmitter.Emit(e.Left);

                    if (l is UnknownType)
                    {
                        TypeMapper.EmitBox(IL, l);
                        var tmpL = _ctx.Locals.Declare($"__powl_{e.Line}", typeof(object));
                        IL.Emit(OpCodes.Stloc, tmpL);

                        var rDyn = _mainEmitter.Emit(e.Right);
                        TypeMapper.EmitBox(IL, rDyn);
                        var tmpR = _ctx.Locals.Declare($"__powr_{e.Line}", typeof(object));
                        IL.Emit(OpCodes.Stloc, tmpR);

                        IL.Emit(OpCodes.Ldloc, tmpL);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
                        IL.Emit(OpCodes.Ldloc, tmpR);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
                    }
                    else
                    {
                        if (l is IntType) IL.Emit(OpCodes.Conv_R8);

                        var r = _mainEmitter.Emit(e.Right);
                        if (r is UnknownType)
                        {
                            TypeMapper.EmitBox(IL, r);
                            var tmpR = _ctx.Locals.Declare($"__powr_{e.Line}", typeof(object));
                            IL.Emit(OpCodes.Stloc, tmpR);
                            IL.Emit(OpCodes.Ldloc, tmpR);
                            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
                        }
                        else
                        {
                            if (r is IntType) IL.Emit(OpCodes.Conv_R8);
                        }
                    }

                    IL.Emit(OpCodes.Call, typeof(Math).GetMethod("Pow", new[] { typeof(double), typeof(double) })!);
                    return NajaTypes.Float;
                }

            case BinaryOp.BitAnd:
                _mainEmitter.Emit(e.Left); _mainEmitter.Emit(e.Right);
                IL.Emit(OpCodes.And);
                return NajaTypes.Int;

            case BinaryOp.BitOr:
                _mainEmitter.Emit(e.Left); _mainEmitter.Emit(e.Right);
                IL.Emit(OpCodes.Or);
                return NajaTypes.Int;

            case BinaryOp.BitXor:
                _mainEmitter.Emit(e.Left); _mainEmitter.Emit(e.Right);
                IL.Emit(OpCodes.Xor);
                return NajaTypes.Int;

            case BinaryOp.LShift:
                _mainEmitter.Emit(e.Left); _mainEmitter.Emit(e.Right);
                IL.Emit(OpCodes.Shl);
                return NajaTypes.Int;

            case BinaryOp.RShift:
                _mainEmitter.Emit(e.Left); _mainEmitter.Emit(e.Right);
                IL.Emit(OpCodes.Shr);
                return NajaTypes.Int;

            case BinaryOp.MatMul:
                throw new CodeGenException("Matrix multiply (@) not supported yet", e.Line, e.Column);

            default:
                throw new CodeGenException($"Unknown binary op {e.Op}", e.Line, e.Column);
        }
    }

    public NajaType EmitUnary(UnaryExpr e)
    {
        var operandType = _mainEmitter.Emit(e.Operand);
        switch (e.Op)
        {
            case UnaryOp.Neg:
                IL.Emit(OpCodes.Neg);
                return operandType;
            case UnaryOp.Pos:
                return operandType;
            case UnaryOp.Invert:
                IL.Emit(OpCodes.Not);
                return NajaTypes.Int;
            case UnaryOp.Not:
                if (operandType is BoolType || operandType is IntType)
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Ceq);
                }
                else
                {
                    TypeMapper.EmitBox(IL, operandType);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToBool_Method);
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Ceq);
                }
                return NajaTypes.Bool;
        }
        return NajaTypes.Unknown;
    }

    public NajaType EmitBoolOp(BoolOpExpr e)
    {
        var endLabel = DefineLabel();
        NajaType resultType = NajaTypes.Unknown;
        var toBool = NajaBuiltinsMethodCache.ToBool_Method;

        for (int i = 0; i < e.Values.Count; i++)
        {
            resultType = _mainEmitter.Emit(e.Values[i]);

            if (i < e.Values.Count - 1)
            {
                IL.Emit(OpCodes.Dup);
                TypeMapper.EmitBox(IL, resultType);
                IL.Emit(OpCodes.Call, toBool);

                if (e.Op == BoolOp.And)
                    IL.Emit(OpCodes.Brfalse, endLabel);
                else
                    IL.Emit(OpCodes.Brtrue, endLabel);

                IL.Emit(OpCodes.Pop);
            }
        }

        EmitLabel(endLabel);
        return resultType;
    }

    public NajaType EmitCompare(CompareExpr e)
    {
        if (e.Comparators.Count == 1)
        {
            var (op, right) = e.Comparators[0];
            EmitSingleComparison(e.Left, op, right);
            return NajaTypes.Bool;
        }

        var andLabel = DefineLabel();
        var endLabel = DefineLabel();

        var leftType = _mainEmitter.Emit(e.Left);
        var tempLeft = _ctx.Locals.Declare("__cmp_tmp", TypeMapper.ToClrType(leftType));
        IL.Emit(OpCodes.Stloc, tempLeft);

        for (int i = 0; i < e.Comparators.Count; i++)
        {
            var (op, right) = e.Comparators[i];
            IL.Emit(OpCodes.Ldloc, tempLeft);
            var rightType = _mainEmitter.Emit(right);

            if (i < e.Comparators.Count - 1)
            {
                var tempRight = _ctx.Locals.Declare($"__cmp_tmp{i}", TypeMapper.ToClrType(rightType));
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, tempRight);
                EmitCompareOp(op);
                IL.Emit(OpCodes.Brfalse, andLabel);
                IL.Emit(OpCodes.Ldloc, tempRight);
                IL.Emit(OpCodes.Stloc, tempLeft);
            }
            else
            {
                EmitCompareOp(op);
                IL.Emit(OpCodes.Br, endLabel);
            }
        }

        EmitLabel(andLabel);
        IL.Emit(OpCodes.Ldc_I4_0);
        EmitLabel(endLabel);
        return NajaTypes.Bool;
    }

    private void EmitSingleComparison(Expression left, CompareOp op, Expression right)
    {
        if (op == CompareOp.Is || op == CompareOp.IsNot)
        {
            _mainEmitter.Emit(left);
            _mainEmitter.Emit(right);
            IL.Emit(OpCodes.Ceq);
            if (op == CompareOp.IsNot) { IL.Emit(OpCodes.Ldc_I4_0); IL.Emit(OpCodes.Ceq); }
            return;
        }

        if (op == CompareOp.In || op == CompareOp.NotIn)
        {
            var lt = _mainEmitter.Emit(left); TypeMapper.EmitBox(IL, lt);
            var rt = _mainEmitter.Emit(right); TypeMapper.EmitBox(IL, rt);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.Contains_Method);
            if (op == CompareOp.NotIn) { IL.Emit(OpCodes.Ldc_I4_0); IL.Emit(OpCodes.Ceq); }
            return;
        }

        var leftType = _mainEmitter.Emit(left);
        var rightType = _mainEmitter.Emit(right);

        if ((op == CompareOp.Eq || op == CompareOp.NotEq) && leftType is StrType && rightType is StrType)
        {
            var stringEquals = typeof(string).GetMethod("Equals",
                new[] { typeof(string), typeof(string) })!;
            IL.Emit(OpCodes.Call, stringEquals);
            if (op == CompareOp.NotEq)
            {
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
            }
            return;
        }

        if ((op == CompareOp.Eq || op == CompareOp.NotEq) &&
            (leftType is ListType || rightType is ListType ||
             leftType is DictType || rightType is DictType ||
             leftType is SetType || rightType is SetType ||
             leftType is TupleType || rightType is TupleType))
        {
            var tmpRight = _ctx.Locals.Declare("__cmp_r", typeof(object));
            TypeMapper.EmitBox(IL, rightType);
            IL.Emit(OpCodes.Stloc, tmpRight);

            TypeMapper.EmitBox(IL, leftType);
            IL.Emit(OpCodes.Ldloc, tmpRight);

            var dynMethod = op == CompareOp.Eq ? NajaBuiltinsMethodCache.DynamicEq_Method : NajaBuiltinsMethodCache.DynamicNotEq_Method;
            IL.Emit(OpCodes.Call, dynMethod);
            return;
        }

        if (leftType is UnknownType || rightType is UnknownType)
        {
            var tmpRight = _ctx.Locals.Declare("__cmp_r", typeof(object));
            TypeMapper.EmitBox(IL, rightType);
            IL.Emit(OpCodes.Stloc, tmpRight);

            TypeMapper.EmitBox(IL, leftType);
            IL.Emit(OpCodes.Ldloc, tmpRight);

            var dynMethod = op switch
            {
                CompareOp.Eq => NajaBuiltinsMethodCache.DynamicEq_Method,
                CompareOp.NotEq => NajaBuiltinsMethodCache.DynamicNotEq_Method,
                CompareOp.Lt => NajaBuiltinsMethodCache.DynamicLt_Method,
                CompareOp.LtEq => NajaBuiltinsMethodCache.DynamicLtEq_Method,
                CompareOp.Gt => NajaBuiltinsMethodCache.DynamicGt_Method,
                CompareOp.GtEq => NajaBuiltinsMethodCache.DynamicGtEq_Method,
                _ => throw new CodeGenException($"Unsupported dynamic comparison: {op}")
            };

            IL.Emit(OpCodes.Call, dynMethod);
            return;
        }

        EmitCompareOp(op);
    }

    private void EmitCompareOp(CompareOp op)
    {
        switch (op)
        {
            case CompareOp.Eq: IL.Emit(OpCodes.Ceq); break;
            case CompareOp.NotEq:
                IL.Emit(OpCodes.Ceq);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
                break;
            case CompareOp.Lt: IL.Emit(OpCodes.Clt); break;
            case CompareOp.Gt: IL.Emit(OpCodes.Cgt); break;
            case CompareOp.LtEq:
                IL.Emit(OpCodes.Cgt);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
                break;
            case CompareOp.GtEq:
                IL.Emit(OpCodes.Clt);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ceq);
                break;
        }
    }

    /// <summary>
    /// Not implemented in this emitter - used by ExpressionEmitter for dispatch.
    /// Call the specific methods (EmitBinary, EmitUnary, EmitBoolOp, EmitCompare) directly.
    /// </summary>
    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException("Use specialized methods: EmitBinary, EmitUnary, EmitBoolOp, EmitCompare");
    }
}

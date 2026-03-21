using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Naja.CodeGen.Emitters.Expressions;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen;

/// <summary>
/// Dispatcher for expression emission to specialist emitters.
/// Routes all expression types to their respective handlers.
/// Every Emit method leaves exactly ONE value on the evaluation stack.
/// </summary>
public sealed class ExpressionEmitter
{
    private readonly EmitContext _ctx;
    private ILGenerator IL => _ctx.IL;

    // Specialist emitters
    private readonly LiteralEmitters _literalEmitters;
    private readonly CollectionEmitters _collectionEmitters;
    private readonly OperatorEmitters _operatorEmitters;
    private readonly CallEmitters _callEmitters;
    private readonly AttributeEmitters _attributeEmitters;
    private readonly ControlFlowEmitters _controlFlowEmitters;
    private readonly ComprehensionEmitters _comprehensionEmitters;
    private readonly GeneratorEmitters _generatorEmitters;
    private readonly LambdaEmitters _lambdaEmitters;
    private readonly FStringEmitters _fstringEmitters;
    private readonly NameEmitters _nameEmitters;

    public ExpressionEmitter(EmitContext ctx)
    {
        _ctx = ctx;
        _literalEmitters = new LiteralEmitters(ctx);
        _collectionEmitters = new CollectionEmitters(ctx, this);
        _operatorEmitters = new OperatorEmitters(ctx, this);
        _callEmitters = new CallEmitters(ctx, this);
        _attributeEmitters = new AttributeEmitters(ctx, this);
        _controlFlowEmitters = new ControlFlowEmitters(ctx, this);
        _comprehensionEmitters = new ComprehensionEmitters(ctx, this);
        _generatorEmitters = new GeneratorEmitters(ctx, this);
        _lambdaEmitters = new LambdaEmitters(ctx, this);
        _fstringEmitters = new FStringEmitters(ctx, this);
        _nameEmitters = new NameEmitters(ctx, this);
    }

    // ── Main dispatch ─────────────────────────────────────────────────────────

    public NajaType Emit(Expression expr)
    {
        var type = expr switch
        {
            IntLiteral e => _literalEmitters.EmitLiteral(e),
            FloatLiteral e => _literalEmitters.EmitLiteral(e),
            StringLiteral e => _literalEmitters.EmitLiteral(e),
            FStringExpr e => _fstringEmitters.EmitFString(e),
            BoolLiteral e => _literalEmitters.EmitLiteral(e),
            NoneLiteral e => _literalEmitters.EmitLiteral(e),
            EllipsisLiteral e => _literalEmitters.EmitLiteral(e),
            NameExpr e => _nameEmitters.EmitName(e),
            BinaryExpr e => _operatorEmitters.EmitBinary(e),
            UnaryExpr e => _operatorEmitters.EmitUnary(e),
            BoolOpExpr e => _operatorEmitters.EmitBoolOp(e),
            CompareExpr e => _operatorEmitters.EmitCompare(e),
            IfExpr e => _controlFlowEmitters.EmitIfExpr(e),
            WalrusExpr e => _controlFlowEmitters.EmitWalrus(e),
            CallExpr e => _callEmitters.EmitCall(e),
            AttributeExpr e => _attributeEmitters.EmitAttribute(e),
            SubscriptExpr e => _attributeEmitters.EmitSubscript(e),
            SliceExpr e => _attributeEmitters.EmitSlice(e),
            LambdaExpr e => _lambdaEmitters.EmitLambda(e),
            ListExpr e => _collectionEmitters.EmitCollection(e),
            TupleExpr e => _collectionEmitters.EmitCollection(e),
            SetExpr e => _collectionEmitters.EmitCollection(e),
            DictExpr e => _collectionEmitters.EmitCollection(e),
            ListCompExpr e => _comprehensionEmitters.EmitListComp(e),
            SetCompExpr e => _comprehensionEmitters.EmitSetComp(e),
            DictCompExpr e => _comprehensionEmitters.EmitDictComp(e),
            GeneratorExpr e => _comprehensionEmitters.EmitGenerator(e),
            StarredExpr e => _collectionEmitters.EmitStarred(e),
            AwaitExpr e => throw new CodeGenException("async/await not yet supported", e.Line, e.Column),
            YieldExpr e => _generatorEmitters.EmitYield(e),
            _ => throw new CodeGenException(
                                     $"Cannot emit expression: {expr.GetType().Name}",
                                     expr.Line, expr.Column)
        };

        return type;
    }

    // ── Comprehension infrastructure ───────────────────────────────────────────
    // These helpers are used by ComprehensionEmitters for scoping and iteration

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
            var iterType = Emit(gen.Iter);
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
            var condType = Emit(cond);
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
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Box, typeof(int));
                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetItem_Method);
                StoreComprehensionTarget(t.Elements[i]);
            }
        }
        else
        {
            IL.Emit(OpCodes.Pop);
        }
    }

    // ── Utility methods (moved from inline) ──────────────────────────────────

    /// <summary>
    /// Converts a value to double if needed (for numeric coercion).
    /// </summary>
    internal void ConvertToDouble(NajaType t)
    {
        if (t is IntType) IL.Emit(OpCodes.Conv_R8);
    }

    /// <summary>
    /// Emits default values for parameters and constants.
    /// </summary>
    internal void EmitDefaultValue(object? value)
    {
        if (value is null || value == System.Type.Missing)
            IL.Emit(OpCodes.Ldnull);
        else if (value is long l) { IL.Emit(OpCodes.Ldc_I8, l); }
        else if (value is int i2) { IL.Emit(OpCodes.Ldc_I4, i2); IL.Emit(OpCodes.Conv_I8); }
        else if (value is double d) { IL.Emit(OpCodes.Ldc_R8, d); }
        else if (value is float f) { IL.Emit(OpCodes.Ldc_R8, (double)f); }
        else if (value is bool b) { IL.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); }
        else if (value is string s) { IL.Emit(OpCodes.Ldstr, s); }
        else { IL.Emit(OpCodes.Ldnull); }
    }

    /// <summary>
    /// Human-readable description of an expression (for error messages).
    /// </summary>
    internal static string DescribeExpr(Expression expr) => expr switch
    {
        NameExpr e => e.Name,
        CallExpr e => $"{DescribeExpr(e.Func)}(...)",
        AttributeExpr e => $"{DescribeExpr(e.Object)}.{e.Attribute}",
        StringLiteral e => $"'{e.Value}'",
        IntLiteral e => e.Value.ToString(),
        _ => expr.GetType().Name
    };

    /// <summary>
    /// Parse a cast target type name (int, float, str, etc.).
    /// </summary>
    internal static NajaType ParseCastTarget(Expression typeArg, int line, int col)
    {
        if (typeArg is not NameExpr n)
            throw new CodeGenException(
                $"cast() first argument must be a type name (int, float, str, bool, list, dict, set, bytes). " +
                $"Got: {typeArg.GetType().Name}", line, col);

        return n.Name switch
        {
            "int" => NajaTypes.Int,
            "float" => NajaTypes.Float,
            "str" => NajaTypes.Str,
            "bool" => NajaTypes.Bool,
            "bytes" => NajaTypes.Bytes,
            "list" => new ListType(NajaTypes.Unknown),
            "dict" => new DictType(NajaTypes.Unknown, NajaTypes.Unknown),
            "set" => new SetType(NajaTypes.Unknown),
            _ => throw new CodeGenException(
                $"cast(): unknown target type '{n.Name}'. " +
                $"Supported: int, float, str, bool, bytes, list, dict, set.", line, col)
        };
    }

    /// <summary>
    /// Emits type coercion IL (e.g., int to float, value to bool).
    /// Assumes value is already on the stack.
    /// </summary>
    internal static void EmitCoercion(
        ILGenerator il, NajaType from, NajaType to, int line, int col)
    {
        if (from.GetType() == to.GetType()) return;  // already correct type on stack

        var clrTo = TypeMapper.ToClrType(to);

        switch (to)
        {
            case IntType:
                if (from is FloatType) il.Emit(OpCodes.Conv_I8);
                else if (from is BoolType) il.Emit(OpCodes.Conv_I8);
                else if (from is UnknownType) il.Emit(OpCodes.Unbox_Any, typeof(long));
                else if (from is StrType)
                {
                    // call TypeConversion.ToInt(string) → long
                    il.Emit(OpCodes.Call, typeof(Builtins.TypeConversion).GetMethod(nameof(Builtins.TypeConversion.ToInt))!);
                }
                break;

            case FloatType:
                if (from is IntType or BoolType) il.Emit(OpCodes.Conv_R8);
                else if (from is UnknownType) il.Emit(OpCodes.Unbox_Any, typeof(double));
                else if (from is StrType)
                {
                    il.Emit(OpCodes.Call, typeof(Builtins.TypeConversion).GetMethod(nameof(Builtins.TypeConversion.ToFloat))!);
                }
                break;

            case BoolType:
                if (from is UnknownType)
                {
                    il.Emit(OpCodes.Call, typeof(Builtins.TypeConversion).GetMethod(nameof(Builtins.TypeConversion.ToBool))!);
                }
                else
                {
                    // Numeric → bool: != 0
                    il.Emit(OpCodes.Ldc_I4_0);
                    if (from is IntType) { il.Emit(OpCodes.Conv_I8); }
                    il.Emit(OpCodes.Ceq);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ceq);  // double-negate to get 0/1
                }
                break;

            case StrType:
                if (from is UnknownType)
                    il.Emit(OpCodes.Castclass, typeof(string));
                else
                {
                    TypeMapper.EmitBox(il, from);
                    il.Emit(OpCodes.Call, typeof(Builtins.TypeConversion).GetMethod(nameof(Builtins.TypeConversion.ToStr))!);
                }
                break;

            case ListType:
                il.Emit(OpCodes.Castclass, typeof(System.Collections.Generic.List<object>));
                break;

            case DictType:
                il.Emit(OpCodes.Castclass, typeof(System.Collections.Generic.Dictionary<object, object>));
                break;

            case SetType:
                il.Emit(OpCodes.Castclass, typeof(System.Collections.Generic.HashSet<object>));
                break;

            case BytesType:
                il.Emit(OpCodes.Castclass, typeof(byte[]));
                break;
        }
    }
}

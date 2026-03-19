using System.Reflection;
using System.Reflection.Emit;
using Naja.CodeGen.Emitters.Expressions;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen;

/// <summary>
/// Emits IL opcodes for expression nodes.
/// Every Emit method leaves exactly ONE value on the evaluation stack.
/// </summary>
public sealed class ExpressionEmitter
{
    private readonly EmitContext _ctx;
    private ILGenerator IL => _ctx.IL;

    // Specialist emitters
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
            IntLiteral e => EmitInt(e),
            FloatLiteral e => EmitFloat(e),
            StringLiteral e => EmitString(e),
             FStringExpr e => _fstringEmitters.EmitFString(e),
            BoolLiteral e => EmitBool(e),
            NoneLiteral e => EmitNone(e),
            EllipsisLiteral e => EmitEllipsis(e),
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
            ListExpr e => EmitList(e),
            TupleExpr e => EmitTuple(e),
            SetExpr e => EmitSet(e),
            DictExpr e => EmitDict(e),
            ListCompExpr e => _comprehensionEmitters.EmitListComp(e),
            SetCompExpr e => _comprehensionEmitters.EmitSetComp(e),
            DictCompExpr e => _comprehensionEmitters.EmitDictComp(e),
            GeneratorExpr e => _comprehensionEmitters.EmitGenerator(e),
            StarredExpr e => EmitStarred(e),
            AwaitExpr e => throw new CodeGenException("async/await not yet supported", e.Line, e.Column),
            YieldExpr e => _generatorEmitters.EmitYield(e),
            _ => throw new CodeGenException(
                                     $"Cannot emit expression: {expr.GetType().Name}",
                                     expr.Line, expr.Column)
        };

        return type;
    }

    // ── Literals ──────────────────────────────────────────────────────────────

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

    // ── Collections ───────────────────────────────────────────────────────────

    private NajaType EmitList(ListExpr e)
    {
        var listType = typeof(System.Collections.Generic.List<object>);
        var ctor = listType.GetConstructor(Type.EmptyTypes)!;
        var addMethod = listType.GetMethod("Add")!;

        IL.Emit(OpCodes.Newobj, ctor);

        foreach (var elem in e.Elements)
        {
            IL.Emit(OpCodes.Dup);
            var elemType = Emit(elem);
            TypeMapper.EmitBox(IL, elemType);
            IL.Emit(OpCodes.Callvirt, addMethod);
        }

        return new ListType(NajaTypes.Unknown);
    }

    private NajaType EmitTuple(TupleExpr e)
    {
        // Tuples as object[]
        IL.Emit(OpCodes.Ldc_I4, e.Elements.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < e.Elements.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            var elemType = Emit(e.Elements[i]);
            TypeMapper.EmitBox(IL, elemType);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        return new TupleType([]);
    }

    private NajaType EmitDict(DictExpr e)
    {
        var dictType = typeof(System.Collections.Generic.Dictionary<object, object>);
        var ctor = dictType.GetConstructor(Type.EmptyTypes)!;
        var setItem = dictType.GetMethod("set_Item")!;

        IL.Emit(OpCodes.Newobj, ctor);

        foreach (var (key, val) in e.Pairs)
        {
            if (key is null) continue;  // **unpack not supported yet
            IL.Emit(OpCodes.Dup);
            var kt = Emit(key); TypeMapper.EmitBox(IL, kt);
            var vt = Emit(val); TypeMapper.EmitBox(IL, vt);
            IL.Emit(OpCodes.Callvirt, setItem);
        }

        return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
    }

    // ── Numeric helpers ───────────────────────────────────────────────────────

    private void ConvertToDouble(NajaType t)
    {
        if (t is IntType) IL.Emit(OpCodes.Conv_R8);
    }

    // ── Missing literals ──────────────────────────────────────────────────────

    private NajaType EmitEllipsis(EllipsisLiteral e)
    {
        IL.Emit(OpCodes.Ldnull);   // treat ... as None for now
        return NajaTypes.None;
    }

    // ── Walrus  (x := expr) ───────────────────────────────────────────────────

    private NajaType EmitWalrus(WalrusExpr e)
    {
        var type = Emit(e.Value);
        // Declare local and store a copy, then leave value on stack
        var clrType = TypeMapper.ToClrType(type);
        if (clrType == typeof(void)) clrType = typeof(object);
        if (!_ctx.Locals.Contains(e.Target))
            _ctx.Locals.Declare(e.Target, clrType);
        IL.Emit(OpCodes.Dup);
        _ctx.Locals.EmitStore(e.Target);
        return type;
    }

    // ── Yield  (Basic generator support) ──────────────────────────────────────

    private NajaType EmitYield(YieldExpr e)
    {
        if (!_ctx.IsInsideFunction)
            throw new CodeGenException("'yield' outside function", e.Line, e.Column);

        if (e.IsFrom)
            return EmitYieldFrom(e);

        // Add yielded value to the generator list that was initialized at function entry
        if (_ctx.GeneratorListLocal != null)
        {
            IL.Emit(OpCodes.Ldloc, _ctx.GeneratorListLocal);
            var valueType = Emit(e.Value);
            TypeMapper.EmitBox(IL, valueType);
            var addMethod = typeof(System.Collections.Generic.List<object>).GetMethod("Add")!;
            IL.Emit(OpCodes.Callvirt, addMethod);
        }
        else
        {
            // Fallback: generator list not initialized, just discard the value
            var valueType = Emit(e.Value);
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

        // Iterate the sub-iterable and add every value to the generator list
        var iterType = Emit(e.Value);
        TypeMapper.EmitBox(IL, iterType);
        IL.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        var getEnum = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        IL.Emit(OpCodes.Callvirt, getEnum);

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

    // ── Set literal  {1, 2, 3} ───────────────────────────────────────────────

    private NajaType EmitSet(SetExpr e)
    {
        var setType = typeof(System.Collections.Generic.HashSet<object>);
        var ctor = setType.GetConstructor(Type.EmptyTypes)!;
        var add = setType.GetMethod("Add")!;

        IL.Emit(OpCodes.Newobj, ctor);
        foreach (var elem in e.Elements)
        {
            IL.Emit(OpCodes.Dup);
            var t = Emit(elem);
            TypeMapper.EmitBox(IL, t);
            IL.Emit(OpCodes.Callvirt, add);
            IL.Emit(OpCodes.Pop);   // Add returns bool
        }
        return new SetType(NajaTypes.Unknown);
    }

    // ── Starred  *iterable ───────────────────────────────────────────────────

    private NajaType EmitStarred(StarredExpr e)
    {
        // In call context this is handled by EmitBuiltinCall
        // Here just emit the inner value
        return Emit(e.Value);
    }

    // ── Comprehensions ────────────────────────────────────────────────────────
    // All comprehensions compile to an inline loop that builds the collection.
    // Python 3 scopes comprehensions — we use a fresh local name prefix.

    private NajaType EmitListComp(ListCompExpr e)
    {
        return EmitComprehensionHelper(e.Line, e.Column, e.Generators, e.Element, isSet: false, isDict: false, null, null);
    }

    private NajaType EmitSetComp(SetCompExpr e)
    {
        return EmitComprehensionHelper(e.Line, e.Column, e.Generators, e.Element, isSet: true, isDict: false, null, null);
    }

    private NajaType EmitDictComp(DictCompExpr e)
    {
        return EmitComprehensionHelper(e.Line, e.Column, e.Generators, null, isSet: false, isDict: true, e.Key, e.Value);
    }

    private NajaType EmitGenerator(GeneratorExpr e)
    {
        // Compile generators eagerly as List<object> for now
        return EmitComprehensionHelper(e.Line, e.Column, e.Generators, e.Element, isSet: false, isDict: false, null, null);
    }

    /// <summary>
    /// Emits a comprehension (list/set/dict/generator) by creating a private static helper
    /// method on the same type.  The helper method receives the outer iterable(s) as
    /// parameters so that comprehension-scoped loop variables never leak into the
    /// calling method's locals — exactly matching Python 3's comprehension scoping rules.
    /// </summary>
    private NajaType EmitComprehensionHelper(
        int line, int col,
        IReadOnlyList<Comprehension> generators,
        Expression? element,
        bool isSet, bool isDict,
        Expression? dictKey, Expression? dictValue)
    {
        // Determine return type
        Type returnClrType = isSet
            ? typeof(System.Collections.Generic.HashSet<object>)
            : isDict
                ? typeof(System.Collections.Generic.Dictionary<object, object>)
                : typeof(System.Collections.Generic.List<object>);

        // The outermost generator's iterable is evaluated in the CALLER's scope.
        // We pass it as a parameter to the helper method.
        // All other iterables and the element expression are evaluated inside the helper.
        // We also pass captured outer variables as additional object parameters.

        // --- Emit outer iterable in the CALLING context ---
        var outerIterType = Emit(generators[0].Iter);
        TypeMapper.EmitBox(IL, outerIterType);

        // --- Define helper method ---
        var helperName = $"<comp>_{line}_{col}";
        var helperPts = new[] { typeof(object) };  // single param: the outer iterable
        var helperMb = _ctx.TypeBuilder.DefineMethod(
            helperName,
            MethodAttributes.Private | MethodAttributes.Static,
            returnClrType,
            helperPts);
        helperMb.DefineParameter(1, ParameterAttributes.None, "__iter0");

        // --- Build EmitContext for helper (NO outer locals — scope isolation) ---
        var hIL = helperMb.GetILGenerator();
        var hCtx = new EmitContext(hIL, _ctx.Model, _ctx.TypeBuilder, _ctx.Module,
                                   returnClrType, new[] { "__iter0" });
        hCtx.IsInsideFunction = true;
        // Copy module-level fields, methods, class info — but NOT locals
        foreach (var (k, v) in _ctx.Fields) hCtx.Fields[k] = v;
        // Set the comprehension scope ID for hoisted loop variables
        var scopeId = $"comp_{line}_{col}";
        hCtx.ComprehensionScopeId = scopeId;

        foreach (var (k, v) in _ctx.Methods) hCtx.Methods[k] = v;
        foreach (var (k, v) in _ctx.MethodParamTypes) hCtx.MethodParamTypes[k] = v;
        foreach (var (k, v) in _ctx.ClassTypes) hCtx.ClassTypes[k] = v;
        foreach (var (k, v) in _ctx.ClassConstructors) hCtx.ClassConstructors[k] = v;
        foreach (var (k, v) in _ctx.InstanceFields) hCtx.InstanceFields[k] = v;
        foreach (var (k, v) in _ctx.AllClassMethods) hCtx.AllClassMethods[k] = v;
        foreach (var (k, v) in _ctx.AllClassMethodParamTypes) hCtx.AllClassMethodParamTypes[k] = v;
        foreach (var mn in _ctx.ClassMethods) hCtx.ClassMethods.Add(mn);
        foreach (var (k, v) in _ctx.ImportMap) hCtx.ImportMap[k] = v;
        foreach (var (k, v) in _ctx.NamespaceImports) hCtx.NamespaceImports[k] = v;
        hCtx.SelfName = _ctx.SelfName;
        hCtx.IsInstanceMethod = false;  // helper is static, no 'self'

        // --- Build the collection inside the helper ---
        var hExpr = new ExpressionEmitter(hCtx);
        var resultLocal = hCtx.Locals.Declare("__result", returnClrType);

        if (isSet)
        {
            hIL.Emit(OpCodes.Newobj, returnClrType.GetConstructor(Type.EmptyTypes)!);
        }
        else if (isDict)
        {
            hIL.Emit(OpCodes.Newobj, returnClrType.GetConstructor(Type.EmptyTypes)!);
        }
        else
        {
            hIL.Emit(OpCodes.Newobj, returnClrType.GetConstructor(Type.EmptyTypes)!);
        }
        hIL.Emit(OpCodes.Stloc, resultLocal);

        // Rewrite the first generator to use the __iter0 parameter
        var rewrittenGenerators = new List<Comprehension>(generators);

        hExpr.EmitComprehensionLoopsHelper(
            rewrittenGenerators, 0, isFirstFromParam: true,
            body: () =>
            {
                hIL.Emit(OpCodes.Ldloc, resultLocal);
                if (isDict)
                {
                    var kt = hExpr.Emit(dictKey!); TypeMapper.EmitBox(hIL, kt);
                    var vt = hExpr.Emit(dictValue!); TypeMapper.EmitBox(hIL, vt);
                    hIL.Emit(OpCodes.Callvirt, returnClrType.GetMethod("set_Item")!);
                }
                else if (isSet)
                {
                    var t = hExpr.Emit(element!);
                    TypeMapper.EmitBox(hIL, t);
                    hIL.Emit(OpCodes.Callvirt, returnClrType.GetMethod("Add")!);
                    hIL.Emit(OpCodes.Pop); // HashSet.Add returns bool
                }
                else
                {
                    var t = hExpr.Emit(element!);
                    TypeMapper.EmitBox(hIL, t);
                    hIL.Emit(OpCodes.Callvirt, returnClrType.GetMethod("Add")!);
                }
            });

        hIL.Emit(OpCodes.Ldloc, resultLocal);
        hIL.Emit(OpCodes.Ret);

        // --- Call helper from the original context (outer iterable already on stack) ---
        IL.Emit(OpCodes.Call, helperMb);

        return isSet ? (NajaType)new SetType(NajaTypes.Unknown)
             : isDict ? new DictType(NajaTypes.Unknown, NajaTypes.Unknown)
             : new ListType(NajaTypes.Unknown);
    }

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
            IL.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        }
        else
        {
            var iterType = Emit(gen.Iter);
            TypeMapper.EmitBox(IL, iterType);
            IL.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        }

        var getEnum = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        IL.Emit(OpCodes.Callvirt, getEnum);

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
            Emit(cond);
            var condTrueLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue_S, condTrueLabel);
            IL.Emit(OpCodes.Br, loopStart);
            IL.MarkLabel(condTrueLabel);
        }

        EmitComprehensionLoopsHelper(generators, depth + 1, false, body);

        IL.Emit(OpCodes.Br, loopStart);
        IL.MarkLabel(loopEnd);
    }

    /// <summary>
    /// Recursively emit nested for-loops for each comprehension generator.
    /// Each generator may have filter conditions (if clauses).
    /// </summary>
    private void EmitComprehensionLoops(
        IReadOnlyList<Comprehension> generators,
        int depth,
        Action emitBody)
    {
        if (depth >= generators.Count)
        {
            emitBody();
            return;
        }

        var gen = generators[depth];
        var loopStart = IL.DefineLabel();
        var loopEnd = IL.DefineLabel();

        // Get enumerator
        var iterType = Emit(gen.Iter);
        TypeMapper.EmitBox(IL, iterType);
        IL.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        var getEnum = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        IL.Emit(OpCodes.Callvirt, getEnum);

        var enumLocal = _ctx.Locals.Declare($"__cenum_{depth}_{generators[depth].Iter.Line}",
            typeof(System.Collections.IEnumerator));
        IL.Emit(OpCodes.Stloc, enumLocal);

        IL.MarkLabel(loopStart);
        IL.Emit(OpCodes.Ldloc, enumLocal);
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        IL.Emit(OpCodes.Callvirt, moveNext);
        IL.Emit(OpCodes.Brfalse, loopEnd);

        // Assign loop variable
        IL.Emit(OpCodes.Ldloc, enumLocal);
        var current = typeof(System.Collections.IEnumerator)
            .GetProperty("Current")!.GetGetMethod()!;
        IL.Emit(OpCodes.Callvirt, current);

        // Store — target might be Name or Tuple
        StoreComprehensionTarget(gen.Target);

        // Emit filter conditions (if clauses)
        foreach (var cond in gen.Conditions)
        {
            Emit(cond);
            // If condition is Unknown (object), unbox to bool
            var condTrueLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Brtrue_S, condTrueLabel);
            IL.Emit(OpCodes.Br, loopStart);
            IL.MarkLabel(condTrueLabel);
        }

        // Recurse for next generator or emit body
        EmitComprehensionLoops(generators, depth + 1, emitBody);

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
                var unpackHelper = typeof(NajaBuiltins)
                    .GetMethod(nameof(NajaBuiltins.GetItem))!;
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Box, typeof(int));
                IL.Emit(OpCodes.Call, unpackHelper);
                StoreComprehensionTarget(t.Elements[i]);
            }
        }
        else
        {
            IL.Emit(OpCodes.Pop);
        }
    }

    // ── Default value emitter ─────────────────────────────────────────────────

    private void EmitDefaultValue(object? value)
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

    // ── Helpers for dynamic() and cast() ─────────────────────────────────────

    private static string DescribeExpr(Expression expr) => expr switch
    {
        NameExpr e => e.Name,
        CallExpr e => $"{DescribeExpr(e.Func)}(...)",
        AttributeExpr e => $"{DescribeExpr(e.Object)}.{e.Attribute}",
        StringLiteral e => $"'{e.Value}'",
        IntLiteral e => e.Value.ToString(),
        _ => expr.GetType().Name
    };

    private static NajaType ParseCastTarget(Expression typeArg, int line, int col)
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

    private static void EmitCoercion(
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
                    // call NajaBuiltins.ToInt(string) → long
                    il.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToInt))!);
                }
                break;

            case FloatType:
                if (from is IntType or BoolType) il.Emit(OpCodes.Conv_R8);
                else if (from is UnknownType) il.Emit(OpCodes.Unbox_Any, typeof(double));
                else if (from is StrType)
                {
                    il.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!);
                }
                break;

            case BoolType:
                if (from is UnknownType)
                {
                    il.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToBool))!);
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
                    il.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToStr))!);
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
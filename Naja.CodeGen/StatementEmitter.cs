using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

// Disambiguate between System.Reflection.Emit.Label and System.Windows.Forms.Label
using ILLabel = System.Reflection.Emit.Label;

namespace Naja.CodeGen;

/// <summary>
/// Emits IL opcodes for statement nodes.
/// Statements do NOT leave values on the stack (stack is balanced after each).
/// </summary>
public sealed class StatementEmitter
{
    private readonly EmitContext _ctx;
    private readonly ExpressionEmitter _expr;
    private ILGenerator IL => _ctx.IL;

    public StatementEmitter(EmitContext ctx)
    {
        _ctx = ctx;
        _expr = new ExpressionEmitter(ctx);
    }

    // ── Main dispatch ─────────────────────────────────────────────────────────

    public void Emit(Statement stmt)
    {
        switch (stmt)
        {
            case AssignStatement s: Emit(s); break;
            case AnnAssignStatement s: Emit(s); break;
            case AugAssignStatement s: Emit(s); break;
            case ExprStatement s: Emit(s); break;
            case ReturnStatement s: Emit(s); break;
            case IfStatement s: Emit(s); break;
            case WhileStatement s: Emit(s); break;
            case ForStatement s: Emit(s); break;
            case TryStatement s: Emit(s); break;
            case WithStatement s: Emit(s); break;
            case FunctionDef s: Emit(s); break;
            case ClassDef s: Emit(s); break;
            case PassStatement _: break;
            case BreakStatement s: Emit(s); break;
            case ContinueStatement s: Emit(s); break;
            case RaiseStatement s: Emit(s); break;
            case AssertStatement s: Emit(s); break;
            case DeleteStatement _: break;
            case ImportStatement _: break;
            case FromImportStatement _: break;
            case GlobalStatement _: break;
            case NonlocalStatement _: break;
            case MatchStatement s: Emit(s); break;
            case TypeAliasStatement _: break;

            default:
                throw new CodeGenException(
                    $"Cannot emit statement: {stmt.GetType().Name}",
                    stmt.Line, stmt.Column);
        }
    }

    public void EmitAll(IReadOnlyList<Statement> stmts)
    {
        foreach (var s in stmts) Emit(s);
    }

    // ── Assignment ────────────────────────────────────────────────────────────

    private void Emit(AssignStatement s)
    {
        var valType = _expr.Emit(s.Value);

        foreach (var target in s.Targets)
        {
            if (s.Targets.Count > 1)
                IL.Emit(OpCodes.Dup);   // duplicate value for multiple targets

            EmitStore(target, valType);
        }
    }

    private void Emit(AnnAssignStatement s)
    {
        if (s.Value is null) return;
        var valType = _expr.Emit(s.Value);
        EmitStore(s.Target, valType);
    }

    private void Emit(AugAssignStatement s)
    {
        // Special-case: .NET event subscription/unsubscription
        //   exit_item.Click += self.handle_exit
        // Emit: NajaBuiltins.AddEventHandler(exit_item, "Click", self, "handle_exit")
        if ((s.Op == BinaryOp.Add || s.Op == BinaryOp.Sub) && s.Target is AttributeExpr evAttr)
        {
            // Only handle simple method references on RHS (self.method or function name)
            string? handlerMethod = null;
            Expression? handlerTarget = null;

            if (s.Value is AttributeExpr { Object: var ht, Attribute: var hm })
            {
                handlerTarget = ht;
                handlerMethod = hm;
            }
            else if (s.Value is NameExpr { Name: var n })
            {
                handlerMethod = n;
                handlerTarget = null;
            }

            if (handlerMethod is not null)
            {
                var objType = _expr.Emit(evAttr.Object);
                TypeMapper.EmitBox(IL, objType);
                IL.Emit(OpCodes.Ldstr, evAttr.Attribute);
                if (handlerTarget is not null)
                {
                    var htType = _expr.Emit(handlerTarget);
                    TypeMapper.EmitBox(IL, htType);
                }
                else
                {
                    // Module-level function: emit the module Type token instead of null
                    // This allows NajaBuiltins to look up the static method on the module type
                    IL.Emit(OpCodes.Ldtoken, _ctx.TypeBuilder);
                    IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                }
                IL.Emit(OpCodes.Ldstr, handlerMethod);
                
                var methodName = s.Op == BinaryOp.Add ? nameof(NajaBuiltins.AddEventHandler) : nameof(NajaBuiltins.RemoveEventHandler);
                var evMethod = typeof(NajaBuiltins).GetMethod(methodName)!;
                IL.Emit(OpCodes.Call, evMethod);
                return;
            }
        }

        // x += 1  →  x = x + 1
        var targetType = _expr.Emit(s.Target);   // load current value (one value on stack)
        var valueType = _expr.Emit(s.Value);

        NajaType resultType;

        // For Unknown/object operands we must avoid numeric IL opcodes directly,
        // otherwise we emit invalid IL (and can crash the test host).
        bool isDyn = targetType is UnknownType || valueType is UnknownType;

        switch (s.Op)
        {
            case BinaryOp.Add:
                if (targetType is StrType && valueType is StrType)
                {
                    IL.Emit(OpCodes.Call, typeof(string).GetMethod("Concat",
                        new[] { typeof(string), typeof(string) })!);
                    resultType = NajaTypes.Str;
                }
                else if (isDyn)
                {
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);
                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicAdd))!);
                    resultType = NajaTypes.Unknown;
                }
                else
                {
                    IL.Emit(OpCodes.Add);
                    resultType = (targetType is FloatType || valueType is FloatType) ? NajaTypes.Float : NajaTypes.Int;
                }
                break;

            case BinaryOp.Sub:
                if (isDyn)
                {
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);
                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicSub))!);
                    resultType = NajaTypes.Unknown;
                }
                else
                {
                    IL.Emit(OpCodes.Sub);
                    resultType = (targetType is FloatType || valueType is FloatType) ? NajaTypes.Float : NajaTypes.Int;
                }
                break;

            case BinaryOp.Mul:
                if (isDyn)
                {
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);
                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicMul))!);
                    resultType = NajaTypes.Unknown;
                }
                else
                {
                    IL.Emit(OpCodes.Mul);
                    resultType = (targetType is FloatType || valueType is FloatType) ? NajaTypes.Float : NajaTypes.Int;
                }
                break;

            case BinaryOp.Mod:
                if (isDyn)
                {
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);
                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicMod))!);
                    resultType = NajaTypes.Unknown;
                }
                else if (targetType is FloatType || valueType is FloatType)
                {
                    // float %= value — Python sign semantics
                    var tmpFmod = _ctx.Locals.Declare($"__fmod_{s.Line}", typeof(double));
                    if (valueType is IntType) IL.Emit(OpCodes.Conv_R8);
                    IL.Emit(OpCodes.Stloc, tmpFmod);
                    if (targetType is IntType) IL.Emit(OpCodes.Conv_R8);
                    IL.Emit(OpCodes.Ldloc, tmpFmod);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.PyModF))!);
                    resultType = NajaTypes.Float;
                }
                else
                {
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.PyMod))!);
                    resultType = NajaTypes.Int;
                }
                break;

            case BinaryOp.Div:
                // Python / always yields float
                if (isDyn)
                {
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);

                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!); // double

                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!); // double
                    IL.Emit(OpCodes.Div);
                }
                else
                {
                    if (targetType is IntType) IL.Emit(OpCodes.Conv_R8);
                    if (valueType is IntType) IL.Emit(OpCodes.Conv_R8);
                    IL.Emit(OpCodes.Div);
                }
                resultType = NajaTypes.Float;
                break;

            case BinaryOp.FloorDiv:
                if (targetType is IntType && valueType is IntType)
                {
                    // int //= int — Python floors toward -∞
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.PyFloorDiv))!);
                    resultType = NajaTypes.Int;
                }
                else
                {
                    if (isDyn)
                    {
                        TypeMapper.EmitBox(IL, valueType);
                        var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                        IL.Emit(OpCodes.Stloc, tmpR);
                        TypeMapper.EmitBox(IL, targetType);
                        IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!);
                        IL.Emit(OpCodes.Ldloc, tmpR);
                        IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!);
                    }
                    else
                    {
                        // float //= value — stash right, convert left, reload right
                        var tmpFdr = _ctx.Locals.Declare($"__fdr_{s.Line}", typeof(double));
                        if (valueType is IntType) IL.Emit(OpCodes.Conv_R8);
                        IL.Emit(OpCodes.Stloc, tmpFdr);
                        if (targetType is IntType) IL.Emit(OpCodes.Conv_R8);
                        IL.Emit(OpCodes.Ldloc, tmpFdr);
                    }
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.PyFloorDivF))!);
                    resultType = NajaTypes.Float;
                }
                break;

            case BinaryOp.BitAnd:
            case BinaryOp.BitOr:
            case BinaryOp.BitXor:
            case BinaryOp.LShift:
            case BinaryOp.RShift:
                if (isDyn)
                {
                    // Coerce both to int64 for bitwise ops
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);

                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToInt))!); // long

                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToInt))!); // long
                }

                switch (s.Op)
                {
                    case BinaryOp.BitAnd: IL.Emit(OpCodes.And); break;
                    case BinaryOp.BitOr: IL.Emit(OpCodes.Or); break;
                    case BinaryOp.BitXor: IL.Emit(OpCodes.Xor); break;
                    case BinaryOp.LShift: IL.Emit(OpCodes.Shl); break;
                    case BinaryOp.RShift: IL.Emit(OpCodes.Shr); break;
                }
                resultType = NajaTypes.Int;
                break;

            case BinaryOp.Pow:
                if (isDyn)
                {
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);

                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!);

                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!);
                }
                else
                {
                    // Stack is: [left, right]. Convert right first (top-of-stack),
                    // then stash it so we can convert left without disturbing order.
                    if (valueType is IntType) IL.Emit(OpCodes.Conv_R8);
                    var tmpR = _ctx.Locals.Declare($"__powr_{s.Line}", typeof(double));
                    IL.Emit(OpCodes.Stloc, tmpR); // pops right
                    if (targetType is IntType) IL.Emit(OpCodes.Conv_R8);
                    IL.Emit(OpCodes.Ldloc, tmpR);
                }
                IL.Emit(OpCodes.Call, typeof(Math).GetMethod("Pow", new[] { typeof(double), typeof(double) })!);
                resultType = NajaTypes.Float;
                break;

            default:
                // Fallback: treat as dynamic assignment to keep IL valid.
                TypeMapper.EmitBox(IL, valueType);
                var tmp = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                TypeMapper.EmitBox(IL, targetType);
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldloc, tmp);
                resultType = NajaTypes.Unknown;
                break;
        }

        EmitStore(s.Target, resultType);
    }

    private void EmitStore(Expression target, NajaType valueType)
    {
        switch (target)
        {
            case NameExpr n:
                // If this name is a module-level static field, store there (not a local)
                if (_ctx.Fields.TryGetValue(n.Name, out var staticField))
                {
                    // Ensure the stack type matches the field type.
                    //
                    // - If the field is `object`, box value types.
                    // - If the field is a value type but the value is `Unknown` (object),
                    //   unbox it back to the concrete field type.
                    if (staticField.FieldType == typeof(object))
                    {
                        TypeMapper.EmitBox(IL, valueType);
                    }
                    else if (staticField.FieldType.IsValueType && valueType is UnknownType)
                    {
                        IL.Emit(OpCodes.Unbox_Any, staticField.FieldType);
                    }
                    IL.Emit(OpCodes.Stsfld, staticField);
                    break;
                }
                var clrType = TypeMapper.ToClrType(valueType);
                if (clrType == typeof(void)) clrType = typeof(object);
                if (!_ctx.Locals.Contains(n.Name))
                    _ctx.Locals.Declare(n.Name, clrType);
                _ctx.Locals.EmitStore(n.Name);
                break;

            case AttributeExpr a:
                var valTmp = _ctx.Locals.Declare($"__sv_{a.Line}", typeof(object));
                TypeMapper.EmitBox(IL, valueType);
                IL.Emit(OpCodes.Stloc, valTmp);

                _expr.Emit(a.Object);   // push object ref

                // Safely check if the base class has this property to prevent shadowing
                bool isNativeProperty = false;
                if (a.Object is NameExpr { Name: var sname } && sname == (_ctx.SelfName ?? "self"))
                {
                    Type? currentBase = _ctx.TypeBuilder.BaseType;
                    while (currentBase != null && currentBase != typeof(object))
                    {
                        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                        if (currentBase.GetProperty(a.Attribute, flags) != null ||
                            currentBase.GetEvent(a.Attribute, flags) != null)
                        {
                            isNativeProperty = true;
                            break;
                        }
                        currentBase = currentBase.BaseType;
                    }
                }

                // Only use 'stfld' if it's NOT a native WinForms property
                if (!isNativeProperty &&
                    a.Object is NameExpr { Name: var sn } &&
                    sn == (_ctx.SelfName ?? "self") &&
                    _ctx.InstanceFields.TryGetValue(a.Attribute, out var iField))
                {
                    IL.Emit(OpCodes.Ldloc, valTmp);
                    IL.Emit(OpCodes.Stfld, iField);
                }
                else
                {
                    // Dynamic fallback via reflection (routes to your fixed SetAttr)
                    TypeMapper.EmitBox(IL, NajaTypes.Unknown);
                    IL.Emit(OpCodes.Ldstr, a.Attribute);
                    IL.Emit(OpCodes.Ldloc, valTmp);
                    var setAttr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.SetAttr))!;
                    IL.Emit(OpCodes.Call, setAttr);
                }
                break;

            case SubscriptExpr sub:
                // a[key] = value — value already on stack, save it
                var subVal = _ctx.Locals.Declare($"__si_{sub.Line}", typeof(object));
                TypeMapper.EmitBox(IL, valueType);
                IL.Emit(OpCodes.Stloc, subVal);

                var objType = _expr.Emit(sub.Object);
                TypeMapper.EmitBox(IL, objType);
                var keyType = _expr.Emit(sub.Index);
                TypeMapper.EmitBox(IL, keyType);
                IL.Emit(OpCodes.Ldloc, subVal);

                var setItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.SetItem))!;
                IL.Emit(OpCodes.Call, setItem);
                break;

            case TupleExpr te:
                EmitUnpackTarget(te.Elements, valueType);
                break;

            case ListExpr le:
                EmitUnpackTarget(le.Elements, valueType);
                break;

            default:
                IL.Emit(OpCodes.Pop);
                break;
        }
    }

    private void EmitUnpackTarget(IReadOnlyList<Expression> elems, NajaType valueType)
    {
        // Check for starred element
        int starIdx = -1;
        for (int i = 0; i < elems.Count; i++)
            if (elems[i] is StarredExpr) { starIdx = i; break; }

        // Convert to List<object?> via UnpackIterable
        var unpackLocal = _ctx.Locals.Declare($"__up_{IL.GetHashCode()}", typeof(System.Collections.Generic.List<object?>));
        TypeMapper.EmitBox(IL, valueType);
        IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.UnpackIterable))!);
        IL.Emit(OpCodes.Stloc, unpackLocal);

        var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
        var getSlice = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetUnpackSlice))!;

        if (starIdx < 0)
        {
            // Simple unpack: a, b, c = iter
            for (int i = 0; i < elems.Count; i++)
            {
                IL.Emit(OpCodes.Ldloc, unpackLocal);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Box, typeof(int));
                IL.Emit(OpCodes.Call, getItem);
                EmitStore(elems[i], NajaTypes.Unknown);
            }
        }
        else
        {
            // Extended unpack: a, *rest, b = iter
            int prefixCount = starIdx;
            int suffixCount = elems.Count - starIdx - 1;

            // Emit prefix elements
            for (int i = 0; i < prefixCount; i++)
            {
                IL.Emit(OpCodes.Ldloc, unpackLocal);
                IL.Emit(OpCodes.Ldc_I4, i);
                IL.Emit(OpCodes.Box, typeof(int));
                IL.Emit(OpCodes.Call, getItem);
                EmitStore(elems[i], NajaTypes.Unknown);
            }

            // Emit starred slice
            IL.Emit(OpCodes.Ldloc, unpackLocal);
            IL.Emit(OpCodes.Ldc_I4, prefixCount);
            IL.Emit(OpCodes.Ldc_I4, suffixCount);
            IL.Emit(OpCodes.Call, getSlice);
            EmitStore(((StarredExpr)elems[starIdx]).Value, new ListType(NajaTypes.Unknown));

            // Emit suffix elements (index from end)
            for (int i = 0; i < suffixCount; i++)
            {
                // Index = list.Count - suffixCount + i  -- compute at runtime
                var tmpCount = _ctx.Locals.Declare($"__uplen_{IL.GetHashCode()}_{i}", typeof(int));
                var countProp = typeof(System.Collections.Generic.List<object?>).GetProperty("Count")!.GetGetMethod()!;
                IL.Emit(OpCodes.Ldloc, unpackLocal);
                IL.Emit(OpCodes.Callvirt, countProp);
                IL.Emit(OpCodes.Ldc_I4, suffixCount - i);
                IL.Emit(OpCodes.Sub);
                IL.Emit(OpCodes.Stloc, tmpCount);

                IL.Emit(OpCodes.Ldloc, unpackLocal);
                IL.Emit(OpCodes.Ldloc, tmpCount);
                IL.Emit(OpCodes.Box, typeof(int));
                IL.Emit(OpCodes.Call, getItem);
                EmitStore(elems[starIdx + 1 + i], NajaTypes.Unknown);
            }
        }
    }   // end of EmitUnpackTarget

    // ── Expression statement ──────────────────────────────────────────────────

    private void Emit(ExprStatement s)
    {
        _ = _expr.Emit(s.Expr);
        // Every Emit() leaves exactly one value on stack — always pop it
        IL.Emit(OpCodes.Pop);
    }

    // ── Return ────────────────────────────────────────────────────────────────

    private void Emit(ReturnStatement s)
    {
        if (s.Value is not null)
        {
            var type = _expr.Emit(s.Value);
            if (_ctx.ReturnType == typeof(void))
            {
                IL.Emit(OpCodes.Pop);
            }
            else if (_ctx.ReturnType == typeof(object))
            {
                // Box value types so they fit in object return slot
                TypeMapper.EmitBox(IL, type);
            }
            // else types match directly — emit as-is
        }
        else if (_ctx.ReturnType == typeof(object))
        {
            IL.Emit(OpCodes.Ldnull);
        }
        else if (_ctx.ReturnType != typeof(void))
        {
            // Return default for value types — push zero
            IL.Emit(OpCodes.Ldc_I4_0);
            if (_ctx.ReturnType == typeof(long)) IL.Emit(OpCodes.Conv_I8);
            if (_ctx.ReturnType == typeof(double)) IL.Emit(OpCodes.Conv_R8);
        }

        IL.Emit(OpCodes.Ret);
    }

    // ── If ────────────────────────────────────────────────────────────────────

    private void Emit(IfStatement s)
    {
        var endLabel = IL.DefineLabel();
        var elseLabel = IL.DefineLabel();

        _expr.Emit(s.Condition);
        IL.Emit(OpCodes.Brfalse, s.Elifs.Count > 0 || s.Else.Count > 0 ? elseLabel : endLabel);

        EmitAll(s.Then);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(elseLabel);

        foreach (var (elifCond, elifBody) in s.Elifs)
        {
            var nextLabel = IL.DefineLabel();
            _expr.Emit(elifCond);
            IL.Emit(OpCodes.Brfalse, nextLabel);
            EmitAll(elifBody);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(nextLabel);
        }

        EmitAll(s.Else);
        IL.MarkLabel(endLabel);
    }

    // ── While ─────────────────────────────────────────────────────────────────

    private void Emit(WhileStatement s)
    {
        var loopStart = IL.DefineLabel();
        // Python while/else: else runs only if loop exits normally (no break).
        var normalEnd = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        // Push labels for break/continue
        _breakLabels.Push(endLabel);      // break skips else
        _continueLabels.Push(loopStart);

        IL.MarkLabel(loopStart);
        _expr.Emit(s.Condition);
        IL.Emit(OpCodes.Brfalse, normalEnd);

        EmitAll(s.Body);
        IL.Emit(OpCodes.Br, loopStart);

        // Normal termination: run else
        IL.MarkLabel(normalEnd);

        _breakLabels.Pop();
        _continueLabels.Pop();

        EmitAll(s.Else);
        IL.MarkLabel(endLabel);
    }

    // ── For ───────────────────────────────────────────────────────────────────

    private void Emit(ForStatement s)
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
        _continueLabels.Push(loopStart);

        // Get the iterable
        var iterType = _expr.Emit(s.Iter);

        // Call GetEnumerator — works for List<object>, string, array, etc.
        var getEnumerator = typeof(System.Collections.IEnumerable)
            .GetMethod("GetEnumerator")!;

        // Box if needed
        TypeMapper.EmitBox(IL, iterType);
        IL.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        IL.Emit(OpCodes.Callvirt, getEnumerator);

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
                EmitStore(
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
                EmitStore(s.Target, existingLocal.LocalType == typeof(long) ? NajaTypes.Int :
                                    existingLocal.LocalType == typeof(double) ? NajaTypes.Float :
                                    NajaTypes.Unknown);
            }
            else
            {
                // Store as object (allows iterating strings, lists, tuples, etc.)
                EmitStore(s.Target, NajaTypes.Unknown);
            }
            }
        }
        else
        {
            EmitStore(s.Target, NajaTypes.Unknown);
        }

        // Body
        EmitAll(s.Body);
        IL.Emit(OpCodes.Br, loopStart);

        // Normal termination: run else
        IL.MarkLabel(normalEnd);

        _breakLabels.Pop();
        _continueLabels.Pop();

        EmitAll(s.Else);
        IL.MarkLabel(endLabel);
    }

    // ── Break / Continue ──────────────────────────────────────────────────────

    private readonly Stack<ILLabel> _breakLabels = new();
    private readonly Stack<ILLabel> _continueLabels = new();

    private void Emit(BreakStatement s)
    {
        if (_breakLabels.Count == 0)
            throw new CodeGenException("break outside loop", s.Line, s.Column);
        IL.Emit(OpCodes.Br, _breakLabels.Peek());
    }

    private void Emit(ContinueStatement s)
    {
        if (_continueLabels.Count == 0)
            throw new CodeGenException("continue outside loop", s.Line, s.Column);
        IL.Emit(OpCodes.Br, _continueLabels.Peek());
    }

    // ── Raise ─────────────────────────────────────────────────────────────────

    private void Emit(RaiseStatement s)
    {
        if (s.Exception is not null)
        {
            _expr.Emit(s.Exception);
            IL.Emit(OpCodes.Castclass, typeof(Exception));

            if (s.Cause is not null)
            {
                // Must store exception first — cause gets emitted second
                // so arg order on stack matches (Exception, object)
                var exTmp = _ctx.Locals.Declare($"__raise_{s.Line}", typeof(Exception));
                IL.Emit(OpCodes.Stloc, exTmp);          // pop Exception, save it

                IL.Emit(OpCodes.Ldloc, exTmp);          // push Exception  (arg0)
                _expr.Emit(s.Cause);                    // push cause      (arg1)
                TypeMapper.EmitBox(IL, NajaTypes.Unknown);

                var setCause = typeof(NajaBuiltins)
                    .GetMethod(nameof(NajaBuiltins.SetExceptionCause))!;
                IL.Emit(OpCodes.Call, setCause);        // returns Exception, stack: [Exception]
            }

            IL.Emit(OpCodes.Throw);
        }
        else
        {
            IL.Emit(OpCodes.Rethrow);
        }
    }

    // ── Assert ────────────────────────────────────────────────────────────────

    private void Emit(AssertStatement s)
    {
        var passLabel = IL.DefineLabel();
        _expr.Emit(s.Test);
        IL.Emit(OpCodes.Brtrue, passLabel);

        var ctor = typeof(Exception).GetConstructor(new[] { typeof(string) })!;
        if (s.Message is not null)
        {
            _expr.Emit(s.Message);
            var toStr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToStr))!;
            IL.Emit(OpCodes.Call, toStr);
        }
        else
            IL.Emit(OpCodes.Ldstr, "AssertionError");

        IL.Emit(OpCodes.Newobj, ctor);
        IL.Emit(OpCodes.Throw);
        IL.MarkLabel(passLabel);
    }

    // ── Try / except / else / finally ─────────────────────────────────────────

    private void Emit(TryStatement s)
    {
        var hasHandlers = s.Handlers.Count > 0;
        var hasFinally = s.Finally.Count > 0;
        var hasElse = s.Else.Count > 0;

        // One outer exception block covers everything when finally is present
        if (hasFinally)
            IL.BeginExceptionBlock();

        if (hasHandlers)
        {
            // Declare noExFlag BEFORE opening the inner exception block
            LocalBuilder? noExFlag = null;
            if (hasElse)
                noExFlag = _ctx.Locals.Declare($"__noex_{s.Line}", typeof(bool));

            IL.BeginExceptionBlock();
            EmitAll(s.Body);

            if (noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stloc, noExFlag);
            }

            foreach (var handler in s.Handlers)
            {
                // Always open a single catch(Exception) block
                // For tuple types, do runtime type filtering inside
                IL.BeginCatchBlock(typeof(Exception));

                var catchTypes = ResolveCatchTypes(handler);

                // Temp local to hold the caught exception for type filtering
                var exTmp = _ctx.Locals.Declare($"__ex_{s.Line}_{handler.Line}", typeof(Exception));
                IL.Emit(OpCodes.Stloc, exTmp);

                if (catchTypes.Count > 1 || catchTypes[0] != typeof(Exception))
                {
                    // Runtime type check — rethrow if not matched
                    var matchedLabel = IL.DefineLabel();
                    foreach (var ct in catchTypes)
                    {
                        IL.Emit(OpCodes.Ldloc, exTmp);
                        IL.Emit(OpCodes.Isinst, ct);
                        IL.Emit(OpCodes.Brtrue, matchedLabel);
                    }
                    IL.Emit(OpCodes.Ldloc, exTmp);
                    IL.Emit(OpCodes.Throw);
                    IL.MarkLabel(matchedLabel);
                }

                if (handler.Name is not null)
                {
                    if (!_ctx.Locals.Contains(handler.Name))
                        _ctx.Locals.Declare(handler.Name, typeof(Exception));
                    IL.Emit(OpCodes.Ldloc, exTmp);
                    _ctx.Locals.EmitStore(handler.Name);
                }

                if (noExFlag is not null)
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Stloc, noExFlag);
                }

                EmitAll(handler.Body);
            }

            IL.EndExceptionBlock();

            if (hasElse && noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldloc, noExFlag);
                var skipElse = IL.DefineLabel();
                IL.Emit(OpCodes.Brfalse, skipElse);
                EmitAll(s.Else);
                IL.MarkLabel(skipElse);
            }
        }
        else
        {
            // try/finally with no handlers — body is inside the outer block
            EmitAll(s.Body);
        }

        if (hasFinally)
        {
            IL.BeginFinallyBlock();
            EmitAll(s.Finally);
            IL.EndExceptionBlock();
        }
    }
    private List<Type> ResolveCatchTypes(ExceptHandler handler)
    {
        if (handler.ExceptionType is null)
            return [typeof(Exception)];

        if (handler.ExceptionType is TupleExpr texpr)
            return texpr.Elements
                .Select(el => {
                    var name = el is NameExpr n ? n.Name : el.ToString() ?? "";
                    return TypeMapper.ResolveExceptionType(name) ?? typeof(Exception);
                })
                .ToList();

        var exName = handler.ExceptionType is NameExpr ne
            ? ne.Name
            : handler.ExceptionType.ToString() ?? "";
        return [TypeMapper.ResolveExceptionType(exName) ?? typeof(Exception)];
    }

    // ── With / as ─────────────────────────────────────────────────────────────

    private void Emit(WithStatement s)
    {
        if (s.IsAsync)
            throw new CodeGenException("async with is not yet supported in Naja.", s.Line, s.Column);
        // with expr as var: body
        // Compiles to: try { var = expr.__enter__(); body } finally { var.__exit__(...) }
        foreach (var item in s.Items)
        {
            var ctxLocal = _ctx.Locals.Declare($"__with_{s.Line}_{item.GetHashCode()}", typeof(object));

            var ctxType = _expr.Emit(item.Context);
            TypeMapper.EmitBox(IL, ctxType);
            
            IL.Emit(OpCodes.Stloc, ctxLocal);

            IL.BeginExceptionBlock();

            IL.Emit(OpCodes.Ldloc, ctxLocal);
            var enter = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ContextEnter))!;
            IL.Emit(OpCodes.Call, enter);

            if (item.Target is not null)
            {
                if (item.Target is NameExpr n)
                {
                    if (!_ctx.Locals.Contains(n.Name))
                        _ctx.Locals.Declare(n.Name, typeof(object));
                    _ctx.Locals.EmitStore(n.Name);
                }
                else
                {
                    IL.Emit(OpCodes.Pop);
                }
            }
            else
            {
                IL.Emit(OpCodes.Pop);
            }

            EmitAll(s.Body);

            IL.BeginFinallyBlock();

            // Call __exit__(None, None, None) via runtime helper
            if (_ctx.Locals.TryGet($"__with_{s.Line}_{item.GetHashCode()}") is { } withLocal)
                IL.Emit(OpCodes.Ldloc, withLocal);
            else
                IL.Emit(OpCodes.Ldnull);

            var exit = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ContextExit))!;
            IL.Emit(OpCodes.Call, exit);

            IL.EndExceptionBlock();
        }
    }

    // ── Nested function def ───────────────────────────────────────────────────

    private void Emit(FunctionDef s)
    {
        // Nested function — declare as a static method on the host type
        // and store a reference in a local (closures deferred to Phase 7)
        var pts = s.Params.Select(_ => typeof(object)).ToArray();
        var mb = _ctx.TypeBuilder.DefineMethod(
            $"{s.Name}_{s.Line}",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            pts);

        for (int i = 0; i < s.Params.Count; i++)
            mb.DefineParameter(i + 1, ParameterAttributes.None, s.Params[i].Name);

        var paramNames = s.Params.Select(p => p.Name).ToList();
        var fnCtx = new EmitContext(mb.GetILGenerator(), _ctx.Model,
                                    _ctx.TypeBuilder, _ctx.Module,
                                    typeof(object), paramNames);
        fnCtx.IsInsideFunction = true;

        foreach (var (k, v) in _ctx.Fields) fnCtx.Fields[k] = v;
        foreach (var (k, v) in _ctx.Methods) fnCtx.Methods[k] = v;
        foreach (var (k, v) in _ctx.MethodParamTypes) fnCtx.MethodParamTypes[k] = v;

        // Check if this function contains yield statements (is a generator)
        bool isGenerator = ContainsYield(s.Body);
        if (isGenerator)
        {
            // Initialize the generator list at function entry
            var listType = typeof(System.Collections.Generic.List<object>);
            var ctor = listType.GetConstructor(Type.EmptyTypes)!;
            fnCtx.GeneratorListLocal = fnCtx.Locals.Declare($"__generator_{s.Name}_{s.Line}", listType);
            IL.Emit(OpCodes.Newobj, ctor);
            IL.Emit(OpCodes.Stloc, fnCtx.GeneratorListLocal);
        }

        var bodyEmitter = new StatementEmitter(fnCtx);
        bodyEmitter.EmitAll(s.Body);

        bool endsWithReturn = s.Body.Count > 0 && s.Body[^1] is ReturnStatement;
        if (!endsWithReturn)
        {
            if (isGenerator)
            {
                // Return the generator list
                IL.Emit(OpCodes.Ldloc, fnCtx.GeneratorListLocal);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }
            IL.Emit(OpCodes.Ret);
        }
        else if (isGenerator)
        {
            // If the function ends with a return statement, we still need to return the list
            // But the ReturnStatement already emitted Ret, so this won't be reached
            // We need a different approach: modify return statement handling
        }

        // Register in current context so calls within scope find it
        _ctx.Methods[s.Name] = mb;
        _ctx.MethodParamTypes[s.Name] = pts;

        // Push null as the "function object" value — local variable holds method ref
        // Full delegate creation in Phase 7
    }

    /// <summary>Check if a statement list contains any yield expressions.</summary>
    private static bool ContainsYield(IReadOnlyList<Statement> statements)
    {
        foreach (var stmt in statements)
        {
            if (ContainsYieldInStatement(stmt))
                return true;
        }
        return false;
    }

    private static bool ContainsYieldInStatement(Statement stmt)
    {
        return stmt switch
        {
            ExprStatement es => ContainsYieldInExpression(es.Expr),
            ReturnStatement rs => rs.Value is not null && ContainsYieldInExpression(rs.Value),
        IfStatement ifs => ContainsYield(ifs.Then) || 
                          ifs.Elifs.Any(e => ContainsYield(e.Body)) ||
                          ContainsYield(ifs.Else),
            WhileStatement ws => ContainsYield(ws.Body) || ContainsYield(ws.Else),
            ForStatement fs => ContainsYield(fs.Body) || ContainsYield(fs.Else),
            TryStatement ts => ContainsYield(ts.Body) ||
                              ts.Handlers.Any(h => ContainsYield(h.Body)) ||
                              ContainsYield(ts.Else) ||
                              ContainsYield(ts.Finally),
            WithStatement ws => ContainsYield(ws.Body),
            AssignStatement ass => ass.Value is not null && ContainsYieldInExpression(ass.Value),
            AnnAssignStatement aas => aas.Value is not null && ContainsYieldInExpression(aas.Value),
            AugAssignStatement aas => ContainsYieldInExpression(aas.Value),
            _ => false
        };
    }

    private static bool ContainsYieldInExpression(Expression expr)
    {
        return expr switch
        {
            YieldExpr => true,
            BinaryExpr be => ContainsYieldInExpression(be.Left) || ContainsYieldInExpression(be.Right),
            UnaryExpr ue => ContainsYieldInExpression(ue.Operand),
            BoolOpExpr boe => boe.Values.Any(ContainsYieldInExpression),
            CompareExpr ce => ContainsYieldInExpression(ce.Left) || ce.Comparators.Any(c => ContainsYieldInExpression(c.Right)),
            IfExpr ie => ContainsYieldInExpression(ie.Condition) || ContainsYieldInExpression(ie.Then) || ContainsYieldInExpression(ie.Else),
            CallExpr ce => ce.Args.Any(a => ContainsYieldInExpression(a.Value)),
            AttributeExpr ae => ContainsYieldInExpression(ae.Object),
            SubscriptExpr se => ContainsYieldInExpression(se.Object) || ContainsYieldInExpression(se.Index),
            ListExpr le => le.Elements.Any(ContainsYieldInExpression),
            TupleExpr te => te.Elements.Any(ContainsYieldInExpression),
            SetExpr se => se.Elements.Any(ContainsYieldInExpression),
            DictExpr de => de.Pairs.Any(p => (p.Key is null || ContainsYieldInExpression(p.Key)) && ContainsYieldInExpression(p.Value)),
            ListCompExpr lce => ContainsYieldInComprehension(lce.Generators) || ContainsYieldInExpression(lce.Element),
            SetCompExpr sce => ContainsYieldInComprehension(sce.Generators) || ContainsYieldInExpression(sce.Element),
            DictCompExpr dce => ContainsYieldInComprehension(dce.Generators) || ContainsYieldInExpression(dce.Key) || ContainsYieldInExpression(dce.Value),
            GeneratorExpr ge => ContainsYieldInComprehension(ge.Generators) || ContainsYieldInExpression(ge.Element),
            _ => false
        };
    }

    private static bool ContainsYieldInComprehension(IReadOnlyList<Comprehension> generators)
    {
        foreach (var gen in generators)
        {
            if (ContainsYieldInExpression(gen.Iter))
                return true;
            foreach (var cond in gen.Conditions)
            {
                if (ContainsYieldInExpression(cond))
                    return true;
            }
        }
        return false;
    }

    // ── Class def ─────────────────────────────────────────────────────────────

    private void Emit(ClassDef s)
    {
        // Inline class definitions (nested in functions) — no-op for now
        // Top-level classes are handled by AssemblyEmitter
    }

    // ── Match / case ──────────────────────────────────────────────────────────

    private void Emit(MatchStatement s)
    {
        var endLabel = IL.DefineLabel();
        var subjectLocal = _ctx.Locals.Declare($"__match_{s.Line}", typeof(object));

        var subjType = _expr.Emit(s.Subject);
        TypeMapper.EmitBox(IL, subjType);
        IL.Emit(OpCodes.Stloc, subjectLocal);

        foreach (var c in s.Cases)
        {
            var nextCase = IL.DefineLabel();

            // Emit pattern match check
            EmitPatternCheck(c.Pattern, subjectLocal, nextCase);

            // Guard
            if (c.Guard is not null)
            {
                _expr.Emit(c.Guard);
                IL.Emit(OpCodes.Brfalse, nextCase);
            }

            EmitAll(c.Body);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(nextCase);
        }

        IL.MarkLabel(endLabel);
    }

    private void EmitPatternCheck(Pattern pattern, LocalBuilder subject, ILLabel noMatch)
    {
        var objEquals = typeof(object).GetMethod("Equals", new[] { typeof(object), typeof(object) })!;

        switch (pattern)
        {
            case WildcardPattern:
                break;  // always matches

            case CapturePattern cp:
                IL.Emit(OpCodes.Ldloc, subject);
                if (!_ctx.Locals.Contains(cp.Name))
                    _ctx.Locals.Declare(cp.Name, typeof(object));
                _ctx.Locals.EmitStore(cp.Name);
                break;

            case LiteralPattern lp:
                IL.Emit(OpCodes.Ldloc, subject);
                var litType = _expr.Emit(lp.Value);
                TypeMapper.EmitBox(IL, litType);
                IL.Emit(OpCodes.Call, objEquals);
                IL.Emit(OpCodes.Brfalse, noMatch);
                break;

            case OrPattern op:
            {
                var matched = IL.DefineLabel();
                foreach (var p in op.Patterns)
                {
                    var tryNext = IL.DefineLabel();
                    EmitPatternCheck(p, subject, tryNext);
                    IL.Emit(OpCodes.Br, matched);
                    IL.MarkLabel(tryNext);
                }
                IL.Emit(OpCodes.Br, noMatch);
                IL.MarkLabel(matched);
                break;
            }

            case SequencePattern sp:
            {
                // Check that subject is IEnumerable with matching length, then match each element
                // Load subject as List<object?> via UnpackIterable
                var seqLocal = _ctx.Locals.Declare($"__seq_{pattern.Line}_{pattern.Column}", typeof(System.Collections.Generic.List<object?>));
                IL.Emit(OpCodes.Ldloc, subject);
                IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.UnpackIterable))!);
                IL.Emit(OpCodes.Stloc, seqLocal);

                var countProp = typeof(System.Collections.Generic.List<object?>).GetProperty("Count")!.GetGetMethod()!;
                var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
                var getSlice = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetUnpackSlice))!;

                // Find star pattern index if any
                int starIdx = -1;
                for (int i = 0; i < sp.Patterns.Count; i++)
                    if (sp.Patterns[i] is StarPattern) { starIdx = i; break; }

                // Length check
                IL.Emit(OpCodes.Ldloc, seqLocal);
                IL.Emit(OpCodes.Callvirt, countProp);
                if (starIdx < 0)
                {
                    // Exact match required
                    IL.Emit(OpCodes.Ldc_I4, sp.Patterns.Count);
                    IL.Emit(OpCodes.Bne_Un, noMatch);
                }
                else
                {
                    // Minimum length: non-star elements
                    IL.Emit(OpCodes.Ldc_I4, sp.Patterns.Count - 1);
                    IL.Emit(OpCodes.Blt, noMatch);
                }

                int prefix = starIdx < 0 ? sp.Patterns.Count : starIdx;

                // Emit prefix elements
                for (int i = 0; i < prefix; i++)
                {
                    var elemLocal = _ctx.Locals.Declare($"__seqe_{pattern.Line}_{pattern.Column}_{i}", typeof(object));
                    IL.Emit(OpCodes.Ldloc, seqLocal);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    IL.Emit(OpCodes.Box, typeof(int));
                    IL.Emit(OpCodes.Call, getItem);
                    IL.Emit(OpCodes.Stloc, elemLocal);
                    EmitPatternCheck(sp.Patterns[i], elemLocal, noMatch);
                }

                if (starIdx >= 0)
                {
                    var starPat = (StarPattern)sp.Patterns[starIdx];
                    int suffixCount = sp.Patterns.Count - starIdx - 1;

                    // Bind the star name if not discard
                    if (starPat.Name is not null && starPat.Name != "_")
                    {
                        if (!_ctx.Locals.Contains(starPat.Name))
                            _ctx.Locals.Declare(starPat.Name, typeof(object));
                        IL.Emit(OpCodes.Ldloc, seqLocal);
                        IL.Emit(OpCodes.Ldc_I4, starIdx);
                        IL.Emit(OpCodes.Ldc_I4, suffixCount);
                        IL.Emit(OpCodes.Call, getSlice);
                        IL.Emit(OpCodes.Castclass, typeof(object));
                        _ctx.Locals.EmitStore(starPat.Name);
                    }

                    // Emit suffix elements (indexed from end)
                    for (int i = 0; i < suffixCount; i++)
                    {
                        int patIdx = starIdx + 1 + i;
                        var idxLocal = _ctx.Locals.Declare($"__sqit_{pattern.Line}_{pattern.Column}_{i}", typeof(int));
                        var elemLocal = _ctx.Locals.Declare($"__seqs_{pattern.Line}_{pattern.Column}_{i}", typeof(object));

                        // index = seqLocal.Count - suffixCount + i
                        IL.Emit(OpCodes.Ldloc, seqLocal);
                        IL.Emit(OpCodes.Callvirt, countProp);
                        IL.Emit(OpCodes.Ldc_I4, suffixCount - i);
                        IL.Emit(OpCodes.Sub);
                        IL.Emit(OpCodes.Stloc, idxLocal);

                        IL.Emit(OpCodes.Ldloc, seqLocal);
                        IL.Emit(OpCodes.Ldloc, idxLocal);
                        IL.Emit(OpCodes.Box, typeof(int));
                        IL.Emit(OpCodes.Call, getItem);
                        IL.Emit(OpCodes.Stloc, elemLocal);
                        EmitPatternCheck(sp.Patterns[patIdx], elemLocal, noMatch);
                    }
                }
                break;
            }

            case MappingPattern mp:
            {
                // For each key, check that subject contains it and value matches
                var containsKey = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Contains))!;
                var getItem2 = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;

                foreach (var (key, valuePattern) in mp.Pairs)
                {
                    // Check key exists
                    var keyType = _expr.Emit(key);
                    TypeMapper.EmitBox(IL, keyType);
                    var keyLocal = _ctx.Locals.Declare($"__mpk_{pattern.Line}_{pattern.Column}", typeof(object));
                    IL.Emit(OpCodes.Stloc, keyLocal);

                    IL.Emit(OpCodes.Ldloc, keyLocal);
                    IL.Emit(OpCodes.Ldloc, subject);
                    IL.Emit(OpCodes.Call, containsKey);
                    IL.Emit(OpCodes.Brfalse, noMatch);

                    // Get value and match pattern
                    var valLocal = _ctx.Locals.Declare($"__mpv_{pattern.Line}_{pattern.Column}", typeof(object));
                    IL.Emit(OpCodes.Ldloc, subject);
                    IL.Emit(OpCodes.Ldloc, keyLocal);
                    IL.Emit(OpCodes.Call, getItem2);
                    IL.Emit(OpCodes.Stloc, valLocal);
                    EmitPatternCheck(valuePattern, valLocal, noMatch);
                }

                // Bind **rest if present
                if (mp.Rest is not null)
                {
                    // Simply capture whole subject for now (full rest filtering complex)
                    IL.Emit(OpCodes.Ldloc, subject);
                    if (!_ctx.Locals.Contains(mp.Rest))
                        _ctx.Locals.Declare(mp.Rest, typeof(object));
                    _ctx.Locals.EmitStore(mp.Rest);
                }
                break;
            }

            case ClassPattern clp:
            {
                // Check subject is an instance of the class
                var isInstanceMethod = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.IsInstance))!;
                var clsType = _expr.Emit(clp.Cls);
                TypeMapper.EmitBox(IL, clsType);
                var clsLocal = _ctx.Locals.Declare($"__clsp_{pattern.Line}_{pattern.Column}", typeof(object));
                IL.Emit(OpCodes.Stloc, clsLocal);

                IL.Emit(OpCodes.Ldloc, subject);
                IL.Emit(OpCodes.Ldloc, clsLocal);
                IL.Emit(OpCodes.Call, isInstanceMethod);
                IL.Emit(OpCodes.Brfalse, noMatch);

                var getAttr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetAttr))!;

                // Match positional patterns using __match_args__
                if (clp.Positional.Count > 0)
                {
                    // Fetch __match_args__ from the class
                    IL.Emit(OpCodes.Ldloc, clsLocal);
                    IL.Emit(OpCodes.Ldstr, "__match_args__");
                    IL.Emit(OpCodes.Call, getAttr);
                    var matchArgsLocal = _ctx.Locals.Declare($"__ma_{pattern.Line}_{pattern.Column}", typeof(object));
                    IL.Emit(OpCodes.Stloc, matchArgsLocal);

                    // Map each positional pattern to its attribute name
                    for (int i = 0; i < clp.Positional.Count; i++)
                    {
                        var attrNameLocal = _ctx.Locals.Declare($"__man_{pattern.Line}_{pattern.Column}_{i}", typeof(object));
                        IL.Emit(OpCodes.Ldloc, matchArgsLocal);
                        IL.Emit(OpCodes.Ldc_I4, i);
                        IL.Emit(OpCodes.Box, typeof(int));
                        var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
                        IL.Emit(OpCodes.Call, getItem);
                        IL.Emit(OpCodes.Stloc, attrNameLocal);

                        // Load attribute value from subject
                        var valLocal = _ctx.Locals.Declare($"__cpv_{pattern.Line}_{pattern.Column}_{i}", typeof(object));
                        IL.Emit(OpCodes.Ldloc, subject);
                        IL.Emit(OpCodes.Ldloc, attrNameLocal);
                        IL.Emit(OpCodes.Castclass, typeof(string));
                        IL.Emit(OpCodes.Call, getAttr);
                        IL.Emit(OpCodes.Stloc, valLocal);

                        EmitPatternCheck(clp.Positional[i], valLocal, noMatch);
                    }
                }

                // Match keyword patterns against attributes
                foreach (var (name, valPat) in clp.Keyword)
                {
                    var attrLocal = _ctx.Locals.Declare($"__cpa_{pattern.Line}_{pattern.Column}_{name}", typeof(object));
                    IL.Emit(OpCodes.Ldloc, subject);
                    IL.Emit(OpCodes.Ldstr, name);
                    IL.Emit(OpCodes.Call, getAttr);
                    IL.Emit(OpCodes.Stloc, attrLocal);
                    EmitPatternCheck(valPat, attrLocal, noMatch);
                }
                break;
            }

            case AsPattern asp:
            {
                // Run the inner pattern check; if it passes, bind the name
                EmitPatternCheck(asp.Inner, subject, noMatch);
                // Bind the name
                IL.Emit(OpCodes.Ldloc, subject);
                if (!_ctx.Locals.Contains(asp.Name))
                    _ctx.Locals.Declare(asp.Name, typeof(object));
                _ctx.Locals.EmitStore(asp.Name);
                break;
            }

            default:
                break;  // unhandled — treat as wildcard
        }
    }
}

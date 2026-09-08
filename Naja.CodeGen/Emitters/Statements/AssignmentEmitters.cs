using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Statements;

/// <summary>
/// Emits IL for assignment statements (=, :=, +=, -=, etc.).
/// Handles simple and augmented assignments, unpacking, and store operations.
/// </summary>
public class AssignmentEmitters : StatementEmitterBase
{
    public AssignmentEmitters(EmitContext ctx, ExpressionEmitter exprEmitter, Action<Statement> emitStatement)
        : base(ctx, exprEmitter, emitStatement)
    {
    }

    // ── Main emission dispatch ────────────────────────────────────────────────

    public void EmitAssign(AssignStatement s)
    {
        var valType = _exprEmitter.Emit(s.Value);

        foreach (var target in s.Targets)
        {
            if (s.Targets.Count > 1)
                IL.Emit(OpCodes.Dup);   // duplicate value for multiple targets

            EmitStore(target, valType);
        }
    }

    public void EmitAnnAssign(AnnAssignStatement s)
    {
        if (s.Value is null) return;
        var valType = _exprEmitter.Emit(s.Value);
        EmitStore(s.Target, valType);
    }

    public void EmitAugAssign(AugAssignStatement s)
    {
        // Special-case: .NET event subscription/unsubscription
        //   exit_item.Click += self.handle_exit
        //   exit_item.Click += lambda s, e: ...
        // Emit: NajaBuiltins.AddEventHandler(exit_item, "Click", <handler>, "<method>")
        if ((s.Op == BinaryOp.Add || s.Op == BinaryOp.Sub) && s.Target is AttributeExpr evAttr)
        {
            // Try to detect event handler subscription patterns
            string? handlerMethod = null;
            Expression? handlerTarget = null;
            bool isLambda = false;

            if (s.Value is AttributeExpr { Object: var ht, Attribute: var hm })
            {
                handlerTarget = ht;
                handlerMethod = hm;
            }
            else if (s.Value is NameExpr { Name: var n })
            {
                handlerMethod = n;
                // handlerTarget = null means module-level function — emit the function value itself
                // so AddEventHandler can detect NajaFunction and use it directly
            }
            else if (s.Value is LambdaExpr or CallExpr)
            {
                // Only genuine callable expressions (lambda or call result) count as event handlers.
                // Numeric/string literals and other non-callable values fall through to regular +=.
                isLambda = true;
            }

            if (handlerMethod is not null)
            {
                var objType = _exprEmitter.Emit(evAttr.Object);
                TypeMapper.EmitBox(IL, objType);
                IL.Emit(OpCodes.Ldstr, evAttr.Attribute);
                if (handlerTarget is not null)
                {
                    var htType = _exprEmitter.Emit(handlerTarget);
                    TypeMapper.EmitBox(IL, htType);
                }
                else
                {
                    // Module-level function: emit the NajaFunction wrapper object directly
                    // (stored in a static field with the function's name)
                    var valType = _exprEmitter.Emit(s.Value);
                    TypeMapper.EmitBox(IL, valType);
                }
                IL.Emit(OpCodes.Ldstr, handlerMethod);

                var evMethod = s.Op == BinaryOp.Add
                    ? NajaBuiltinsMethodCache.AddEventHandler_Method
                    : NajaBuiltinsMethodCache.RemoveEventHandler_Method;
                IL.Emit(OpCodes.Call, evMethod);
                return;
            }
            else if (isLambda)
            {
                // Emit lambda as NajaFunction, then use it as handler
                var objType = _exprEmitter.Emit(evAttr.Object);
                TypeMapper.EmitBox(IL, objType);
                IL.Emit(OpCodes.Ldstr, evAttr.Attribute);
                
                // Emit the lambda expression (produces a NajaFunction on the stack)
                var lambdaType = _exprEmitter.Emit(s.Value);
                TypeMapper.EmitBox(IL, lambdaType);
                
                IL.Emit(OpCodes.Ldstr, "");  // handlerMethodName (empty for lambda)

                var evMethod = s.Op == BinaryOp.Add
                    ? NajaBuiltinsMethodCache.AddEventHandler_Method
                    : NajaBuiltinsMethodCache.RemoveEventHandler_Method;
                IL.Emit(OpCodes.Call, evMethod);
                return;
            }
        }

        // x += 1  →  x = x + 1
        var targetType = _exprEmitter.Emit(s.Target);   // load current value (one value on stack)
        var valueType = _exprEmitter.Emit(s.Value);

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
                else if (isDyn || targetType is ListType || valueType is ListType ||
                         targetType is TupleType || valueType is TupleType)
                {
                    // Lists: x += [...] is concatenation — raw 'add' on two
                    // references is pointer arithmetic (AccessViolation).
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);
                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicAdd_Method);
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicSub_Method);
                    resultType = NajaTypes.Unknown;
                }
                else
                {
                    IL.Emit(OpCodes.Sub);
                    resultType = (targetType is FloatType || valueType is FloatType) ? NajaTypes.Float : NajaTypes.Int;
                }
                break;

            case BinaryOp.Mul:
                if (isDyn || targetType is ListType || valueType is ListType ||
                    targetType is StrType || valueType is StrType ||
                    targetType is TupleType || valueType is TupleType)
                {
                    // Sequence repetition: x *= 2 ("ab"*2, [1,2]*2) — raw 'mul'
                    // on a reference and an int is pointer arithmetic (AV crash).
                    TypeMapper.EmitBox(IL, valueType);
                    var tmpR = _ctx.Locals.Declare($"__augr_{s.Line}", typeof(object));
                    IL.Emit(OpCodes.Stloc, tmpR);
                    TypeMapper.EmitBox(IL, targetType);
                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicMul_Method);
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicMod_Method);
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyModF_Method);
                    resultType = NajaTypes.Float;
                }
                else
                {
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyMod_Method);
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method); // double

                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method); // double
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyFloorDiv_Method);
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
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
                        IL.Emit(OpCodes.Ldloc, tmpR);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.PyFloorDivF_Method);
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToInt_Method); // long

                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToInt_Method); // long
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);

                    IL.Emit(OpCodes.Ldloc, tmpR);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
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

    // ── Store target ──────────────────────────────────────────────────────────

    private void EmitStore(Expression target, NajaType valueType)
    {
        switch (target)
        {
            case NameExpr n:
                // Priority for ASSIGNMENT target location (LEGB):
                // 0. Cell param (inner function: var stored in object[] passed as param)
                // 0b. Cell local (outer function: var stored in per-call object[] cell)
                // 1. If a hoisted/nonlocal field exists → store there (closure variable)
                // 2. If semantic says it's a local (non-global/non-nonlocal) → store locally
                // 3. Otherwise → create/store to local (Python default)

                if (_ctx.CellParamOf.TryGetValue(n.Name, out var cellPName))
                {
                    // Write through cell param: cell_param[0] = value
                    var cpTmp = _ctx.Locals.Declare($"__cpv_{n.Name}_{n.Line}", typeof(object));
                    TypeMapper.EmitBox(IL, valueType);
                    IL.Emit(OpCodes.Stloc, cpTmp);
                    _ctx.TryEmitLoadParam(cellPName);   // load cell array
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Ldloc, cpTmp);
                    IL.Emit(OpCodes.Stelem_Ref);
                    break;
                }

                if (_ctx.CellLocals.TryGetValue(n.Name, out var cellLoc))
                {
                    // Write through cell local: cell_local[0] = value
                    var clTmp = _ctx.Locals.Declare($"__clv_{n.Name}_{n.Line}", typeof(object));
                    TypeMapper.EmitBox(IL, valueType);
                    IL.Emit(OpCodes.Stloc, clTmp);
                    IL.Emit(OpCodes.Ldloc, cellLoc);    // load cell array
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Ldloc, clTmp);
                    IL.Emit(OpCodes.Stelem_Ref);
                    break;
                }

                if (_ctx.Fields.TryGetValue(n.Name, out var staticField))
                {
                    // Hoisted field exists — store there for closure semantics.
                    // CRITICAL: convert the emitted value to the FIELD's actual CLR
                    // type before stsfld — stsfld does NOT convert; mismatched
                    // numeric types reinterpret raw bits (int64 1 stored into a
                    // double field reads back as 5E-324). Python semantics say
                    // `e = 0.0; e = 3` re-binds e to int 3 — so when the types
                    // disagree, the SAFE store is to box and unbox exactly.
                    var ft = staticField.FieldType;
                    if (ft == typeof(object))
                    {
                        TypeMapper.EmitBox(IL, valueType);
                    }
                    else if (ft == typeof(long))
                    {
                        if (valueType is FloatType) IL.Emit(OpCodes.Conv_I8);
                        else if (valueType is BoolType) IL.Emit(OpCodes.Conv_I8);
                        else if (valueType is UnknownType) IL.Emit(OpCodes.Unbox_Any, typeof(long));
                        else if (valueType is not IntType) IL.Emit(OpCodes.Conv_I8);
                    }
                    else if (ft == typeof(double))
                    {
                        if (valueType is IntType || valueType is BoolType) IL.Emit(OpCodes.Conv_R8);
                        else if (valueType is UnknownType) IL.Emit(OpCodes.Unbox_Any, typeof(double));
                    }
                    else if (ft == typeof(string))
                    {
                        if (valueType is UnknownType) IL.Emit(OpCodes.Castclass, typeof(string));
                    }
                    else if (ft.IsValueType && valueType is UnknownType)
                    {
                        IL.Emit(OpCodes.Unbox_Any, ft);
                    }
                    IL.Emit(OpCodes.Stsfld, staticField);
                    break;
                }

                // Check semantic symbol for this assignment target
                var sym = _ctx.Model.GetSymbol(n);
                bool symIsLocal = false;
                if (sym is not null && !sym.IsGlobal && !sym.IsNonlocal)
                {
                    symIsLocal = (sym.Kind == SymbolKind.Variable || sym.Kind == SymbolKind.Parameter);
                }

                if (symIsLocal)
                {
                    // Semantic says local — store to local
                    var clrType = TypeMapper.ToClrType(valueType);
                    if (clrType == typeof(void)) clrType = typeof(object);
                    if (!_ctx.Locals.Contains(n.Name))
                        _ctx.Locals.Declare(n.Name, clrType);
                    EmitCoerceForTypedLocal(_ctx.Locals.TryGet(n.Name), valueType);
                    _ctx.Locals.EmitStore(n.Name);
                    break;
                }

                // Default: create a new local variable (Python semantics)
                var defaultClr = TypeMapper.ToClrType(valueType);
                if (defaultClr == typeof(void)) defaultClr = typeof(object);
                if (!_ctx.Locals.Contains(n.Name))
                    _ctx.Locals.Declare(n.Name, defaultClr);
                EmitCoerceForTypedLocal(_ctx.Locals.TryGet(n.Name), valueType);
                _ctx.Locals.EmitStore(n.Name);
                break;

            case AttributeExpr a:
                var valTmp = _ctx.Locals.Declare($"__sv_{a.Line}", typeof(object));
                TypeMapper.EmitBox(IL, valueType);
                IL.Emit(OpCodes.Stloc, valTmp);

                _exprEmitter.Emit(a.Object);   // push object ref

                // Safely check if the base class has this property to prevent shadowing
                bool isNativeProperty = false;
                if (a.Object is NameExpr { Name: var sname } && sname == (_ctx.SelfName ?? "self"))
                {
                    Type? currentBase = _ctx.TypeBuilder.BaseType;
                    while (currentBase != null && currentBase != typeof(object))
                    {
                        // TypeBuilder instances cannot have their members queried before
                        // CreateType() — skip Naja user-defined bases (they have no CLR properties).
                        if (currentBase is TypeBuilder)
                            break;
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
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.SetAttr_Method);
                }
                break;

            case SubscriptExpr sub:
                // a[key] = value — value already on stack, save it
                var subVal = _ctx.Locals.Declare($"__si_{sub.Line}", typeof(object));
                TypeMapper.EmitBox(IL, valueType);
                IL.Emit(OpCodes.Stloc, subVal);

                var objType = _exprEmitter.Emit(sub.Object);
                TypeMapper.EmitBox(IL, objType);
                var keyType = _exprEmitter.Emit(sub.Index);
                TypeMapper.EmitBox(IL, keyType);
                IL.Emit(OpCodes.Ldloc, subVal);

                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.SetItem_Method);
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
        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.UnpackIterable_Method);
        IL.Emit(OpCodes.Stloc, unpackLocal);

        var getItem = NajaBuiltinsMethodCache.GetItem_Method;
        var getSlice = NajaBuiltinsMethodCache.GetUnpackSlice_Method;

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
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// When an already-declared typed local (long, double, bool) would receive an
    /// <see cref="UnknownType"/> (object) value — e.g. from a dynamic operator —
    /// emit a runtime conversion so the stack type matches the local slot type.
    /// Called immediately before every <see cref="LocalsManager.EmitStore"/> call
    /// on a NameExpr target.
    /// </summary>
    private void EmitCoerceForTypedLocal(LocalBuilder? local, NajaType valueType)
    {
        if (local is null || valueType is not UnknownType) return;
        if (local.LocalType == typeof(long))
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToInt_Method);
        else if (local.LocalType == typeof(double))
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToFloat_Method);
        else if (local.LocalType == typeof(bool))
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToBool_Method);
    }
}

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

    public ExpressionEmitter(EmitContext ctx)
    {
        _ctx = ctx;
        _operatorEmitters = new OperatorEmitters(ctx, this);
        _callEmitters = new CallEmitters(ctx, this);
        _attributeEmitters = new AttributeEmitters(ctx, this);
        _controlFlowEmitters = new ControlFlowEmitters(ctx, this);
    }

    // ── Main dispatch ─────────────────────────────────────────────────────────

    public NajaType Emit(Expression expr)
    {
        var type = expr switch
        {
            IntLiteral e => EmitInt(e),
            FloatLiteral e => EmitFloat(e),
            StringLiteral e => EmitString(e),
            FStringExpr e => EmitFString(e),
            BoolLiteral e => EmitBool(e),
            NoneLiteral e => EmitNone(e),
            EllipsisLiteral e => EmitEllipsis(e),
            NameExpr e => EmitName(e),
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
            LambdaExpr e => EmitLambda(e),
            ListExpr e => EmitList(e),
            TupleExpr e => EmitTuple(e),
            SetExpr e => EmitSet(e),
            DictExpr e => EmitDict(e),
            ListCompExpr e => EmitListComp(e),
            SetCompExpr e => EmitSetComp(e),
            DictCompExpr e => EmitDictComp(e),
            GeneratorExpr e => EmitGenerator(e),
            StarredExpr e => EmitStarred(e),
            AwaitExpr e => throw new CodeGenException("async/await not yet supported", e.Line, e.Column),
            YieldExpr e => EmitYield(e),
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

    // ── Name ─────────────────────────────────────────────────────────────────

    private NajaType EmitName(NameExpr e)
    {
        // 0. 'self' / 'cls' in instance methods → ldarg.0
        if (_ctx.IsInstanceMethod && e.Name == (_ctx.SelfName ?? "self"))
        {
            IL.Emit(OpCodes.Ldarg_0);
            return NajaTypes.Unknown;
        }

        // 1. Check parameters first (ldarg.1, ldarg.2, ...)
        if (_ctx.TryEmitLoadParam(e.Name))
        {
            var sym = _ctx.Model.GetSymbol(e);
            return sym?.Type ?? NajaTypes.Unknown;
        }

        // 2. PRIORITY: Check static fields BEFORE locals (module-level variables and hoisted closure variables)
        // MUST be checked BEFORE locals so that nonlocal/hoisted variables take precedence.
        // This ensures that when a variable is promoted to a static field for closure semantics,
        // we load from that field, not from any accidental local copy.

        // 2a. Check for scoped hoisted comprehension loop variables (e.g., __hoisted_i_comp_1_9)
        if (!string.IsNullOrEmpty(_ctx.ComprehensionScopeId))
        {
            var scopedFieldName = $"__hoisted_{e.Name}_{_ctx.ComprehensionScopeId}";
            if (_ctx.Fields.TryGetValue(scopedFieldName, out var scopedField))
            {
                IL.Emit(OpCodes.Ldsfld, scopedField);
                return NajaTypes.Unknown;
            }
        }

        // 2b. Check for any scoped hoisted field with this name (for lambdas inside comprehensions
        // that reference comprehension loop variables). Since the scope ID might not be set in the lambda
        // context, search for any field matching __hoisted_{name}_comp_*
        var scopedMatch = _ctx.Fields.Keys.FirstOrDefault(k =>
            k.StartsWith($"__hoisted_{e.Name}_comp_"));
        if (scopedMatch != null)
        {
            IL.Emit(OpCodes.Ldsfld, _ctx.Fields[scopedMatch]);
            return NajaTypes.Unknown;
        }

        // 2c. Check static fields (module-level variables and hoisted closure variables)
        if (_ctx.Fields.TryGetValue(e.Name, out var field))
        {
            IL.Emit(OpCodes.Ldsfld, field);
            // Return the correct NajaType based on the field's actual CLR type
            if (field.FieldType == typeof(long)) return NajaTypes.Int;
            if (field.FieldType == typeof(double)) return NajaTypes.Float;
            if (field.FieldType == typeof(bool)) return NajaTypes.Bool;
            if (field.FieldType == typeof(string)) return NajaTypes.Str;
            if (field.FieldType == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
            if (field.FieldType == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
            if (field.FieldType == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
            if (field.FieldType == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());
            return NajaTypes.Unknown;
        }

        // 3. Check locals (after static fields, so hoisted variables take precedence)
        var local = _ctx.Locals.TryGet(e.Name);
        if (local is not null)
        {
            IL.Emit(OpCodes.Ldloc, local);
            // If the local's CLR type is `object`, treat as Unknown to avoid
            // incorrect boxing (e.g. for-loop variables stored as object).
            if (local.LocalType == typeof(object))
                return NajaTypes.Unknown;
            return _ctx.Model.GetSymbol(e)?.Type ?? NajaTypes.Unknown;
        }

        // 4. Check Methods (first-class function references -- H5)
        if (_ctx.Methods.TryGetValue(e.Name, out var method))
        {
            var pts = _ctx.MethodParamTypes.TryGetValue(e.Name, out var t) ? t : System.Array.Empty<Type>();
            var typeArgs = pts.Concat(new[] { method.ReturnType == typeof(void) ? typeof(object) : method.ReturnType }).ToArray();
            var delegateType = System.Linq.Expressions.Expression.GetFuncType(typeArgs);
            var ctor = delegateType.GetConstructors()[0];
            IL.Emit(OpCodes.Ldnull);        // static method target
            IL.Emit(OpCodes.Ldftn, method);
            IL.Emit(OpCodes.Newobj, ctor);
            return NajaTypes.Unknown;
        }

        // 4b. User-defined class used as a value (e.g. isinstance(err, AppError)).
        // Use runtime type resolution instead of Ldtoken, which fails on unfinished TypeBuilders.
        if (_ctx.ClassTypes.ContainsKey(e.Name))
        {
            IL.Emit(OpCodes.Ldstr, e.Name);
            IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ResolveTypeByName))!);
            return NajaTypes.Unknown;
        }

        // 5. Builtin constants

        switch (e.Name)
        {
            case "True": IL.Emit(OpCodes.Ldc_I4_1); return NajaTypes.Bool;
            case "False": IL.Emit(OpCodes.Ldc_I4_0); return NajaTypes.Bool;
            case "None": IL.Emit(OpCodes.Ldnull); return NajaTypes.None;
        }

        // 5b. Builtin container type names used as values (e.g. isinstance(x, list)).
        if (e.Name == "list")
        {
            IL.Emit(OpCodes.Ldtoken, typeof(System.Collections.Generic.List<object>));
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            return NajaTypes.Unknown;
        }
        if (e.Name == "dict")
        {
            IL.Emit(OpCodes.Ldtoken, typeof(System.Collections.Generic.Dictionary<object, object>));
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            return NajaTypes.Unknown;
        }
        if (e.Name == "set")
        {
            IL.Emit(OpCodes.Ldtoken, typeof(System.Collections.Generic.HashSet<object>));
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            return NajaTypes.Unknown;
        }
        if (e.Name == "frozenset")
        {
            IL.Emit(OpCodes.Ldtoken, typeof(System.Collections.Immutable.ImmutableHashSet<object>));
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            return NajaTypes.Unknown;
        }
        if (e.Name == "tuple")
        {
            IL.Emit(OpCodes.Ldtoken, typeof(object[]));
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            return NajaTypes.Unknown;
        }

        // 5c. Module builtins when not in Fields (ensures CLI "run" and all paths work)
        if (e.Name == "__name__") { IL.Emit(OpCodes.Ldstr, "__main__"); return NajaTypes.Str; }
        if (e.Name == "__file__") { IL.Emit(OpCodes.Ldstr, ""); return NajaTypes.Str; }

        // Fix 6: Exception names used as values (e.g. raise StopIteration, except ValueError)
        var exType = TypeMapper.ResolveExceptionType(e.Name);
        if (exType is not null)
        {
            IL.Emit(OpCodes.Ldtoken, exType);
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            return NajaTypes.Unknown;
        }

        // 6a. Namespace imports: import System → System is a valid reference
        if (_ctx.NamespaceImports.ContainsKey(e.Name))
        {
            // Namespace used as a value (e.g., passed to a function) - return null
            // The actual type resolution happens in EmitAttribute when accessing System.DateTime
            IL.Emit(OpCodes.Ldnull);
            return NajaTypes.Unknown;
        }

        // 6b. .NET imports: from System.X import Y → push the resolved Type object onto the stack.
        //    We search already-loaded assemblies first (avoids partial-AQN failures with
        //    strong-named WinForms / Drawing assemblies).
        if (_ctx.ImportMap.TryGetValue(e.Name, out var import))
        {
            // Try to resolve the type from currently-loaded assemblies.
            var resolvedType =
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(import.TypeName, throwOnError: false, ignoreCase: true))
                    .FirstOrDefault(t => t is not null);

            // If not found in loaded assemblies, try Type.GetType with assembly name (if provided)
            if (resolvedType is null && !string.IsNullOrEmpty(import.AssemblyName))
            {
                resolvedType = Type.GetType(import.TypeName + ", " + import.AssemblyName, throwOnError: false, ignoreCase: true);
            }

            // Last resort: try just the type name without assembly
            resolvedType ??= Type.GetType(import.TypeName, throwOnError: false, ignoreCase: true);

            if (resolvedType is not null)
            {
                // Push the Type constant directly — no runtime GetType call needed.
                IL.Emit(OpCodes.Ldtoken, resolvedType);
                var getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle")!;
                IL.Emit(OpCodes.Call, getTypeFromHandle);
            }
            else
            {
                // Fallback: runtime Type.GetType with the type name only (no assembly).
                // This allows ASP.NET Core and other framework types to be resolved at runtime
                // when the correct assembly is loaded via the runtimeconfig.json framework.
                IL.Emit(OpCodes.Ldstr, import.TypeName);
                var getType = typeof(Type).GetMethod("GetType", new[] { typeof(string) })!;
                IL.Emit(OpCodes.Call, getType);
            }
            return NajaTypes.Unknown;
        }

        throw new CodeGenException($"Undefined name '{e.Name}'", e.Line, e.Column);
    }

    // ── Ternary if-else ───────────────────────────────────────────────────────

    private NajaType EmitIfExpr(IfExpr e)
    {
        var elseLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        Emit(e.Condition);
        IL.Emit(OpCodes.Brfalse, elseLabel);
        var thenType = Emit(e.Then);
        IL.Emit(OpCodes.Br, endLabel);
        IL.MarkLabel(elseLabel);
        var elseType = Emit(e.Else);
        IL.MarkLabel(endLabel);

        return NajaTypes.Widen(thenType, elseType);
    }

    // ── Call ──────────────────────────────────────────────────────────────────

    private NajaType EmitCall(CallExpr e)
    {
        // ── Naja escape hatches ───────────────────────────────────────────────
        if (e.Func is NameExpr { Name: "dynamic" } && e.Args.Count == 1)
        {
            _ctx.Diagnostics?.ReportExplicitDynamic(DescribeExpr(e.Args[0].Value), e.Line, e.Column);
            var t = Emit(e.Args[0].Value);
            TypeMapper.EmitBox(IL, t);
            return NajaTypes.Unknown;   // callers use DynamicCall as expected
        }

        if (e.Func is NameExpr { Name: "cast" } && e.Args.Count == 2)
        {
            var targetType = ParseCastTarget(e.Args[0].Value, e.Line, e.Column);
            _ctx.Diagnostics?.ReportCast(DescribeExpr(e.Args[0].Value), DescribeExpr(e.Args[1].Value), e.Line, e.Column);
            var innerType = Emit(e.Args[1].Value);
            EmitCoercion(IL, innerType, targetType, e.Line, e.Column);
            return targetType;          // downstream code sees a typed result
        }
        // ─────────────────────────────────────────────────────────────────────

        // Builtin function call
        if (e.Func is NameExpr { Name: var name })
        {
            var builtin = TypeMapper.ResolveBuiltin(name);
            if (builtin is not null)
                return EmitBuiltinCall(builtin, e.Args, name);

            // Exception/class instantiation by name (ValueError, Exception, RuntimeError, etc.)
            // Always create a System.Exception with the message to avoid ctor resolution issues.
            // If user has defined a class with this name in ClassTypes, that is handled below.
            if (!_ctx.ClassTypes.ContainsKey(name) && TypeMapper.ResolveExceptionType(name) is not null)
            {
                // Map Python exception name to CLR exception type and instantiate it with the
                // provided message (or the name if no args). Fall back to System.Exception if
                // the CLR type lacks a string ctor.
                var exType = TypeMapper.ResolveExceptionType(name)!;
                var ctor = exType.GetConstructor(new[] { typeof(string) })
                    ?? typeof(Exception).GetConstructor(new[] { typeof(string) })!;

                if (e.Args.Count == 0)
                {
                    IL.Emit(OpCodes.Ldstr, name);
                }
                else
                {
                    // Emit first arg as string, ignore others for now
                    var argType = Emit(e.Args[0].Value);
                    TypeMapper.EmitBox(IL, argType);
                    var toStr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToStr))!;
                    IL.Emit(OpCodes.Call, toStr);
                }

                IL.Emit(OpCodes.Newobj, ctor);
                return NajaTypes.Unknown;
            }

            // User-defined function in same module
            if (_ctx.Methods.TryGetValue(name, out var method))
            {
                _ctx.MethodParamTypes.TryGetValue(name, out var pts);
                // Emit supplied arguments
                for (int i = 0; i < e.Args.Count; i++)
                {
                    var argType = Emit(e.Args[i].Value);
                    if (pts is not null && i < pts.Length && pts[i] == typeof(object))
                        TypeMapper.EmitBox(IL, argType);
                }
                // Fill in missing optional parameters with their compile-time default values
                // GetParameters() is safe to call on a MethodBuilder only after the type is created;
                // during emit we rely on the fact that DeclareMethod stored default values via
                // DefineParameter + SetConstant. We use MethodParamTypes for count, and fall back
                // to ldnull for any missing args beyond what was explicitly supplied.
                int totalParams = pts?.Length ?? 0;
                for (int i = e.Args.Count; i < totalParams; i++)
                {
                    // Try to get the default value from the MethodBuilder's parameter info
                    // We attempt GetParameters() safely; if it throws we just emit ldnull.
                    try
                    {
                        var mParams = method.GetParameters();
                        if (i < mParams.Length && mParams[i].HasDefaultValue)
                            EmitDefaultValue(mParams[i].DefaultValue);
                        else
                            IL.Emit(OpCodes.Ldnull);
                    }
                    catch
                    {
                        IL.Emit(OpCodes.Ldnull);
                    }
                }
                IL.Emit(OpCodes.Call, method);
                return NajaTypes.Unknown;
            }

            // User-defined class instantiation
            if (_ctx.ClassTypes.TryGetValue(name, out var classType))
            {
                ConstructorInfo defaultCtor = _ctx.ClassConstructors.TryGetValue(name, out var cb)
                    ? cb
                    : typeof(object).GetConstructor(Type.EmptyTypes)!;

                // Push args onto stack directly
                for (int i = 0; i < e.Args.Count; i++)
                {
                    var argType = Emit(e.Args[i].Value);
                    TypeMapper.EmitBox(IL, argType);
                }

                IL.Emit(OpCodes.Newobj, defaultCtor);
                return NajaTypes.Unknown; // return instance
            }

            // .NET imported type instantiation: Size(500, 400), MenuStrip(), ToolStripMenuItem("..."), ...
            // Imported names are emitted as System.Type (see EmitName); use Activator to construct at runtime.
            if (_ctx.ImportMap.ContainsKey(name))
            {
                // Push Type for the imported name
                Emit(e.Func);
                IL.Emit(OpCodes.Castclass, typeof(Type));
                var typeLocal = _ctx.Locals.Declare($"__dotnet_t_{e.Line}", typeof(Type));
                IL.Emit(OpCodes.Stloc, typeLocal);

                // Pack args into object[]
                IL.Emit(OpCodes.Ldc_I4, e.Args.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < e.Args.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    var argType = Emit(e.Args[i].Value);
                    TypeMapper.EmitBox(IL, argType);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                var ctorArgs = _ctx.Locals.Declare($"__dotnet_args_{e.Line}", typeof(object[]));
                IL.Emit(OpCodes.Stloc, ctorArgs);

                IL.Emit(OpCodes.Ldloc, typeLocal);
                IL.Emit(OpCodes.Ldloc, ctorArgs);
                var create = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.CreateDotNet),
                    new[] { typeof(Type), typeof(object[]) })!;
                IL.Emit(OpCodes.Call, create);
                return NajaTypes.Unknown;
            }
        }

        // Namespace.Type constructor call: System.DateTime(2000, 1, 1)
        if (e.Func is AttributeExpr { Object: NameExpr nsName } attr &&
            _ctx.NamespaceImports.ContainsKey(nsName.Name))
        {
            // Build full type name: Namespace.Type
            var fullTypeName = nsName.Name + "." + attr.Attribute;

            // Try to resolve the type from loaded assemblies
            var resolvedType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(fullTypeName, throwOnError: false, ignoreCase: true))
                .FirstOrDefault(t => t is not null)
                ?? Type.GetType(fullTypeName, throwOnError: false, ignoreCase: true);

            if (resolvedType is not null)
            {
                // Pack args into object[]
                IL.Emit(OpCodes.Ldc_I4, e.Args.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < e.Args.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    var argType = Emit(e.Args[i].Value);
                    TypeMapper.EmitBox(IL, argType);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                var ctorArgs = _ctx.Locals.Declare($"__dotnet_args_{e.Line}", typeof(object[]));
                IL.Emit(OpCodes.Stloc, ctorArgs);

                // Push the Type token
                IL.Emit(OpCodes.Ldtoken, resolvedType);
                var getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle")!;
                IL.Emit(OpCodes.Call, getTypeFromHandle);

                IL.Emit(OpCodes.Ldloc, ctorArgs);
                var create = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.CreateDotNet),
                    new[] { typeof(Type), typeof(object[]) })!;
                IL.Emit(OpCodes.Call, create);
                return NajaTypes.Unknown;
            }
        }

        // Method call:  obj.method(args)
        if (e.Func is AttributeExpr attr2)
            return EmitMethodCall(attr2, e.Args);

        // H5: First-class callable — emit func expression, pack args, call CallCallable
        {
            // Build args array into a local first to avoid leaving the callee on the
            // evaluation stack while emitting argument expressions (simpler verifier
            // behaviour and avoids stack shape issues for complex arg expressions).
            IL.Emit(OpCodes.Ldc_I4, e.Args.Count);
            IL.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < e.Args.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                var argT = Emit(e.Args[i].Value);
                TypeMapper.EmitBox(IL, argT);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            var argsLocal = _ctx.Locals.Declare($"__h5_args_{e.Line}", typeof(object[]));
            IL.Emit(OpCodes.Stloc, argsLocal);

            // Now emit callee, box it to object, then load the args local and call helper.
            var funcType = Emit(e.Func);
            TypeMapper.EmitBox(IL, funcType);
            IL.Emit(OpCodes.Ldloc, argsLocal);
            var callCallable = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.CallCallable))!;
            IL.Emit(OpCodes.Call, callCallable);
            return NajaTypes.Unknown;
        }
    }

    private NajaType EmitBuiltinCall(MethodInfo method, IReadOnlyList<Argument> args, string name)
    {
        var parameters = method.GetParameters();

        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(object[]))
        {
            // Variadic — pack args into object[]
            IL.Emit(OpCodes.Ldc_I4, args.Count);
            IL.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < args.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                var argType = Emit(args[i].Value);
                TypeMapper.EmitBox(IL, argType);
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            // Fixed arity — emit each arg
            for (int i = 0; i < Math.Min(args.Count, parameters.Length); i++)
            {
                var argType = Emit(args[i].Value);
                // Convert if needed
                if (parameters[i].ParameterType == typeof(object))
                    TypeMapper.EmitBox(IL, argType);
            }
        }

        IL.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);

        if (method.ReturnType == typeof(void))
        {
            IL.Emit(OpCodes.Ldnull);  // push null so stack stays balanced
            return NajaTypes.None;
        }

        if (method.ReturnType == typeof(long)) return NajaTypes.Int;
        if (method.ReturnType == typeof(double)) return NajaTypes.Float;
        if (method.ReturnType == typeof(bool)) return NajaTypes.Bool;
        if (method.ReturnType == typeof(string)) return NajaTypes.Str;
        if (method.ReturnType == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
        if (method.ReturnType == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);

        return NajaTypes.Unknown;
    }

    private NajaType EmitMethodCall(AttributeExpr attr, IReadOnlyList<Argument> args)
    {
        // ── base.Method() or super().Method() ──
        if ((attr.Object is NameExpr ne && (ne.Name == "base" || ne.Name == "super")) ||
            (attr.Object is CallExpr ce && ce.Func is NameExpr ceNe && (ceNe.Name == "base" || ceNe.Name == "super")))
        {
            var baseType = _ctx.TypeBuilder.BaseType;
            if (baseType == null) throw new CodeGenException($"[L{attr.Line}:C{attr.Column}] base/super call outside a valid class context");

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            List<MethodInfo> candidates;

            string methodKey = $"{baseType.Name}.{attr.Attribute}";
            if (_ctx.AllClassMethods.TryGetValue(methodKey, out var preMB))
            {
                candidates = new List<MethodInfo> { preMB };
            }
            else
            {
                // If baseType is a TypeBuilder, we already checked AllClassMethods.
                // We should only use reflection on actual baked Types or the BaseType.
                Type searchType = baseType is TypeBuilder tb ? tb.BaseType ?? typeof(object) : baseType;
                candidates = searchType.GetMethods(flags)
                                     .Where(m => m.Name.Equals(attr.Attribute, StringComparison.OrdinalIgnoreCase))
                                     .Cast<MethodInfo>()
                                     .ToList();
            }



            // Special-case Python super().__init__(...) when the base is a .NET type like System.Exception:
            // map it to an appropriate constructor on the base type.
            if (candidates.Count == 0 && attr.Attribute == "__init__")
            {
                var ctors = baseType.GetConstructors(flags).ToList();
                var ctor = ctors.FirstOrDefault(c =>
                {
                    if (c is ConstructorBuilder && _ctx.ClassCtorArgCounts.TryGetValue(baseType.Name, out var count))
                        return count == args.Count;
                    return c.GetParameters().Length == args.Count;
                }) ?? ctors.FirstOrDefault();

                if (ctor == null)
                    throw new CodeGenException($"[L{attr.Line}:C{attr.Column}] No matching base constructor for '__init__' found on '{baseType.Name}'");

                IL.Emit(OpCodes.Ldarg_0); // 'this'

                Type[]? pts3 = null;
                if (ctor is ConstructorBuilder && _ctx.ClassCtorArgCounts.TryGetValue(baseType.Name, out var pc))
                    pts3 = Enumerable.Repeat(typeof(object), pc).ToArray();

                var ps = pts3 ?? ctor.GetParameters().Select(p => p.ParameterType).ToArray();

                for (int i = 0; i < args.Count; i++)
                {
                    var t = Emit(args[i].Value);
                    if (i < ps.Length)
                    {
                        Type pType = ps[i];
                        if (pType == typeof(object))
                            TypeMapper.EmitBox(IL, t);
                        else if (pType.IsValueType)
                        {
                            if (t is UnknownType) IL.Emit(OpCodes.Unbox_Any, pType);
                            else if (pType == typeof(int) && t is IntType) IL.Emit(OpCodes.Conv_I4);
                        }
                        else if (pType == typeof(string) && t is UnknownType)
                            IL.Emit(OpCodes.Castclass, typeof(string));
                    }

                }

                IL.Emit(OpCodes.Call, ctor);
                IL.Emit(OpCodes.Ldnull);
                return NajaTypes.None;
            }

            var method = candidates.FirstOrDefault(m =>
            {
                if (_ctx.AllClassMethodParamTypes.TryGetValue($"{baseType.Name}.{m.Name}", out var pts4))
                    return pts4.Length == args.Count;
                return m.GetParameters().Length == args.Count;
            }) ?? candidates.FirstOrDefault();


            if (method == null) throw new CodeGenException($"[L{attr.Line}:C{attr.Column}] No matching base method '{attr.Attribute}' found on '{baseType.Name}'");

            IL.Emit(OpCodes.Ldarg_0); // 'this'

            Type[]? pts = null;
            if (_ctx.AllClassMethods.TryGetValue(methodKey, out var _))
                _ctx.AllClassMethodParamTypes.TryGetValue(methodKey, out pts);

            var ps2 = pts != null ? null : method.GetParameters();
            int pCount = pts?.Length ?? ps2!.Length;

            for (int i = 0; i < args.Count; i++)
            {
                var t = Emit(args[i].Value);
                if (i < pCount)
                {
                    Type pType = pts != null ? pts[i] : ps2![i].ParameterType;
                    if (pType == typeof(object))
                        TypeMapper.EmitBox(IL, t);
                    else if (pType.IsValueType)
                    {
                        if (t is UnknownType) IL.Emit(OpCodes.Unbox_Any, pType);
                        else if (pType == typeof(int) && t is IntType) IL.Emit(OpCodes.Conv_I4);
                    }
                    else if (pType == typeof(string) && t is UnknownType)
                        IL.Emit(OpCodes.Castclass, typeof(string));
                }
            }


            IL.Emit(OpCodes.Call, method); // non-virtual call

            if (method.ReturnType == typeof(void))
            {
                IL.Emit(OpCodes.Ldnull);
                return NajaTypes.None;
            }

            if (method.ReturnType == typeof(long)) return NajaTypes.Int;
            if (method.ReturnType == typeof(double)) return NajaTypes.Float;
            if (method.ReturnType == typeof(bool)) return NajaTypes.Bool;
            if (method.ReturnType == typeof(string)) return NajaTypes.Str;
            return NajaTypes.Unknown;
        }

        var objType = Emit(attr.Object);

        // ── String methods ────────────────────────────────────────────────────
        if (objType is StrType)
        {
            var bridge = ResolveStrMethod(attr.Attribute);
            if (bridge is not null)
            {
                foreach (var arg in args) { var t = Emit(arg.Value); TypeMapper.EmitBox(IL, t); }
                var expectedArgs = bridge.GetParameters().Length - 1;
                for (int i = args.Count; i < expectedArgs; i++) IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Call, bridge);
                if (bridge.ReturnType == typeof(void)) { IL.Emit(OpCodes.Ldnull); return NajaTypes.None; }
                if (bridge.ReturnType == typeof(long)) return NajaTypes.Int;
                if (bridge.ReturnType == typeof(double)) return NajaTypes.Float;
                if (bridge.ReturnType == typeof(bool)) return NajaTypes.Bool;
                if (bridge.ReturnType == typeof(string)) return NajaTypes.Str;
                if (bridge.ReturnType == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
                if (bridge.ReturnType == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
                return NajaTypes.Unknown;
            }
        }

        // ── List methods ──────────────────────────────────────────────────────
        if (objType is ListType)
        {
            var bridge = ResolveListMethod(attr.Attribute);
            if (bridge is not null)
            {
                foreach (var arg in args) { var t = Emit(arg.Value); TypeMapper.EmitBox(IL, t); }
                var expectedArgs = bridge.GetParameters().Length - 1;
                for (int i = args.Count; i < expectedArgs; i++) IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Call, bridge);
                if (bridge.ReturnType == typeof(void)) { IL.Emit(OpCodes.Ldnull); return NajaTypes.None; }
                if (bridge.ReturnType == typeof(long)) return NajaTypes.Int;
                if (bridge.ReturnType == typeof(double)) return NajaTypes.Float;
                if (bridge.ReturnType == typeof(bool)) return NajaTypes.Bool;
                if (bridge.ReturnType == typeof(string)) return NajaTypes.Str;
                if (bridge.ReturnType == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
                if (bridge.ReturnType == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
                return NajaTypes.Unknown;
            }
        }

        // ── Dict methods ──────────────────────────────────────────────────────
        if (objType is DictType)
        {
            var bridge = ResolveDictMethod(attr.Attribute);
            if (bridge is not null)
            {
                foreach (var arg in args) { var t = Emit(arg.Value); TypeMapper.EmitBox(IL, t); }
                var expectedArgs = bridge.GetParameters().Length - 1;
                for (int i = args.Count; i < expectedArgs; i++) IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Call, bridge);
                if (bridge.ReturnType == typeof(void)) { IL.Emit(OpCodes.Ldnull); return NajaTypes.None; }
                if (bridge.ReturnType == typeof(long)) return NajaTypes.Int;
                if (bridge.ReturnType == typeof(double)) return NajaTypes.Float;
                if (bridge.ReturnType == typeof(bool)) return NajaTypes.Bool;
                if (bridge.ReturnType == typeof(string)) return NajaTypes.Str;
                if (bridge.ReturnType == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
                if (bridge.ReturnType == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
                return NajaTypes.Unknown;
            }
        }

        // ── Unknown type — use dynamic fallback ───────────────────────────────
        TypeMapper.EmitBox(IL, objType);
        IL.Emit(OpCodes.Ldstr, attr.Attribute);
        IL.Emit(OpCodes.Ldc_I4, args.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < args.Count; i++)
        {
            IL.Emit(OpCodes.Dup); IL.Emit(OpCodes.Ldc_I4, i);
            var at = Emit(args[i].Value); TypeMapper.EmitBox(IL, at);
            IL.Emit(OpCodes.Stelem_Ref);
        }
        var dynCall = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicCall))!;
        IL.Emit(OpCodes.Call, dynCall);
        return NajaTypes.Unknown;
    }

    private static System.Reflection.MethodInfo? ResolveStrMethod(string name) => name switch
    {
        "upper" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrUpper)),
        "lower" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrLower)),
        "strip" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrStrip)),
        "lstrip" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrLStrip)),
        "rstrip" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrRStrip)),
        "startswith" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrStartsWith)),
        "endswith" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrEndsWith)),
        "isdigit" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrIsDigit)),
        "isalpha" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrIsAlpha)),
        "isalnum" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrIsAlNum)),
        "find" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrFind)),
        "index" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrIndex)),
        "replace" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrReplace)),
        "center" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrCenter)),
        "ljust" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrLJust)),
        "rjust" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrRJust)),
        "zfill" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrZFill)),
        "count" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrCount)),
        "join" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrJoin)),
        "split" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrSplit)),
        "splitlines" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrSplitLines)),
        "title" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrTitle)),
        "format" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Format)),
        _ => null
    };

    private static System.Reflection.MethodInfo? ResolveListMethod(string name) => name switch
    {
        "append" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListAppend)),
        "extend" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListExtend)),
        "insert" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListInsert)),
        "pop" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListPop)),
        "remove" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListRemove)),
        "reverse" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListReverse)),
        "sort" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListSort)),
        "index" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListIndex)),
        "count" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListCount)),
        "copy" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListCopy)),
        "clear" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListClear)),
        _ => null
    };

    private static System.Reflection.MethodInfo? ResolveDictMethod(string name) => name switch
    {
        "keys" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictKeys)),
        "values" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictValues)),
        "items" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictItems)),
        "get" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictGet)),
        "pop" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictPop)),
        "update" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictUpdate)),
        "clear" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictClear)),
        "copy" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictCopy)),
        _ => null
    };

    // ── Attribute access ──────────────────────────────────────────────────────

    private NajaType EmitAttribute(AttributeExpr e)
    {
        // ── Namespace.Type access: System.DateTime, etc. ──
        if (e.Object is NameExpr nsName &&
            _ctx.NamespaceImports.TryGetValue(nsName.Name, out var nsAsmName))
        {
            // Build full type name: Namespace.Type
            var fullTypeName = nsName.Name + "." + e.Attribute;

            // Try to resolve the type from loaded assemblies
            Type? resolvedType = null;

            if (string.IsNullOrEmpty(nsAsmName))
            {
                // No specific assembly - search all loaded assemblies
                resolvedType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(fullTypeName, throwOnError: false, ignoreCase: true))
                    .FirstOrDefault(t => t is not null);
            }
            else
            {
                // Try with the specified assembly name first
                resolvedType = Type.GetType(fullTypeName + ", " + nsAsmName, throwOnError: false, ignoreCase: true);
            }

            // Fallback: try without assembly qualification
            resolvedType ??= Type.GetType(fullTypeName, throwOnError: false, ignoreCase: true);

            if (resolvedType is not null)
            {
                // Push the Type token directly
                IL.Emit(OpCodes.Ldtoken, resolvedType);
                var getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle")!;
                IL.Emit(OpCodes.Call, getTypeFromHandle);
                return NajaTypes.Unknown;
            }

            // If we can't resolve the type, fall through to dynamic handling
        }

        // ── Imported .NET type static member access: Color.White, FontStyle.Bold, etc. ──
        if (e.Object is NameExpr importedName &&
            _ctx.ImportMap.TryGetValue(importedName.Name, out var importEntry))
        {
            var dotnetType =
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(importEntry.TypeName, throwOnError: false, ignoreCase: true))
                    .FirstOrDefault(t => t is not null)
                ?? Type.GetType(importEntry.TypeName + ", " + importEntry.AssemblyName)
                ?? Type.GetType(importEntry.TypeName);

            if (dotnetType is not null)
            {
                Emit(e.Object);
                IL.Emit(OpCodes.Pop);

                // IMPORTANT: Check for enum types FIRST before static field lookup.
                // Enum members are stored as literal fields in metadata, but they cannot
                // be accessed via Ldsfld like regular static fields. We must parse and
                // emit the integer value directly.
                // Enum value by name (FormBorderStyle.Fixed3D, DockStyle.Fill, etc.)
                if (dotnetType.IsEnum)
                {
                    try
                    {
                        var enumVal = Enum.Parse(dotnetType, e.Attribute, ignoreCase: true);
                        var underlying = Convert.ToInt64(enumVal);
                        IL.Emit(OpCodes.Ldc_I8, underlying);
                        // Removed Conv_I4: NajaTypes.Int is Int64, so leave it as an 8-byte integer
                        return NajaTypes.Int;
                    }
                    catch { /* fall through to static field/property lookup */ }
                }

                // Try static property first (Color.White, Color.FromArgb as property, etc.)
                var staticProp = dotnetType.GetProperty(e.Attribute,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (staticProp?.GetGetMethod() is { } staticGetter)
                {
                    IL.Emit(OpCodes.Call, staticGetter);
                    var pt = staticProp.PropertyType;
                    if (pt == typeof(long)) return NajaTypes.Int;
                    if (pt == typeof(double)) return NajaTypes.Float;
                    if (pt == typeof(bool)) return NajaTypes.Bool;
                    if (pt == typeof(string)) return NajaTypes.Str;

                    // FIX: Immediately box any native struct so it becomes a valid System.Object
                    if (pt.IsValueType) IL.Emit(OpCodes.Box, pt);
                    return NajaTypes.Unknown;
                }

                // Try static field (Color.Empty, IntPtr.Zero, etc. - but NOT enum members)
                var staticField = dotnetType.GetField(e.Attribute,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (staticField is not null && !staticField.FieldType.IsEnum)
                {
                    IL.Emit(OpCodes.Ldsfld, staticField);
                    var ft = staticField.FieldType;
                    if (ft == typeof(long)) return NajaTypes.Int;
                    if (ft == typeof(double)) return NajaTypes.Float;
                    if (ft == typeof(bool)) return NajaTypes.Bool;
                    if (ft == typeof(string)) return NajaTypes.Str;

                    // FIX: Immediately box any native struct so it becomes a valid System.Object
                    if (ft.IsValueType) IL.Emit(OpCodes.Box, ft);
                    return NajaTypes.Unknown;
                }

                IL.Emit(OpCodes.Ldtoken, dotnetType);
                IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                IL.Emit(OpCodes.Ldstr, e.Attribute);
                var staticGetAttr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetStaticAttr),
                    new[] { typeof(Type), typeof(string) });
                if (staticGetAttr is not null)
                {
                    IL.Emit(OpCodes.Call, staticGetAttr);
                    return NajaTypes.Unknown;
                }
                IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetAttr))!);
                return NajaTypes.Unknown;
            }
        }

        var objType = Emit(e.Object);

        bool isNativeProperty = false;
        if (e.Object is NameExpr { Name: var n1 } && n1 == (_ctx.SelfName ?? "self"))
        {
            Type? currentBase = _ctx.TypeBuilder.BaseType;
            while (currentBase != null && currentBase != typeof(object))
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                if (currentBase.GetProperty(e.Attribute, flags) != null)
                {
                    isNativeProperty = true;
                    break;
                }
                currentBase = currentBase.BaseType;
            }
        }

        if (!isNativeProperty &&
            e.Object is NameExpr { Name: var n } &&
            n == (_ctx.SelfName ?? "self") &&
            _ctx.InstanceFields.TryGetValue(e.Attribute, out var iField))
        {
            IL.Emit(OpCodes.Ldfld, iField);
            if (iField.FieldType == typeof(long)) return NajaTypes.Int;
            if (iField.FieldType == typeof(double)) return NajaTypes.Float;
            if (iField.FieldType == typeof(bool)) return NajaTypes.Bool;
            if (iField.FieldType == typeof(string)) return NajaTypes.Str;
            if (iField.FieldType == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
            if (iField.FieldType == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
            if (iField.FieldType == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
            if (iField.FieldType == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());
            return NajaTypes.Unknown;
        }

        var clrType = TypeMapper.ToClrType(objType);

        var prop = clrType.GetProperty(e.Attribute, BindingFlags.Public | BindingFlags.Instance);
        if (prop?.GetGetMethod() is { } getter)
        {
            IL.Emit(OpCodes.Callvirt, getter);
            var pt = prop.PropertyType;
            if (pt == typeof(long)) return NajaTypes.Int;
            if (pt == typeof(double)) return NajaTypes.Float;
            if (pt == typeof(bool)) return NajaTypes.Bool;
            if (pt == typeof(string)) return NajaTypes.Str;
            if (pt == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
            if (pt == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
            if (pt == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
            if (pt == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());

            // FIX: Box instance property structs
            if (pt.IsValueType) IL.Emit(OpCodes.Box, pt);
            return NajaTypes.Unknown;
        }

        var field = clrType.GetField(e.Attribute, BindingFlags.Public | BindingFlags.Instance);
        if (field is not null)
        {
            IL.Emit(OpCodes.Ldfld, field);
            var ft = field.FieldType;
            if (ft == typeof(long)) return NajaTypes.Int;
            if (ft == typeof(double)) return NajaTypes.Float;
            if (ft == typeof(bool)) return NajaTypes.Bool;
            if (ft == typeof(string)) return NajaTypes.Str;
            if (ft == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
            if (ft == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
            if (ft == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
            if (ft == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());

            // FIX: Box instance field structs
            if (ft.IsValueType) IL.Emit(OpCodes.Box, ft);
            return NajaTypes.Unknown;
        }

        TypeMapper.EmitBox(IL, objType);
        IL.Emit(OpCodes.Ldstr, e.Attribute);
        var getAttr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetAttr))!;
        IL.Emit(OpCodes.Call, getAttr);
        return NajaTypes.Unknown;
    }

    // ── Subscript ─────────────────────────────────────────────────────────────

    private NajaType EmitSubscript(SubscriptExpr e)
    {
        // Slice indexing: a[1:10:2]
        if (e.Index is SliceExpr slice)
        {
            // Emit the object and store it — we need NajaSlice (this) on stack first
            // for the instance method call, then the object as argument.
            var objType2 = Emit(e.Object);
            TypeMapper.EmitBox(IL, objType2);
            var objLocal = _ctx.Locals.Declare($"__slobj_{e.Line}", typeof(object));
            IL.Emit(OpCodes.Stloc, objLocal);

            // Build NajaSlice (this for the instance call)
            EmitSlicePart(slice.Lower);
            EmitSlicePart(slice.Upper);
            EmitSlicePart(slice.Step);
            var sliceCtor = typeof(NajaSlice).GetConstructor(
                new[] { typeof(object), typeof(object), typeof(object) })!;
            IL.Emit(OpCodes.Newobj, sliceCtor);  // stack: [NajaSlice]

            // Call appropriate apply method: instance call needs (this=NajaSlice, arg=obj)
            if (objType2 is StrType)
            {
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Castclass, typeof(string));  // stack: [NajaSlice, string]
                var applyStr = typeof(NajaSlice).GetMethod("ApplyToString")!;
                IL.Emit(OpCodes.Call, applyStr);
                return NajaTypes.Str;
            }
            else
            {
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Castclass, typeof(System.Collections.Generic.List<object>));  // stack: [NajaSlice, List]
                var applyList = typeof(NajaSlice).GetMethod("ApplyToList")!;
                IL.Emit(OpCodes.Call, applyList);
                return new ListType(NajaTypes.Unknown);
            }
        }

        // Regular indexing
        var objType = Emit(e.Object);
        var idxType = Emit(e.Index);

        if (objType is ListType l)
        {
            TypeMapper.EmitBox(IL, idxType);
            var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
            IL.Emit(OpCodes.Call, getItem);

            // FIX: Unbox the object back to its raw semantic type
            TypeMapper.EmitUnbox(IL, l.ElementType);

            return l.ElementType;
        }

        if (objType is StrType)
        {
            TypeMapper.EmitBox(IL, idxType);
            var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
            IL.Emit(OpCodes.Call, getItem);

            // FIX: Cast object back to string
            TypeMapper.EmitUnbox(IL, NajaTypes.Str);

            return NajaTypes.Str;
        }

        if (objType is DictType dt)
        {
            TypeMapper.EmitBox(IL, idxType);
            var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
            IL.Emit(OpCodes.Call, getItem);

            // FIX: Unbox the object back to its raw semantic type
            TypeMapper.EmitUnbox(IL, dt.ValueType);

            return dt.ValueType;
        }

        // Unknown — use dynamic helper
        TypeMapper.EmitBox(IL, objType);
        TypeMapper.EmitBox(IL, idxType);
        var dynGet = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
        IL.Emit(OpCodes.Call, dynGet);
        return NajaTypes.Unknown;
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

    // ── Slice  a[1:10:2] ─────────────────────────────────────────────────────

    private NajaType EmitSlice(SliceExpr e)
    {
        // Build a NajaSlice object (runtime helper) — or call string/list slice
        var sliceCtor = typeof(NajaSlice).GetConstructor(
            new[] { typeof(object), typeof(object), typeof(object) })!;

        EmitSlicePart(e.Lower);
        EmitSlicePart(e.Upper);
        EmitSlicePart(e.Step);
        IL.Emit(OpCodes.Newobj, sliceCtor);
        return NajaTypes.Unknown;
    }

    private void EmitSlicePart(Expression? expr)
    {
        if (expr is null) IL.Emit(OpCodes.Ldnull);
        else { var t = Emit(expr); TypeMapper.EmitBox(IL, t); }
    }

    // ── Lambda  lambda x: x+1 ────────────────────────────────────────────────

    private NajaType EmitLambda(LambdaExpr e)
    {
        // Compile lambda as a static method on the host type, return a delegate
        var paramTypes = e.Params.Select(_ => typeof(object)).ToArray();
        var lambdaName = $"<lambda>_{e.Line}_{e.Column}";
        var mb = _ctx.TypeBuilder.DefineMethod(
            lambdaName,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            paramTypes);

        // Emit lambda body into new method
        var lambdaIL = mb.GetILGenerator();
        var paramNames = e.Params.Select(p => p.Name).ToList();
        var lambdaCtx = new EmitContext(lambdaIL, _ctx.Model, _ctx.TypeBuilder,
                                          _ctx.Module, typeof(object), paramNames);
        foreach (var (k, v) in _ctx.Fields) lambdaCtx.Fields[k] = v;
        foreach (var (k, v) in _ctx.Methods) lambdaCtx.Methods[k] = v;
        foreach (var (k, v) in _ctx.MethodParamTypes) lambdaCtx.MethodParamTypes[k] = v;
        // Copy locals from outer scope for closure support
        foreach (var localName in _ctx.Locals.GetAllNames())
        {
            var local = _ctx.Locals.TryGet(localName);
            if (local != null && !lambdaCtx.Locals.Contains(localName))
            {
                lambdaCtx.Locals.Declare(localName, local.LocalType);
            }
        }

        var bodyEmitter = new ExpressionEmitter(lambdaCtx);
        var retType = bodyEmitter.Emit(e.Body);
        TypeMapper.EmitBox(lambdaIL, retType);
        lambdaIL.Emit(OpCodes.Ret);

        // Create a Func<...> delegate pointing at this method
        var delegateType = System.Linq.Expressions.Expression.GetFuncType(paramTypes.Concat(new[] { typeof(object) }).ToArray());
        var ctor = delegateType.GetConstructors()[0];
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldftn, mb);
        IL.Emit(OpCodes.Newobj, ctor);

        // If any parameters have defaults, evaluate them inline and wrap the delegate
        bool hasDefaults = e.Params.Any(p => p.Default is not null);
        if (hasDefaults)
        {
            // Build array of evaluated defaults inline in the current context
            int defaultsCount = e.Params.Count(p => p.Default is not null);
            IL.Emit(OpCodes.Ldc_I4, defaultsCount);
            IL.Emit(OpCodes.Newarr, typeof(object));

            int defaultIndex = 0;
            for (int i = 0; i < e.Params.Count; i++)
            {
                var p = e.Params[i];
                if (p.Default is null) continue;
                IL.Emit(OpCodes.Dup); // array
                IL.Emit(OpCodes.Ldc_I4, defaultIndex);
                // Evaluate the default expression in the current context
                Emit(p.Default);
                TypeMapper.EmitBox(IL, NajaTypes.Unknown);
                IL.Emit(OpCodes.Stelem_Ref);
                defaultIndex++;
            }

            // Call CreateFunctionWithDefaults(delegate, defaults)
            var createFnHelper = typeof(NajaBuiltins).GetMethod("CreateFunctionWithDefaults",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            IL.Emit(OpCodes.Call, createFnHelper);
        }

        return NajaTypes.Unknown;
    }

    // ── F-string interpolation ────────────────────────────────────────────────

    /// <summary>
    /// Classifies a Python format spec into one of three categories:
    /// - Returns true if the spec is safe for string.Format (only .NET-native codes with no flags)
    /// - Returns false if the spec requires routing to NajaBuiltins.Format (Python-semantic codes)
    /// </summary>
    private static bool IsNetNativeSpec(string pySpec, out string csSpec)
    {
        csSpec = null;
        if (string.IsNullOrEmpty(pySpec))
            return false;

        // Dynamic spec: contains nested {variable} references — must use Python runtime
        if (pySpec.Contains('{'))
            return false;

        // Last character is the Python type code
        char typeChar = pySpec[^1];

        // Always route to runtime helper for Python-only format codes
        if ("bos%".Contains(typeChar))
            return false; // binary, octal, string %, percent
        if (pySpec.Contains('_'))
            return false; // _ grouping separator
        if (pySpec.Contains('+'))
            return false; // explicit positive sign
        if (pySpec.Contains('#'))
            return false; // alternate form (0b, 0o, 0x prefix)
        if (HasFillAlign(pySpec))
            return false; // fill/align characters present

        // Safe subset: d, f, n, x, X with optional width.precision only
        csSpec = TranslateSimpleSpec(pySpec, typeChar);
        return csSpec != null;
    }

    /// <summary>
    /// Detects if a format spec contains fill/align characters: < > ^ =
    /// </summary>
    private static bool HasFillAlign(string spec)
    {
        return spec.IndexOfAny(new[] { '<', '>', '^', '=' }) >= 0;
    }

    /// <summary>
    /// Translates a whitelisted Python format spec to .NET composite format code.
    /// Example: "05d" -> "D5", ".2f" -> "F2"
    /// </summary>
    private static string TranslateSimpleSpec(string spec, char typeChar)
    {
        // Strip the type char, leaving optional [[0]width][.precision]
        string body = spec[..^1];
        return typeChar switch
        {
            // 'd': route all to Python runtime — .NET D format counts digits excluding sign,
            // but Python's width includes the sign (e.g. {:06d} for -42 = "-00042" not "-000042").
            'd' => null,
            'f' => ParseFP(body, 'F'),  // {x:.2f} → {0:F2}
            // 'e'/'g': route to Python runtime — .NET E format uses uppercase and may differ in exponent digits
            'e' => null,
            'g' => null,
            'n' => "N" + body,          // {n:n} → {0:N}
            'x' => "x" + body,          // {n:x} → {0:x}
            'X' => "X" + body,          // {n:X} → {0:X}
            _ => null                   // unknown — punt to runtime
        };
    }

    /// <summary>
    /// Extracts precision from Python float spec body.
    /// Examples: ".2" → "2", "10.2" → "2", "10" → "", "" → ""
    /// .NET composite format only takes precision in the format token, not width.
    /// </summary>
    private static string ParseFP(string body, char netType)
    {
        // body examples: ".2"  "10.2"  "10"  ""
        int dotIdx = body.IndexOf('.');
        if (dotIdx >= 0)
            return netType + body[(dotIdx + 1)..]; // extract precision digits only
        return netType.ToString();
    }

    private NajaType EmitFString(FStringExpr e)
    {
        // Parse {expr} segments out of the raw template and build string via concat
        var parts = ParseFStringParts(e.RawTemplate);

        if (parts.Count == 0)
        {
            IL.Emit(OpCodes.Ldstr, "");
            return NajaTypes.Str;
        }

        // Build via string.Concat on object[]
        IL.Emit(OpCodes.Ldc_I4, parts.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));

        var toStr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToStr))!;
        var repr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Repr))!;
        var najaFormat = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Format), 
            new[] { typeof(object), typeof(object) })!;

        for (int i = 0; i < parts.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);

            var (isExpr, text, formatSpec, conversion) = parts[i];
            if (isExpr)
            {
                // Parse and emit the expression inside {}
                try
                {
                    // Special-case: nested f-string literal like "f'...'". The lexer
                    // cannot lex nested quotes inside f-strings correctly here, so
                    // detect and emit a nested FStringExpr directly.
                    if (text.Length >= 2 && (text[0] == 'f' || text[0] == 'F') && (text[1] == '"' || text[1] == '\'') && text[^1] == text[1])
                    {
                        var inner = text.Substring(2, text.Length - 3);
                        var nested = new Naja.Parser.FStringExpr(inner, e.Line, e.Column);
                        var tn = Emit(nested);
                        TypeMapper.EmitBox(IL, tn);
                        if (conversion == 'r' || conversion == 'a')
                            IL.Emit(OpCodes.Call, repr);
                        else
                            IL.Emit(OpCodes.Call, toStr);
                        goto SKIP_PARSE_EXPR;
                    }

                    var tokens = new Naja.Lexer.Lexer(text).Tokenize();
                    var expr = new Naja.Parser.Parser(tokens).ParseExpression();

                    if (string.IsNullOrEmpty(formatSpec))
                    {
                        // Path 1: No format spec — just stringify
                        var t = Emit(expr);
                        TypeMapper.EmitBox(IL, t);
                        // Apply !r / !s / !a conversion
                        if (conversion == 'r' || conversion == 'a')
                            IL.Emit(OpCodes.Call, repr);
                        else
                            IL.Emit(OpCodes.Call, toStr);
                    }
                    else if (IsNetNativeSpec(formatSpec, out var csSpec))
                    {
                        // Path 2: .NET-native spec (d, f, e, g, n, x, X with no flags)
                        // Safe to use string.Format
                        IL.Emit(OpCodes.Ldstr, "{0:" + csSpec + "}");
                        var t = Emit(expr);
                        TypeMapper.EmitBox(IL, t);
                        IL.Emit(OpCodes.Call, typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) })!);
                    }
                    else
                    {
                        // Path 3: Python-semantic spec (b, o, %, #, +, fill/align, _, etc.)
                        // Route to NajaBuiltins.Format(value, spec)
                        var t = Emit(expr);
                        TypeMapper.EmitBox(IL, t);
                        if (formatSpec != null && formatSpec.Contains('{'))
                        {
                            // Evaluate nested replacement fields inside the format spec
                            Emit(new Naja.Parser.FStringExpr(formatSpec, e.Line, e.Column));
                        }
                        else
                        {
                            IL.Emit(OpCodes.Ldstr, formatSpec ?? "");
                        }
                        IL.Emit(OpCodes.Call, najaFormat);
                    }
                }
                catch
                {
                    IL.Emit(OpCodes.Ldstr, $"{{{text}}}");
                }
            SKIP_PARSE_EXPR: ;
            }
            else
            {
                IL.Emit(OpCodes.Ldstr, text);
            }

            IL.Emit(OpCodes.Stelem_Ref);
        }

        var concat = typeof(string).GetMethod("Concat", new[] { typeof(object[]) })!;
        IL.Emit(OpCodes.Call, concat);
        return NajaTypes.Str;
    }

    private static List<(bool IsExpr, string Text, string? FormatSpec, char Conversion)> ParseFStringParts(string template)
    {
        var parts = new List<(bool, string, string?, char)>();
        var sb = new System.Text.StringBuilder();
        int i = 0;

        while (i < template.Length)
        {
            if (i + 1 < template.Length && template[i] == '{' && template[i + 1] == '{')
            {
                sb.Append('{'); i += 2;
            }
            else if (i + 1 < template.Length && template[i] == '}' && template[i + 1] == '}')
            {
                sb.Append('}'); i += 2;
            }
            else if (template[i] == '{')
            {
                if (sb.Length > 0) { parts.Add((false, sb.ToString(), null, '\0')); sb.Clear(); }
                i++;
                int depth = 1;
                bool inFormat = false;
                char conversion = '\0';
                var exprSb = new System.Text.StringBuilder();
                var fmtSb = new System.Text.StringBuilder();

                while (i < template.Length && depth > 0)
                {
                    char ch = template[i];
                    if (ch == '{') { depth++; }
                    else if (ch == '}') { depth--; if (depth == 0) break; }
                    else if (depth == 1 && ch == '!' && !inFormat)
                    {
                        // Peek for conversion char: r, s, or a
                        if (i + 1 < template.Length && (template[i + 1] == 'r' || template[i + 1] == 's' || template[i + 1] == 'a'))
                        {
                            conversion = template[i + 1];
                            i += 2;
                            continue;
                        }
                    }
                    else if (depth == 1 && ch == ':' && !inFormat)
                    {
                        inFormat = true; i++; continue;
                    }

                    if (depth > 0)
                    {
                        if (inFormat) fmtSb.Append(template[i++]);
                        else exprSb.Append(template[i++]);
                    }
                }
                parts.Add((true, exprSb.ToString(), inFormat ? fmtSb.ToString() : null, conversion));
                sb.Clear();
                i++; // skip closing }
            }
            else
            {
                sb.Append(template[i++]);
            }
        }

        if (sb.Length > 0) parts.Add((false, sb.ToString(), null, '\0'));
        return parts;
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
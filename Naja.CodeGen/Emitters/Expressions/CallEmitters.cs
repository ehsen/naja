using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Emits IL opcodes for call expressions: function calls, method calls, and builtin calls.
/// Handles user functions, builtins, .NET types, exception instantiation, and first-class callables.
/// </summary>
public sealed class CallEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public CallEmitters(EmitContext ctx, ExpressionEmitter mainEmitter) : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    public NajaType EmitCall(CallExpr e)
    {
        // Validate call argument structure before emitting any IL
        bool seenKwarg = false;
        var seenKwargNames = new HashSet<string>();
        foreach (var arg in e.Args)
        {
            if (arg.IsStar || arg.IsDoubleStar) continue;
            if (arg.Keyword is not null)
            {
                seenKwarg = true;
                if (!seenKwargNames.Add(arg.Keyword))
                    throw new CodeGenException(
                        $"SyntaxError: keyword argument repeated: '{arg.Keyword}'", e.Line, e.Column);
            }
            else if (seenKwarg)
                throw new CodeGenException(
                    "SyntaxError: positional argument follows keyword argument", e.Line, e.Column);
        }

        // Handle escape hatches (dynamic, cast)
        if (e.Func is NameExpr { Name: "dynamic" } && e.Args.Count == 1)
        {
            _ctx.Diagnostics?.ReportExplicitDynamic(DescribeExpr(e.Args[0].Value), e.Line, e.Column);
            var t = _mainEmitter.Emit(e.Args[0].Value);
            TypeMapper.EmitBox(IL, t);
            return NajaTypes.Unknown;
        }

        if (e.Func is NameExpr { Name: "cast" } && e.Args.Count == 2)
        {
            var targetType = ParseCastTarget(e.Args[0].Value, e.Line, e.Column);
            _ctx.Diagnostics?.ReportCast(DescribeExpr(e.Args[0].Value), DescribeExpr(e.Args[1].Value), e.Line, e.Column);
            var innerType = _mainEmitter.Emit(e.Args[1].Value);
            EmitCoercion(IL, innerType, targetType, e.Line, e.Column);
            return targetType;
        }

        // Handle builtin functions by name
        if (e.Func is NameExpr { Name: var name })
        {
            var builtin = TypeMapper.ResolveBuiltin(name);
            if (builtin is not null)
                return EmitBuiltinCall(builtin, e.Args, name);

            // Exception/class instantiation by name
            if (!_ctx.ClassTypes.ContainsKey(name) && TypeMapper.ResolveExceptionType(name) is not null)
            {
                var exType = TypeMapper.ResolveExceptionType(name)!;
                var ctor = exType.GetConstructor(new[] { typeof(string) })
                    ?? typeof(Exception).GetConstructor(new[] { typeof(string) })!;

                if (e.Args.Count == 0)
                {
                    IL.Emit(OpCodes.Ldstr, name);
                }
                else
                {
                    var argType = _mainEmitter.Emit(e.Args[0].Value);
                    TypeMapper.EmitBox(IL, argType);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToStr_Method);
                }

                IL.Emit(OpCodes.Newobj, ctor);
                return NajaTypes.Unknown;
            }

            // User-defined function
            if (_ctx.Methods.TryGetValue(name, out var method))
            {
                _ctx.MethodParamTypes.TryGetValue(name, out var pts);
                int totalParams = pts?.Length ?? 0;

                // Get the FunctionDef to access parameter defaults
                _ctx.FunctionDefs.TryGetValue(name, out var functionDef);

                // *args / variadic call: more positional args than declared params.
                // Pack the excess args (from index totalParams-1 onward) into an object[]
                // so the star-param receives a single iterable value — matches Python semantics.
                if (pts != null && e.Args.Count > totalParams && totalParams > 0)
                {
                    int starStart = totalParams - 1;
                    // Emit leading (non-star) args normally
                    for (int i = 0; i < starStart; i++)
                    {
                        var argType = _mainEmitter.Emit(e.Args[i].Value);
                        if (pts[i] == typeof(object)) TypeMapper.EmitBox(IL, argType);
                    }
                    // Pack remaining args into object[]
                    int packCount = e.Args.Count - starStart;
                    IL.Emit(OpCodes.Ldc_I4, packCount);
                    IL.Emit(OpCodes.Newarr, typeof(object));
                    for (int i = starStart; i < e.Args.Count; i++)
                    {
                        IL.Emit(OpCodes.Dup);
                        IL.Emit(OpCodes.Ldc_I4, i - starStart);
                        var argType = _mainEmitter.Emit(e.Args[i].Value);
                        TypeMapper.EmitBox(IL, argType);
                        IL.Emit(OpCodes.Stelem_Ref);
                    }
                    // object[] is the star-param value (iterable for for-loops)
                }
                else
                {
                    for (int i = 0; i < e.Args.Count; i++)
                    {
                        var argType = _mainEmitter.Emit(e.Args[i].Value);
                        if (pts is not null && i < pts.Length && pts[i] == typeof(object))
                            TypeMapper.EmitBox(IL, argType);
                    }
                    // Determine where captured-cell params begin in the param list
                    _ctx.FunctionCapturedCells.TryGetValue(name, out var capturedCells);
                    int cellParamStart = capturedCells is { Count: > 0 }
                        ? totalParams - capturedCells.Count
                        : totalParams;

                    for (int i = e.Args.Count; i < totalParams; i++)
                    {
                        if (i >= cellParamStart && capturedCells is not null)
                        {
                            // Inject the object[] cell reference for this captured variable
                            var cv = capturedCells[i - cellParamStart];
                            if (_ctx.CellLocals.TryGetValue(cv, out var cellLoc))
                                IL.Emit(OpCodes.Ldloc, cellLoc);
                            else if (_ctx.CellParamOf.TryGetValue(cv, out var cellParamName))
                                _ctx.TryEmitLoadParam(cellParamName);
                            else
                                IL.Emit(OpCodes.Ldnull);
                        }
                        else
                        {
                            // Check if function has a corresponding parameter with a default
                            bool foundDefault = false;
                            if (functionDef is not null && i < functionDef.Params.Count && functionDef.Params[i].Default is not null)
                            {
                                var argType = _mainEmitter.Emit(functionDef.Params[i].Default);
                                TypeMapper.EmitBox(IL, argType);
                                foundDefault = true;
                            }

                            if (!foundDefault)
                            {
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
                        }
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

                for (int i = 0; i < e.Args.Count; i++)
                {
                    var argType = _mainEmitter.Emit(e.Args[i].Value);
                    TypeMapper.EmitBox(IL, argType);
                }

                IL.Emit(OpCodes.Newobj, defaultCtor);
                return NajaTypes.Unknown;
            }

            // .NET imported type instantiation
            if (_ctx.ImportMap.ContainsKey(name))
            {
                _mainEmitter.Emit(e.Func);
                IL.Emit(OpCodes.Castclass, typeof(Type));
                var typeLocal = _ctx.Locals.Declare($"__dotnet_t_{e.Line}_{e.Column}", typeof(Type));
                IL.Emit(OpCodes.Stloc, typeLocal);

                IL.Emit(OpCodes.Ldc_I4, e.Args.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < e.Args.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    var argType = _mainEmitter.Emit(e.Args[i].Value);
                    TypeMapper.EmitBox(IL, argType);
                    IL.Emit(OpCodes.Stelem_Ref);
                }

                var ctorArgs = _ctx.Locals.Declare($"__dotnet_args_{e.Line}_{e.Column}", typeof(object[]));
                IL.Emit(OpCodes.Stloc, ctorArgs);

                IL.Emit(OpCodes.Ldloc, typeLocal);
                IL.Emit(OpCodes.Ldloc, ctorArgs);
                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CreateDotNet_Method);
                return NajaTypes.Unknown;
            }
        }

        // Namespace.Type constructor call
        if (e.Func is AttributeExpr { Object: NameExpr nsName } attr &&
            _ctx.NamespaceImports.ContainsKey(nsName.Name))
        {
            var fullTypeName = nsName.Name + "." + attr.Attribute;

            var resolvedType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(fullTypeName, throwOnError: false, ignoreCase: true))
                .FirstOrDefault(t => t is not null)
                ?? Type.GetType(fullTypeName, throwOnError: false, ignoreCase: true);

            if (resolvedType is not null)
            {
                IL.Emit(OpCodes.Ldc_I4, e.Args.Count);
                IL.Emit(OpCodes.Newarr, typeof(object));
                for (int i = 0; i < e.Args.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    var argType = _mainEmitter.Emit(e.Args[i].Value);
                    TypeMapper.EmitBox(IL, argType);
                    IL.Emit(OpCodes.Stelem_Ref);
                }

                var ctorArgs = _ctx.Locals.Declare($"__dotnet_args_{e.Line}_{e.Column}", typeof(object[]));
                IL.Emit(OpCodes.Stloc, ctorArgs);

                IL.Emit(OpCodes.Ldtoken, resolvedType);
                var getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle")!;
                IL.Emit(OpCodes.Call, getTypeFromHandle);

                IL.Emit(OpCodes.Ldloc, ctorArgs);
                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CreateDotNet_Method);
                return NajaTypes.Unknown;
            }
        }

        // Method call: obj.method(args)
        if (e.Func is AttributeExpr attr2)
            return EmitMethodCall(attr2, e.Args);

        // First-class callable
        {
            IL.Emit(OpCodes.Ldc_I4, e.Args.Count);
            IL.Emit(OpCodes.Newarr, typeof(object));
            for (int i = 0; i < e.Args.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                var argT = _mainEmitter.Emit(e.Args[i].Value);
                TypeMapper.EmitBox(IL, argT);
                IL.Emit(OpCodes.Stelem_Ref);
            }

            var argsLocal = _ctx.Locals.Declare($"__h5_args_{e.Line}", typeof(object[]));
            IL.Emit(OpCodes.Stloc, argsLocal);

            var funcType = _mainEmitter.Emit(e.Func);
            TypeMapper.EmitBox(IL, funcType);
            IL.Emit(OpCodes.Ldloc, argsLocal);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CallCallable_Method);
            return NajaTypes.Unknown;
        }
    }

    public NajaType EmitBuiltinCall(MethodInfo method, IReadOnlyList<Argument> args, string name)
    {
        var parameters = method.GetParameters();

        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(object[]))
        {
            IL.Emit(OpCodes.Ldc_I4, args.Count);
            IL.Emit(OpCodes.Newarr, typeof(object));

            for (int i = 0; i < args.Count; i++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, i);
                var argType = _mainEmitter.Emit(args[i].Value);
                TypeMapper.EmitBox(IL, argType);
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }
        else
        {
            for (int i = 0; i < Math.Min(args.Count, parameters.Length); i++)
            {
                var argType = _mainEmitter.Emit(args[i].Value);
                if (parameters[i].ParameterType == typeof(object))
                    TypeMapper.EmitBox(IL, argType);
            }
        }

        IL.Emit(method.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, method);

        if (method.ReturnType == typeof(void))
        {
            IL.Emit(OpCodes.Ldnull);
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

    public NajaType EmitMethodCall(AttributeExpr attr, IReadOnlyList<Argument> args)
    {
        // base/super method calls
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
                Type searchType = baseType is TypeBuilder tb ? tb.BaseType ?? typeof(object) : baseType;
                if (searchType is TypeBuilder stb)
                {
                    string searchMethodKey = $"{stb.Name}.{attr.Attribute}";
                    if (_ctx.AllClassMethods.TryGetValue(searchMethodKey, out var smb))
                        candidates = new List<MethodInfo> { smb };
                    else
                        candidates = new List<MethodInfo>();
                }
                else
                {
                    candidates = searchType.GetMethods(flags)
                                         .Where(m => m.Name.Equals(attr.Attribute, StringComparison.OrdinalIgnoreCase))
                                         .Cast<MethodInfo>()
                                         .ToList();
                }
            }

            if (candidates.Count == 0 && attr.Attribute == "__init__")
            {
                // If the base is an uncreated TypeBuilder, GetConstructors() throws
                // NotSupportedException — use the registry instead.
                List<ConstructorInfo> ctors;
                if (baseType is TypeBuilder && _ctx.ClassConstructors.TryGetValue(baseType.Name, out var knownCb))
                    ctors = new List<ConstructorInfo> { knownCb };
                else
                    ctors = baseType.GetConstructors(flags).ToList();

                var ctor = ctors.FirstOrDefault(c =>
                {
                    if (c is ConstructorBuilder && _ctx.ClassCtorArgCounts.TryGetValue(baseType.Name, out var count))
                        return count == args.Count;
                    return c.GetParameters().Length == args.Count;
                }) ?? ctors.FirstOrDefault();

                if (ctor == null)
                    throw new CodeGenException($"[L{attr.Line}:C{attr.Column}] No matching base constructor for '__init__' found on '{baseType.Name}'");

                IL.Emit(OpCodes.Ldarg_0);

                Type[]? pts3 = null;
                if (ctor is ConstructorBuilder && _ctx.ClassCtorArgCounts.TryGetValue(baseType.Name, out var pc))
                    pts3 = Enumerable.Repeat(typeof(object), pc).ToArray();

                var ps = pts3 ?? ctor.GetParameters().Select(p => p.ParameterType).ToArray();

                for (int i = 0; i < args.Count; i++)
                {
                    var t = _mainEmitter.Emit(args[i].Value);
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

            IL.Emit(OpCodes.Ldarg_0);

            Type[]? pts = null;
            if (_ctx.AllClassMethods.TryGetValue(methodKey, out var _))
                _ctx.AllClassMethodParamTypes.TryGetValue(methodKey, out pts);

            var ps2 = pts != null ? null : method.GetParameters();
            int pCount = pts?.Length ?? ps2!.Length;

            for (int i = 0; i < args.Count; i++)
            {
                var t = _mainEmitter.Emit(args[i].Value);
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

            IL.Emit(OpCodes.Call, method);

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

        var objType = _mainEmitter.Emit(attr.Object);

        // String methods
        if (objType is StrType)
        {
            var bridge = ResolveStrMethod(attr.Attribute);
            if (bridge is not null)
            {
                var bridgeParams = bridge.GetParameters();
                // If last parameter is object[] (e.g. StrFormat), pack args into array
                if (bridgeParams.Length >= 2 && bridgeParams[^1].ParameterType == typeof(object[]))
                {
                    int fixedCount = bridgeParams.Length - 2;
                    for (int i = 0; i < fixedCount && i < args.Count; i++)
                    { var t = _mainEmitter.Emit(args[i].Value); TypeMapper.EmitBox(IL, t); }
                    int packStart = fixedCount;
                    IL.Emit(OpCodes.Ldc_I4, Math.Max(0, args.Count - packStart));
                    IL.Emit(OpCodes.Newarr, typeof(object));
                    for (int i = packStart; i < args.Count; i++)
                    {
                        IL.Emit(OpCodes.Dup); IL.Emit(OpCodes.Ldc_I4, i - packStart);
                        var at = _mainEmitter.Emit(args[i].Value); TypeMapper.EmitBox(IL, at);
                        IL.Emit(OpCodes.Stelem_Ref);
                    }
                }
                else
                {
                    foreach (var arg in args) { var t = _mainEmitter.Emit(arg.Value); TypeMapper.EmitBox(IL, t); }
                    var expectedArgs = bridgeParams.Length - 1;
                    for (int i = args.Count; i < expectedArgs; i++) IL.Emit(OpCodes.Ldnull);
                }
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

        // List methods
        if (objType is ListType)
        {
            var bridge = ResolveListMethod(attr.Attribute);
            if (bridge is not null)
            {
                foreach (var arg in args) { var t = _mainEmitter.Emit(arg.Value); TypeMapper.EmitBox(IL, t); }
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

        // Dict methods
        if (objType is DictType)
        {
            var bridge = ResolveDictMethod(attr.Attribute);
            if (bridge is not null)
            {
                foreach (var arg in args) { var t = _mainEmitter.Emit(arg.Value); TypeMapper.EmitBox(IL, t); }
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

        // Dynamic fallback — carries keyword names so DynamicCallKw can bind
        // named args to their matching CLR parameter (json.dumps(..., indent=2),
        // open(..., mode=...) etc.). kwNames[i] is null for positional args.
        bool hasKw = args.Any(a => a.Keyword is not null);
        TypeMapper.EmitBox(IL, objType);
        IL.Emit(OpCodes.Ldstr, attr.Attribute);
        IL.Emit(OpCodes.Ldc_I4, args.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));
        for (int i = 0; i < args.Count; i++)
        {
            IL.Emit(OpCodes.Dup); IL.Emit(OpCodes.Ldc_I4, i);
            var at = _mainEmitter.Emit(args[i].Value); TypeMapper.EmitBox(IL, at);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        if (hasKw)
        {
            IL.Emit(OpCodes.Ldc_I4, args.Count);
            IL.Emit(OpCodes.Newarr, typeof(string));
            for (int i = 0; i < args.Count; i++)
            {
                IL.Emit(OpCodes.Dup); IL.Emit(OpCodes.Ldc_I4, i);
                var kw = args[i].Keyword;
                if (kw is null) IL.Emit(OpCodes.Ldnull);
                else IL.Emit(OpCodes.Ldstr, kw);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            IL.Emit(OpCodes.Call, typeof(Builtins.ReflectionHelpers).GetMethod(nameof(Builtins.ReflectionHelpers.DynamicCallKw))!);
        }
        else
        {
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.DynamicCall_Method);
        }
        return NajaTypes.Unknown;
    }

    private static MethodInfo? ResolveStrMethod(string name) => name switch
    {
        "upper" => NajaBuiltinsMethodCache.StrUpper_Method,
        "lower" => NajaBuiltinsMethodCache.StrLower_Method,
        "strip" => NajaBuiltinsMethodCache.StrStrip_Method,
        "lstrip" => NajaBuiltinsMethodCache.StrLStrip_Method,
        "rstrip" => NajaBuiltinsMethodCache.StrRStrip_Method,
        "startswith" => NajaBuiltinsMethodCache.StrStartsWith_Method,
        "endswith" => NajaBuiltinsMethodCache.StrEndsWith_Method,
        "isdigit" => NajaBuiltinsMethodCache.StrIsDigit_Method,
        "isalpha" => NajaBuiltinsMethodCache.StrIsAlpha_Method,
        "isalnum" => NajaBuiltinsMethodCache.StrIsAlNum_Method,
        "find" => NajaBuiltinsMethodCache.StrFind_Method,
        "index" => NajaBuiltinsMethodCache.StrIndex_Method,
        "replace" => NajaBuiltinsMethodCache.StrReplace_Method,
        "center" => NajaBuiltinsMethodCache.StrCenter_Method,
        "ljust" => NajaBuiltinsMethodCache.StrLJust_Method,
        "rjust" => NajaBuiltinsMethodCache.StrRJust_Method,
        "zfill" => NajaBuiltinsMethodCache.StrZFill_Method,
        "count" => NajaBuiltinsMethodCache.StrCount_Method,
        "join" => NajaBuiltinsMethodCache.StrJoin_Method,
        "split" => NajaBuiltinsMethodCache.StrSplit_Method,
        "splitlines" => NajaBuiltinsMethodCache.StrSplitLines_Method,
        "title" => NajaBuiltinsMethodCache.StrTitle_Method,
        "encode" => NajaBuiltinsMethodCache.StrEncode_Method,
        "decode" => typeof(Naja.CodeGen.Builtins.StringFunctions).GetMethod("StrDecode")!,
        "format" => NajaBuiltinsMethodCache.StrFormat_Method,
        _ => null
    };

    private static MethodInfo? ResolveListMethod(string name) => name switch
    {
        "append" => NajaBuiltinsMethodCache.ListAppend_Method,
        "extend" => NajaBuiltinsMethodCache.ListExtend_Method,
        "insert" => NajaBuiltinsMethodCache.ListInsert_Method,
        "pop" => NajaBuiltinsMethodCache.ListPop_Method,
        "remove" => NajaBuiltinsMethodCache.ListRemove_Method,
        "reverse" => NajaBuiltinsMethodCache.ListReverse_Method,
        "sort" => NajaBuiltinsMethodCache.ListSort_Method,
        "index" => NajaBuiltinsMethodCache.ListIndex_Method,
        "count" => NajaBuiltinsMethodCache.ListCount_Method,
        "copy" => NajaBuiltinsMethodCache.ListCopy_Method,
        "clear" => NajaBuiltinsMethodCache.ListClear_Method,
        _ => null
    };

    private static MethodInfo? ResolveDictMethod(string name) => name switch
    {
        "keys" => NajaBuiltinsMethodCache.DictKeys_Method,
        "values" => NajaBuiltinsMethodCache.DictValues_Method,
        "items" => NajaBuiltinsMethodCache.DictItems_Method,
        "get" => NajaBuiltinsMethodCache.DictGet_Method,
        "pop" => NajaBuiltinsMethodCache.DictPop_Method,
        "update" => NajaBuiltinsMethodCache.DictUpdate_Method,
        "clear" => NajaBuiltinsMethodCache.DictClear_Method,
        "copy" => NajaBuiltinsMethodCache.DictCopy_Method,
        _ => null
    };

    /// <summary>
    /// Not implemented in this emitter - use specialized methods: EmitCall, EmitBuiltinCall, EmitMethodCall
    /// </summary>
    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException("Use specialized methods: EmitCall, EmitBuiltinCall, EmitMethodCall");
    }

    // Helper methods (referenced from inherited code)
    private string DescribeExpr(Expression expr) =>
        expr switch
        {
            NameExpr n => n.Name,
            IntLiteral i => i.Value.ToString(),
            StringLiteral s => $"\"{s.Value}\"",
            _ => expr.GetType().Name
        };

    private NajaType ParseCastTarget(Expression expr, int line, int col) =>
        expr switch
        {
            NameExpr { Name: "int" } => NajaTypes.Int,
            NameExpr { Name: "float" } => NajaTypes.Float,
            NameExpr { Name: "str" } => NajaTypes.Str,
            NameExpr { Name: "bool" } => NajaTypes.Bool,
            NameExpr { Name: "object" } => NajaTypes.Unknown,
            _ => throw new CodeGenException($"Unknown cast target: {expr}", line, col)
        };

    private void EmitDefaultValue(object? val)
    {
        if (val == null) IL.Emit(OpCodes.Ldnull);
        else if (val is long l) { IL.Emit(OpCodes.Ldc_I8, l); }
        else if (val is int i) { IL.Emit(OpCodes.Ldc_I4, i); }
        else if (val is double d) { IL.Emit(OpCodes.Ldc_R8, d); }
        else if (val is bool b) { IL.Emit(OpCodes.Ldc_I4, b ? 1 : 0); }
        else if (val is string s) { IL.Emit(OpCodes.Ldstr, s); }
        else IL.Emit(OpCodes.Ldnull);
    }

    private void EmitCoercion(ILGenerator il, NajaType from, NajaType to, int line, int col)
    {
        if (from == to) return;
        if (from is IntType && to is FloatType) IL.Emit(OpCodes.Conv_R8);
        else if (from is FloatType && to is IntType) IL.Emit(OpCodes.Conv_I8);
        else if (to is UnknownType) TypeMapper.EmitBox(IL, from);
        else if (from is UnknownType) IL.Emit(OpCodes.Unbox_Any, TypeMapper.ToClrType(to));
    }
}

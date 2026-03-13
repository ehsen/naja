namespace Naja.CodeGen;

/// <summary>
/// Python built-in functions implemented in C#.
/// These are called directly from emitted IL via static call opcodes.
/// All methods must be public static — the IL emitter uses reflection to find them.
/// </summary>
public static class NajaBuiltins
{
    // ── Dynamic arithmetic (for untyped / object parameters) ──────────────────
    // Fix 10: Add dunder method dispatch before numeric fallback

    public static object DynamicAdd(object a, object b)
    {
        if (a is string sa && b is string sb) return sa + sb;
        
        // Try __add__ on left operand
        var addM = a?.GetType().GetMethod("__add__", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (addM is not null) 
        {
            try
            {
                var result = addM.Invoke(a, new[] { b });
                if (result == null) throw new Exception("TypeError: __add__ returned None");
                return result;
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }
        
        // Try __radd__ on right operand
        var raddM = b?.GetType().GetMethod("__radd__", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (raddM is not null) 
        {
            try
            {
                var result = raddM.Invoke(b, new[] { a });
                if (result == null) throw new Exception("TypeError: __radd__ returned None");
                return result;
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }
        
        // Only convert to numeric if both operands are IConvertible (primitives)
        if (a is IConvertible && b is IConvertible)
        {
            if (a is double || b is double)
                return Convert.ToDouble(a) + Convert.ToDouble(b);
            return Convert.ToInt64(a) + Convert.ToInt64(b);
        }
        
        throw new InvalidOperationException($"Unsupported operand types for +: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    public static object DynamicSub(object a, object b)
    {
        // Try __sub__ on left operand
        var subM = a?.GetType().GetMethod("__sub__");
        if (subM is not null) 
            return subM.Invoke(a, new[] { b }) ?? throw new Exception("TypeError: __sub__ returned None");
        
        // Try __rsub__ on right operand
        var rsubM = b?.GetType().GetMethod("__rsub__");
        if (rsubM is not null) 
            return rsubM.Invoke(b, new[] { a }) ?? throw new Exception("TypeError: __rsub__ returned None");
        
        // Only convert to numeric if both operands are IConvertible (primitives)
        if (a is IConvertible && b is IConvertible)
        {
            if (a is double || b is double)
                return Convert.ToDouble(a) - Convert.ToDouble(b);
            return Convert.ToInt64(a) - Convert.ToInt64(b);
        }
        
        throw new InvalidOperationException($"Unsupported operand types for -: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    public static object DynamicMul(object a, object b)
    {
        // String repetition: "A" * 3 → "AAA"
        if (a is string sa && b is long nb)
            return string.Concat(System.Linq.Enumerable.Repeat(sa, (int)nb));
        if (b is string sb && a is long na)
            return string.Concat(System.Linq.Enumerable.Repeat(sb, (int)na));

        // Try __mul__ on left operand
        var mulM = a?.GetType().GetMethod("__mul__");
        if (mulM is not null) 
            return mulM.Invoke(a, new[] { b }) ?? throw new Exception("TypeError: __mul__ returned None");
        
        // Try __rmul__ on right operand
        var rmulM = b?.GetType().GetMethod("__rmul__");
        if (rmulM is not null) 
            return rmulM.Invoke(b, new[] { a }) ?? throw new Exception("TypeError: __rmul__ returned None");
        
        // Only convert to numeric if both operands are IConvertible (primitives)
        if (a is IConvertible && b is IConvertible)
        {
            if (a is double || b is double)
                return Convert.ToDouble(a) * Convert.ToDouble(b);
            return Convert.ToInt64(a) * Convert.ToInt64(b);
        }
        
        throw new InvalidOperationException($"Unsupported operand types for *: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    public static object DynamicMod(object a, object b)
    {
        // Try __mod__ on left operand
        var modM = a?.GetType().GetMethod("__mod__");
        if (modM is not null) 
            return modM.Invoke(a, new[] { b }) ?? throw new Exception("TypeError: __mod__ returned None");
        
        // Try __rmod__ on right operand
        var rmodM = b?.GetType().GetMethod("__rmod__");
        if (rmodM is not null) 
            return rmodM.Invoke(b, new[] { a }) ?? throw new Exception("TypeError: __rmod__ returned None");
        
        // Only convert to numeric if both operands are IConvertible (primitives)
        if (a is IConvertible && b is IConvertible)
        {
            if (a is double || b is double)
                return PyModF(Convert.ToDouble(a), Convert.ToDouble(b));
            return (object)PyMod(Convert.ToInt64(a), Convert.ToInt64(b));
        }
        
        throw new InvalidOperationException($"Unsupported operand types for %: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    /// <summary>Python floor division: floors toward -∞ (unlike C# / which truncates toward zero).</summary>
    public static long PyFloorDiv(long a, long b)
    {
        var q = a / b;
        // If the remainder is non-zero and signs differ, floor by subtracting 1
        if ((a ^ b) < 0 && q * b != a) q--;
        return q;
    }

    /// <summary>Python floor division for floats: Math.Floor(a / b).</summary>
    public static double PyFloorDivF(double a, double b) => Math.Floor(a / b);

    /// <summary>Python modulo: result sign always matches divisor (unlike C# % which matches dividend).</summary>
    public static long PyMod(long a, long b)
    {
        var r = a % b;
        if (r != 0 && ((r ^ b) < 0)) r += b;
        return r;
    }

    /// <summary>Python modulo for floats.</summary>
    public static double PyModF(double a, double b)
    {
        var r = a % b;
        if (r != 0.0 && ((r < 0) != (b < 0))) r += b;
        return r;
    }

    // ── Dynamic comparisons ───────────────────────────────────────────────────

    private static bool IsNumeric(object? obj) =>
        obj is long || obj is int || obj is short || obj is byte || obj is double || obj is float;
    private static object? UnwrapEnum(object? obj)
    {
        if (obj is null) return null;
        var t = obj.GetType();
        if (t.IsEnum)
            return Convert.ChangeType(obj, Enum.GetUnderlyingType(t));
        return obj;
    }
    [System.Security.SecuritySafeCritical]
    public static bool DynamicEq(object? a, object? b)
    {
        // Null / reference identity fast-paths.
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        // Unwrap enums to their underlying numeric values for comparison
        // This allows comparing DockStyle.Fill (enum) with 5 (int)
        a = UnwrapEnum(a);
        b = UnwrapEnum(b);

        // Fast path for primitive primitives.
        if (a is string sa && b is string sb) return sa == sb;
        if (a is long la && b is long lb) return la == lb;
        if (a is double da && b is double db) return da.Equals(db);
        if (a is bool ba && b is bool bb) return ba == bb;
        if (a is int ia && b is int ib) return ia == ib;

        // Python container structural equality.
        if (a is System.Collections.Generic.List<object> l1 &&
            b is System.Collections.Generic.List<object> l2)
        {
            if (l1.Count != l2.Count) return false;
            for (int i = 0; i < l1.Count; i++)
                if (!DynamicEq(l1[i], l2[i])) return false;
            return true;
        }

        if (a is object[] t1 && b is object[] t2)
        {
            if (t1.Length != t2.Length) return false;
            for (int i = 0; i < t1.Length; i++)
                if (!DynamicEq(t1[i], t2[i])) return false;
            return true;
        }

        if (a is System.Collections.Generic.Dictionary<object, object> d1 &&
            b is System.Collections.Generic.Dictionary<object, object> d2)
        {
            if (d1.Count != d2.Count) return false;
            foreach (var kv in d1)
            {
                if (!d2.TryGetValue(kv.Key, out var v2)) return false;
                if (!DynamicEq(kv.Value, v2)) return false;
            }
            return true;
        }

        if (a is System.Collections.Generic.HashSet<object> s1 &&
            b is System.Collections.Generic.HashSet<object> s2)
        {
            if (s1.Count != s2.Count) return false;
            return s1.SetEquals(s2);
        }

        // Numeric-but-not-primitive cases (e.g. mixing int/long/short/byte or
        // float/double). Only treat obvious primitive-like values as numbers.
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a is double || a is float || b is double || b is float)
                return Convert.ToDouble(a).Equals(Convert.ToDouble(b));

            return Convert.ToInt64(a) == Convert.ToInt64(b);
        }

        // Fallback: rely on normal CLR virtual equality. For Naja classes that
        // define __eq__, AssemblyEmitter wires that up to override
        // object.Equals(object), so this calls the Python-level __eq__.
        return object.Equals(a, b);
    }

    public static bool DynamicNotEq(object? a, object? b) => !DynamicEq(a, b);
    public static bool DynamicLt(object? a, object? b)
    {
        if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb) < 0;
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a is double || b is double || a is float || b is float) return Convert.ToDouble(a) < Convert.ToDouble(b);
            return Convert.ToInt64(a) < Convert.ToInt64(b);
        }
        throw new InvalidOperationException($"Cannot compare {a?.GetType().Name} and {b?.GetType().Name}");
    }

    public static bool DynamicLtEq(object? a, object? b)
    {
        if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb) <= 0;
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a is double || b is double || a is float || b is float) return Convert.ToDouble(a) <= Convert.ToDouble(b);
            return Convert.ToInt64(a) <= Convert.ToInt64(b);
        }
        throw new InvalidOperationException($"Cannot compare {a?.GetType().Name} and {b?.GetType().Name}");
    }

    public static bool DynamicGt(object? a, object? b)
    {
        if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb) > 0;
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a is double || b is double || a is float || b is float) return Convert.ToDouble(a) > Convert.ToDouble(b);
            return Convert.ToInt64(a) > Convert.ToInt64(b);
        }
        throw new InvalidOperationException($"Cannot compare {a?.GetType().Name} and {b?.GetType().Name}");
    }

    public static bool DynamicGtEq(object? a, object? b)
    {
        if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb) >= 0;
        if (IsNumeric(a) && IsNumeric(b))
        {
            if (a is double || b is double || a is float || b is float) return Convert.ToDouble(a) >= Convert.ToDouble(b);
            return Convert.ToInt64(a) >= Convert.ToInt64(b);
        }
        throw new InvalidOperationException($"Cannot compare {a?.GetType().Name} and {b?.GetType().Name}");
    }

    // ── Dynamic method dispatch ───────────────────────────────────────────────

    public static object? DynamicCall(object obj, string method, object[] args)
    {
        if (obj is null) throw new Exception($"AttributeError: NoneType has no method '{method}'");

        // .NET static call: obj is System.Type → invoke static method
        if (obj is Type type)
            return StaticCall(type, method, args);

        string? bridgeName = null;
        if (obj is string) bridgeName = "Str" + method;
        else if (obj is System.Collections.Generic.List<object>) bridgeName = "List" + method;
        else if (obj is System.Collections.Generic.Dictionary<object, object>) bridgeName = "Dict" + method;

        if (bridgeName is not null)
        {
            var bridgeM = typeof(NajaBuiltins).GetMethod(bridgeName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);

            if (bridgeM is not null)
            {
                var pParams = bridgeM.GetParameters();
                var invokeArgs = new object[pParams.Length];
                invokeArgs[0] = obj;
                for (int i = 1; i < invokeArgs.Length; i++)
                    invokeArgs[i] = Type.Missing;

                for (int i = 0; i < args.Length && i + 1 < invokeArgs.Length; i++)
                    invokeArgs[i + 1] = args[i];
                try
                {
                    return bridgeM.Invoke(null, invokeArgs);
                }
                catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                    throw; // unreachable
                }
            }
        }

        var t = obj.GetType();
        var flags2 = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase;
        var candidates2 = t.GetMethods(flags2)
            .Where(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase))
            .Cast<System.Reflection.MethodBase>();

        if (!TryBindBestCallable(candidates2, args, out var mb, out var boundArgs))
            throw new Exception($"AttributeError: '{t.Name}' object has no method '{method}' matching {args.Length} argument(s)");

        var mi = (System.Reflection.MethodInfo)mb;
        try { return mi.Invoke(obj, boundArgs); }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Invoke a static method on a .NET type (used for "from System.X import Y" then Y.Method(args)).</summary>
    public static object? StaticCall(Type type, string methodName, object[] args)
    {
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase;
        var candidates = type.GetMethods(flags)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));

        if (!TryBindBestCallable(candidates, args, out var method, out var boundArgs))
            throw new Exception($"AttributeError: type '{type.FullName}' has no static method '{methodName}' matching {args.Length} argument(s)");

        try { return method.Invoke(null, boundArgs); }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Create a .NET object from a <see cref="Type"/> and Python values.</summary>
    public static object? CreateDotNet(Type type, object[] args)
    {
        if (type is null)
            throw new TypeLoadException(
                "Cannot instantiate type: the imported assembly was not found at runtime. " +
                "Ensure all referenced assemblies are present next to the executable.");

        // Enum constructor syntax: FontStyle(1) → (FontStyle)1
        // .NET enums have no constructors; convert the numeric arg instead.
        if (type.IsEnum)
        {
            if (args.Length == 0)
                return Enum.ToObject(type, 0);
            if (args.Length == 1)
                return Enum.ToObject(type, Convert.ToInt64(args[0]));
            throw new ArgumentException(
                $"Enum type '{type.FullName}' accepts 0 or 1 argument, got {args.Length}");
        }

        var ctors = type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var candidates = ctors.Cast<System.Reflection.MethodBase>();

        if (!TryBindBestCallable(candidates, args, out var ctor, out var boundArgs))
        {
            var available = string.Join(", ", ctors.Select(c =>
                "(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name)) + ")"));
            throw new MissingMethodException(
                $"Constructor on type '{type.FullName}' not found for {args.Length} arg(s). " +
                $"Available: [{available}]. Args: [{string.Join(", ", args.Select(a => a?.GetType().Name ?? "null"))}]");
        }

        try { return ((System.Reflection.ConstructorInfo)ctor).Invoke(boundArgs); }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    private static bool TryBindBestCallable(
        IEnumerable<System.Reflection.MethodBase> candidates,
        object[] args,
        out System.Reflection.MethodBase best,
        out object?[] bestArgs)
    {
        best = null!;
        bestArgs = Array.Empty<object?>();

        var bestScore = int.MinValue;
        foreach (var c in candidates)
        {
            var ps = c.GetParameters();
            bool hasParams = ps.Length > 0 && ps[ps.Length - 1].GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0;
            
            if (args.Length > ps.Length && !hasParams) continue;
            
            int requiredParams = ps.Count(p => !p.IsOptional && p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length == 0);
            if (args.Length < requiredParams) continue;

            var bound = new object?[ps.Length];
            var score = 0;
            var ok = true;

            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                bool isParams = hasParams && i == ps.Length - 1;

                if (i < args.Length)
                {
                    if (isParams)
                    {
                        var elementType = p.ParameterType.GetElementType()!;
                        int paramsCount = args.Length - i;
                        
                        // Check if the user passed an array directly
                        if (paramsCount == 1 && args[i] != null && p.ParameterType.IsInstanceOfType(args[i]))
                        {
                            bound[i] = args[i];
                            score += 3;
                        }
                        else
                        {
                            var paramsArray = Array.CreateInstance(elementType, paramsCount);
                            bool paramsOk = true;
                            for (int j = 0; j < paramsCount; j++)
                            {
                                if (TryConvertArg(args[i + j], elementType, false, out var pConv, out var pScore))
                                {
                                    paramsArray.SetValue(pConv, j);
                                    score += pScore;
                                }
                                else
                                {
                                    paramsOk = false;
                                    break;
                                }
                            }
                            if (!paramsOk)
                            {
                                ok = false;
                                break;
                            }
                            bound[i] = paramsArray;
                        }
                    }
                    else
                    {
                        if (!TryConvertArg(args[i], p.ParameterType, p.IsOut, out var converted, out var s))
                        {
                            ok = false;
                            break;
                        }
                        bound[i] = converted;
                        score += s;
                    }
                }
                else
                {
                    if (isParams)
                    {
                        bound[i] = Array.CreateInstance(p.ParameterType.GetElementType()!, 0);
                        score += 0;
                    }
                    else if (p.IsOptional)
                    {
                        bound[i] = p.HasDefaultValue ? p.DefaultValue : Type.Missing;
                        score += 0;
                    }
                    else
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (!ok) continue;
            if (ps.Length == args.Length && !hasParams) score += 10; // Boost exact signature match

            if (score > bestScore)
            {
                bestScore = score;
                best = c;
                bestArgs = bound;
            }
        }

        return bestScore != int.MinValue;
    }

    private static bool TryConvertArg(object? value, Type targetType, bool isOut, out object? converted, out int score)
    {
        converted = null;
        score = 0;
        
        var isByRef = targetType.IsByRef;
        var nnTarget = isByRef ? targetType.GetElementType()! : targetType;
        nnTarget = Nullable.GetUnderlyingType(nnTarget) ?? nnTarget;

        if (isOut)
        {
            converted = nnTarget.IsValueType ? Activator.CreateInstance(nnTarget) : null;
            score = 1;
            return true;
        }

        // Null handling
        if (value is null)
        {
            if (!nnTarget.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
            {
                score = 1;
                return true;
            }
            return false;
        }

        // Exact / assignable
        if (nnTarget.IsInstanceOfType(value))
        {
            converted = value;
            score = 3;
            return true;
        }

        // FIX: Prevent primitive numeric to string conversion in constructor/method binding.
        // This fixes issues like Bitmap(int(100), int(100)) incorrectly matching
        // Bitmap(string filename, bool useIcm) because long->string conversion was allowed.
        bool valueIsNumeric = value is long || value is int || value is short || value is byte ||
                              value is double || value is float || value is decimal;
        bool targetIsString = nnTarget == typeof(string);
        bool targetIsNumeric = nnTarget.IsPrimitive && (nnTarget != typeof(bool) && nnTarget != typeof(char));
        
        // Numeric values should NOT convert to strings during constructor/method resolution
        if (valueIsNumeric && targetIsString)
            return false;
        
        // Strings should NOT convert to numbers during constructor/method resolution
        // (parse failures are expensive and usually indicate wrong overload)
        if (value is string && targetIsNumeric)
            return false;

        // Delegate conversion (e.g. Func<object, object> -> Action<object, EventArgs>)
        if (nnTarget.IsSubclassOf(typeof(Delegate)) && value is Delegate d)
        {
            var invoke = nnTarget.GetMethod("Invoke");
            if (invoke != null)
            {
                var invokeParams = invoke.GetParameters();
                
                // Check if any parameter types or return type contain generic parameters
                // If so, we cannot create a lambda expression and should skip this conversion
                bool hasOpenGenericParams = invokeParams.Any(p => p.ParameterType.ContainsGenericParameters) 
                    || invoke.ReturnType.ContainsGenericParameters;
                
                if (hasOpenGenericParams)
                {
                    // Cannot convert to delegate with open generic parameters
                    // Fall through to other conversion attempts
                }
                else
                {
                    var paramExprs = invokeParams
                        .Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name))
                        .ToArray();

                    var argsArray = System.Linq.Expressions.Expression.NewArrayInit(
                        typeof(object),
                        paramExprs.Select(p => System.Linq.Expressions.Expression.Convert(p, typeof(object))));

                    var miInvoke = typeof(Delegate).GetMethod("DynamicInvoke", new[] { typeof(object[]) })!;

                    var call = System.Linq.Expressions.Expression.Call(
                        System.Linq.Expressions.Expression.Constant(d),
                        miInvoke,
                        argsArray);

                    var body = invoke.ReturnType == typeof(void)
                        ? (System.Linq.Expressions.Expression)call
                        : System.Linq.Expressions.Expression.Convert(call, invoke.ReturnType);

                    converted = System.Linq.Expressions.Expression.Lambda(nnTarget, body, paramExprs).Compile();
                    score = 1;
                    return true;
                }
            }
        }

        // Enums (e.g. FormStartPosition, ScrollBars, SelectionMode)
        if (nnTarget.IsEnum)
        {
            if (value is string s)
            {
                try { converted = Enum.Parse(nnTarget, s, ignoreCase: true); score = 2; return true; }
                catch { /* not a named member — fall through */ }
            }
            if (value is IConvertible)
            {
                try
                {
                    var underlying = Enum.GetUnderlyingType(nnTarget);
                    var raw = Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
                    converted = Enum.ToObject(nnTarget, raw!);
                    score = 2;
                    return true;
                }
                catch { /* fall through to primitive path */ }
            }
            // Direct cast from long for enum (common case: Naja emits all ints as long)
            if (value is long lv)
            {
                try { converted = Enum.ToObject(nnTarget, (int)lv); score = 2; return true; }
                catch { return false; }
            }
        }

        // Primitive conversions (int64 → int32, etc.)
        try
        {
            if (value is IConvertible &&
                (nnTarget.IsPrimitive || nnTarget == typeof(decimal) || nnTarget == typeof(string)))
            {
                converted = Convert.ChangeType(value, nnTarget, System.Globalization.CultureInfo.InvariantCulture);
                score = 2;
                return true;
            }
        }
        catch { /* ignore */ }

        return false;
    }

    /// <summary>Add a .NET event handler by method name (e.g. Click += self.handle_exit).</summary>
    public static void AddEventHandler(object target, string eventName, object? handlerTarget, string handlerMethodName)
    {
        if (target is null) throw new Exception($"AttributeError: cannot add handler to null event '{eventName}'");

        var t = target.GetType();
        var ev = t.GetEvent(eventName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (ev is null)
            throw new Exception($"AttributeError: '{t.Name}' object has no event '{eventName}'");

        var handlerType = ev.EventHandlerType
            ?? throw new Exception($"AttributeError: event '{eventName}' on '{t.Name}' has no handler type");

        // Handle module-level static methods (handlerTarget is Type)
        object? resolvedTarget = handlerTarget;
        System.Reflection.MethodInfo? methodToCallOverride = null;
        Type methodLookupType;
        System.Reflection.BindingFlags methodLookupFlags;
        
        if (handlerTarget is Type moduleType)
        {
            // Module-level static method — look it up by name on the type
            methodLookupType = moduleType;
            methodLookupFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
                                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase;
            resolvedTarget = null;   // static — no instance needed
        }
        else
        {
            if (handlerTarget is null)
                throw new Exception($"TypeError: event handler target is null for '{eventName}' (expected something like self.method)");
            methodLookupType = handlerTarget.GetType();
            methodLookupFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase;
        }

        // First try direct delegate binding (fast path if signatures match)
        try
        {
            if (resolvedTarget is not null)
            {
                var direct = Delegate.CreateDelegate(handlerType, resolvedTarget, handlerMethodName, ignoreCase: true);
                if (direct is not null)
                {
                    ev.AddEventHandler(target, direct);
                    return;
                }
            }
            else
            {
                // Static method direct binding
                var direct = Delegate.CreateDelegate(handlerType, methodLookupType.GetMethod(handlerMethodName, methodLookupFlags) ?? 
                    methodLookupType.GetMethods(methodLookupFlags).FirstOrDefault(m => 
                        string.Equals(m.Name, handlerMethodName, StringComparison.OrdinalIgnoreCase)));
                if (direct is not null)
                {
                    ev.AddEventHandler(target, direct);
                    return;
                }
            }
        }
        catch (ArgumentException)
        {
            // fall through to wrapper
        }

        // Fallback: build a wrapper delegate that invokes the target method via reflection.
        // This allows Python methods (returning object, taking object params) to handle .NET events.
        var invoke = handlerType.GetMethod("Invoke")
            ?? throw new Exception($"TypeError: cannot inspect delegate type for event '{eventName}'");
        if (invoke.ReturnType != typeof(void))
            throw new Exception($"TypeError: event '{eventName}' delegate return type is not supported");

        var invokeParams = invoke.GetParameters();
        
        // Find the method to call
        System.Reflection.MethodInfo methodToCall;
        if (methodToCallOverride is not null)
        {
            methodToCall = methodToCallOverride;
        }
        else
        {
            methodToCall = methodLookupType
                .GetMethods(methodLookupFlags)
                .FirstOrDefault(m => string.Equals(m.Name, handlerMethodName, StringComparison.OrdinalIgnoreCase) &&
                                     m.GetParameters().Length == invokeParams.Length)
                ?? methodLookupType
                    .GetMethods(methodLookupFlags)
                    .FirstOrDefault(m => string.Equals(m.Name, handlerMethodName, StringComparison.OrdinalIgnoreCase));

            if (methodToCall is null)
                throw new Exception($"TypeError: handler method '{handlerMethodName}' not found");
        }

        var paramExprs = invokeParams
            .Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name))
            .ToArray();

        var argsArray = System.Linq.Expressions.Expression.NewArrayInit(
            typeof(object),
            paramExprs.Select(p => System.Linq.Expressions.Expression.Convert(p, typeof(object))));

        var miInvoke = typeof(System.Reflection.MethodInfo).GetMethod(
            "Invoke", new[] { typeof(object), typeof(object[]) })!;

        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Constant(methodToCall),
            miInvoke,
            System.Linq.Expressions.Expression.Constant(resolvedTarget),
            argsArray);

        var body = System.Linq.Expressions.Expression.Block(call, System.Linq.Expressions.Expression.Empty());
        var del = System.Linq.Expressions.Expression.Lambda(handlerType, body, paramExprs).Compile();

        ev.AddEventHandler(target, del);
    }

    /// <summary>Remove a .NET event handler by method name (e.g. Click -= self.handle_exit).</summary>
    public static void RemoveEventHandler(object target, string eventName, object? handlerTarget, string handlerMethodName)
    {
        if (target is null) throw new Exception($"AttributeError: cannot remove handler from null event '{eventName}'");

        var t = target.GetType();
        var ev = t.GetEvent(eventName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (ev is null)
            throw new Exception($"AttributeError: '{t.Name}' object has no event '{eventName}'");

        var handlerType = ev.EventHandlerType
            ?? throw new Exception($"AttributeError: event '{eventName}' on '{t.Name}' has no handler type");

        // Handle module-level static methods (handlerTarget is Type)
        object? resolvedTarget = handlerTarget;
        Type methodLookupType;
        System.Reflection.BindingFlags methodLookupFlags;
        
        if (handlerTarget is Type moduleType)
        {
            // Module-level static method — look it up by name on the type
            methodLookupType = moduleType;
            methodLookupFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
                                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase;
            resolvedTarget = null;   // static — no instance needed
        }
        else
        {
            if (handlerTarget is null)
                throw new Exception($"TypeError: event handler target is null for '{eventName}' (expected something like self.method)");
            methodLookupType = handlerTarget.GetType();
            methodLookupFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase;
        }

        // Try direct delegate binding (fast path) which also gets structural equality correct for unsubscription
        try
        {
            if (resolvedTarget is not null)
            {
                var direct = Delegate.CreateDelegate(handlerType, resolvedTarget, handlerMethodName, ignoreCase: true);
                if (direct is not null)
                {
                    ev.RemoveEventHandler(target, direct);
                    return;
                }
            }
            else
            {
                // Static method direct binding
                var direct = Delegate.CreateDelegate(handlerType, methodLookupType.GetMethod(handlerMethodName, methodLookupFlags) ?? 
                    methodLookupType.GetMethods(methodLookupFlags).FirstOrDefault(m => 
                        string.Equals(m.Name, handlerMethodName, StringComparison.OrdinalIgnoreCase)));
                if (direct is not null)
                {
                    ev.RemoveEventHandler(target, direct);
                    return;
                }
            }
        }
        catch (ArgumentException)
        {
            // fall through to wrapper mapping
        }

        // Fallback mapping
        var invoke = handlerType.GetMethod("Invoke");
        if (invoke == null || invoke.ReturnType != typeof(void)) return; // silently fail to unsubscribe complex fallbacks as instances won't match

        var invokeParams = invoke.GetParameters();
        var methodToCall = methodLookupType
            .GetMethods(methodLookupFlags)
            .FirstOrDefault(m => string.Equals(m.Name, handlerMethodName, StringComparison.OrdinalIgnoreCase) &&
                                 m.GetParameters().Length == invokeParams.Length)
            ?? methodLookupType
                .GetMethods(methodLookupFlags)
                .FirstOrDefault(m => string.Equals(m.Name, handlerMethodName, StringComparison.OrdinalIgnoreCase));

        if (methodToCall is null) return; // fail gracefully

        var paramExprs = invokeParams
            .Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name))
            .ToArray();

        var argsArray = System.Linq.Expressions.Expression.NewArrayInit(
            typeof(object),
            paramExprs.Select(p => System.Linq.Expressions.Expression.Convert(p, typeof(object))));

        var miInvoke = typeof(System.Reflection.MethodInfo).GetMethod(
            "Invoke", new[] { typeof(object), typeof(object[]) })!;

        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Constant(methodToCall),
            miInvoke,
            System.Linq.Expressions.Expression.Constant(resolvedTarget),
            argsArray);

        var body = System.Linq.Expressions.Expression.Block(call, System.Linq.Expressions.Expression.Empty());
        var del = System.Linq.Expressions.Expression.Lambda(handlerType, body, paramExprs).Compile();

        ev.RemoveEventHandler(target, del);
    }

    // ____ Python Exceptio Group/BaseException support (PEP 654) __________________________________________________
    public class NajaExceptionGroup : Exception
    {
        public object[] Exceptions { get; }
        public string GroupMessage { get; }

        public NajaExceptionGroup(string message, object[] exceptions)
            : base(message)
        {
            GroupMessage = message;
            Exceptions = exceptions;
        }
    }

    // ── Attribute access helpers ──────────────────────────────────────────────

    /// <summary>Runtime fallback for static member access on a .NET type.</summary>
    public static object? GetStaticAttr(Type type, string name)
    {
        if (type is null) return null;
        
        // 1. Static property (Color.White, SystemInformation.WorkingArea, etc.)
        var prop = type.GetProperty(name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
        if (prop?.GetGetMethod() is { } getter) 
            return getter.Invoke(null, null);

        // 2. Enum member by name — must come before GetField because enum
        //    literal fields have FieldAttributes.Literal and require special handling
        if (type.IsEnum)
        {
            try { return Enum.Parse(type, name, ignoreCase: true); }
            catch { /* fall through */ }
        }

        // 3. Static field (non-enum, e.g. Color.Empty, IntPtr.Zero)
        var field = type.GetField(name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy);
        if (field is not null) 
            return field.GetValue(null);

        throw new MissingFieldException($"Field not found: '{type.FullName}.{name}'");
    }

    public static object? GetAttr(object obj, string name)
    {
        if (obj is null) return null;
        var t = obj.GetType();
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        System.Reflection.PropertyInfo? prop = null;
        try { prop = t.GetProperty(name, flags); }
        catch (System.Reflection.AmbiguousMatchException)
        {
            // WinForms heavily uses 'new' properties (like Form.Size hiding Control.Size).
            // Grab the first match which is always the most derived type.
            prop = t.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (prop is not null) return prop.GetValue(obj);

        System.Reflection.FieldInfo? field = null;
        try { field = t.GetField(name, flags); }
        catch (System.Reflection.AmbiguousMatchException)
        {
            field = t.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (field is not null) return field.GetValue(obj);

        // Try dict-like storage for Python objects
        if (obj is System.Collections.Generic.Dictionary<string, object> d &&
            d.TryGetValue(name, out var v)) return v;

        throw new Exception($"AttributeError: '{t.Name}' object has no attribute '{name}'");
    }

    public static System.Collections.IEnumerator GetEnumerator(object obj)
    {
        if (obj is System.Collections.IEnumerator er) return er;
        if (obj is System.Collections.IEnumerable e) return e.GetEnumerator();
        throw new Exception($"TypeError: '{obj?.GetType().Name}' object is not iterable");
    }

    /// <summary>Coerce a value to the target type, always succeeding if any conversion exists.</summary>
    public static object? CoerceValue(object? value, Type targetType)
    {
        if (value is null) return null;
        if (targetType.IsAssignableFrom(value.GetType())) return value;
        if (TryConvertArg(value, targetType, false, out var converted, out _)) return converted;

        // Last-resort: if value is long and target is int32/int16/byte/sbyte, narrow it
        if (value is long l)
        {
            if (targetType == typeof(int) || targetType == typeof(int?)) return (int)l;
            if (targetType == typeof(short) || targetType == typeof(short?)) return (short)l;
            if (targetType == typeof(byte) || targetType == typeof(byte?)) return (byte)l;
            if (targetType == typeof(sbyte) || targetType == typeof(sbyte?)) return (sbyte)l;
            if (targetType == typeof(float) || targetType == typeof(float?)) return (float)l;
            if (targetType == typeof(double) || targetType == typeof(double?)) return (double)l;
            if (targetType.IsEnum) return Enum.ToObject(targetType, (int)l);
        }
        if (value is double d2)
        {
            if (targetType == typeof(float) || targetType == typeof(float?)) return (float)d2;
            if (targetType == typeof(int) || targetType == typeof(int?)) return (int)d2;
        }
        return value; // let SetValue throw with a clear CLR message
    }

    public static void SetAttr(object obj, string name, object? value)
    {
        if (obj is null) return;
        var t = obj.GetType();
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        System.Reflection.PropertyInfo? prop = null;
        try { prop = t.GetProperty(name, flags); }
        catch (System.Reflection.AmbiguousMatchException)
        {
            prop = t.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (prop is not null && prop.CanWrite)
        {
            try { prop.SetValue(obj, CoerceValue(value, prop.PropertyType)); }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
            { throw new Exception($"AttributeError: setting '{t.Name}.{name}' failed: {tie.InnerException.Message}", tie.InnerException); }
            return;
        }

        System.Reflection.FieldInfo? field = null;
        try { field = t.GetField(name, flags); }
        catch (System.Reflection.AmbiguousMatchException)
        {
            field = t.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (field is not null)
        {
            try { field.SetValue(obj, CoerceValue(value, field.FieldType)); }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
            { throw new Exception($"AttributeError: setting field '{t.Name}.{name}' failed: {tie.InnerException.Message}", tie.InnerException); }
            return;
        }

        if (obj is System.Collections.Generic.Dictionary<string, object> d)
        { d[name] = value!; return; }

        throw new Exception($"AttributeError: cannot set '{name}' on '{t.Name}'");
    }

    // ── Subscript helpers ─────────────────────────────────────────────────────

    private static int SafeToInt32(object key)
    {
        if (key is int i) return i;
        if (key is long l) return (int)l;
        if (key is short s) return s;
        if (key is byte b) return b;
        throw new InvalidCastException($"TypeError: list indices must be integers, not {key?.GetType().Name}");
    }

    public static object? GetItem(object obj, object key)
    {
        if (obj is null) throw new Exception("TypeError: 'NoneType' is not subscriptable");

        if (obj is System.Collections.Generic.List<object> l) return l[SafeToInt32(key)];
        if (obj is System.Collections.Generic.Dictionary<object, object> d) return d.TryGetValue(key, out var val) ? val : throw new Exception($"KeyError: {Repr(key)}");
        if (obj is string s) return s[SafeToInt32(key)].ToString();
        if (obj is object[] arr) return arr[SafeToInt32(key)];

        var t = obj.GetType();
        var defaultMember = t.GetCustomAttributes(typeof(System.Reflection.DefaultMemberAttribute), true)
                             .OfType<System.Reflection.DefaultMemberAttribute>()
                             .FirstOrDefault();

        if (defaultMember != null)
        {
            // Instead of GetProperty (which throws on overloads), grab all properties matching the indexer name
            var candidates = t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                              .Where(p => p.Name == defaultMember.MemberName && p.GetIndexParameters().Length == 1);

            foreach (var propInfo in candidates)
            {
                var parameters = propInfo.GetIndexParameters();
                try
                {
                    var coercedKey = CoerceValue(key, parameters[0].ParameterType);
                    return propInfo.GetValue(obj, new[] { coercedKey });
                }
                catch (Exception ex) when (!(ex is System.Reflection.TargetInvocationException))
                {
                    // Wrong indexer type (e.g., tried passing int to the string indexer).
                    // Swallow the conversion/argument error and try the next overload!
                    continue;
                }
                catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
                {
                    throw new Exception($"Error mapping key '{key}' to indexer on '{t.Name}': {tie.InnerException.Message}", tie.InnerException);
                }
            }
        }

        throw new Exception($"TypeError: '{t.Name}' is not subscriptable");
    }

    public static void SetItem(object obj, object key, object? value)
    {
        if (obj is null) throw new Exception("TypeError: 'NoneType' does not support item assignment");

        if (obj is System.Collections.Generic.List<object> l) { l[SafeToInt32(key)] = value!; return; }
        if (obj is System.Collections.Generic.Dictionary<object, object> d) { d[key] = value!; return; }
        if (obj is object[] arr) { arr[SafeToInt32(key)] = value!; return; }

        var t = obj.GetType();
        var defaultMember = t.GetCustomAttributes(typeof(System.Reflection.DefaultMemberAttribute), true)
                             .OfType<System.Reflection.DefaultMemberAttribute>()
                             .FirstOrDefault();

        if (defaultMember != null)
        {
            var candidates = t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                              .Where(p => p.Name == defaultMember.MemberName && p.GetIndexParameters().Length == 1);

            foreach (var propInfo in candidates)
            {
                var parameters = propInfo.GetIndexParameters();
                try
                {
                    var coercedKey = CoerceValue(key, parameters[0].ParameterType);
                    var coercedVal = CoerceValue(value, propInfo.PropertyType);
                    propInfo.SetValue(obj, coercedVal, new[] { coercedKey });
                    return;
                }
                catch (Exception ex) when (!(ex is System.Reflection.TargetInvocationException))
                {
                    // Swallow the conversion/argument error and try the next overload!
                    continue;
                }
                catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
                {
                    throw new Exception($"Error assigning value via indexer on '{t.Name}': {tie.InnerException.Message}", tie.InnerException);
                }
            }
        }

        throw new Exception($"TypeError: '{t.Name}' does not support item assignment");
    }

    // ── Context manager helpers ───────────────────────────────────────────────

    public static object? ContextEnter(object obj)
    {
        // Try __enter__ method
        var m = obj?.GetType().GetMethod("__enter__");
        if (m is not null) return m.Invoke(obj, null);
        // IDisposable pattern — return self
        return obj;
    }

    public static void ContextExit(object? obj)
    {
        if (obj is null) return;

        // 1. Try Naja __exit__ protocol
        var exitM = obj.GetType().GetMethod("__exit__");
        if (exitM is not null)
        {
            var ps = exitM.GetParameters();
            exitM.Invoke(obj, new object?[ps.Length]);
            return;
        }

        // 2. Try Naja-emitted Dispose override (typed as object, not bool)
        //    Naja subclasses emit: public object Dispose(object disposing)
        var najaDispose = obj.GetType().GetMethod("Dispose",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
        if (najaDispose is not null)
        {
            var ps = najaDispose.GetParameters();
            if (ps.Length == 0)
                najaDispose.Invoke(obj, null);
            else
                najaDispose.Invoke(obj, new object?[] { true });
            return;
        }

        // 3. Standard IDisposable fallback
        if (obj is IDisposable d) d.Dispose();
    }

    // ── enumerate() ──────────────────────────────────────────────────────────

    public static System.Collections.Generic.List<object> Enumerate(object[] args)
    {
        var iterable = args[0];
        long start = args.Length > 1 ? Convert.ToInt64(args[1]) : 0;
        var result = new System.Collections.Generic.List<object>();
        long i = start;
        foreach (var item in (System.Collections.IEnumerable)iterable)
            result.Add(new object[] { (object)i++, item });
        return result;
    }

    // ── zip() ─────────────────────────────────────────────────────────────────

    public static System.Collections.Generic.List<object> Zip(object[] args)
    {
        var iters = args.Select(a =>
            ((System.Collections.IEnumerable)a).GetEnumerator()).ToArray();
        var result = new System.Collections.Generic.List<object>();
        while (iters.All(e => e.MoveNext()))
            result.Add(iters.Select(e => e.Current).ToArray());
        return result;
    }

    // ── map() ─────────────────────────────────────────────────────────────────

    public static System.Collections.Generic.List<object> Map(object func, object iterable)
    {
        var result = new System.Collections.Generic.List<object>();
        var m = func?.GetType().GetMethod("Invoke");
        foreach (var item in (System.Collections.IEnumerable)iterable)
        {
            var r = m is not null
                ? m.Invoke(func, new[] { item })
                : item;
            result.Add(r!);
        }
        return result;
    }

    // ── filter() ──────────────────────────────────────────────────────────────

    public static System.Collections.Generic.List<object> Filter(object func, object iterable)
    {
        var result = new System.Collections.Generic.List<object>();
        var m = func?.GetType().GetMethod("Invoke");
        foreach (var item in (System.Collections.IEnumerable)iterable)
        {
            var keep = m is not null
                ? ToBool(m.Invoke(func, new[] { item })!)
                : ToBool(item!);
            if (keep) result.Add(item!);
        }
        return result;
    }

    // ── any() / all() ─────────────────────────────────────────────────────────

    public static bool Any(object iterable) =>
        ((System.Collections.IEnumerable)iterable).Cast<object>().Any(x => ToBool(x!));

    public static bool All(object iterable) =>
        ((System.Collections.IEnumerable)iterable).Cast<object>().All(x => ToBool(x!));

    // ── chr() / ord() ─────────────────────────────────────────────────────────

    public static string Chr(object code) => ((char)Convert.ToInt32(code)).ToString();
    public static long Ord(object c) => (long)((string)c)[0];

    // ── hex() / bin() / oct() ─────────────────────────────────────────────────

    public static string Hex(object n) => "0x" + Convert.ToInt64(n).ToString("x");
    public static string Bin(object n) => "0b" + Convert.ToString(Convert.ToInt64(n), 2);
    public static string Oct(object n) => "0o" + Convert.ToString(Convert.ToInt64(n), 8);

    // ── round() ───────────────────────────────────────────────────────────────

    public static object Round(object[] args)
    {
        var n = Convert.ToDouble(args[0]);
        var digits = args.Length > 1 ? Convert.ToInt32(args[1]) : 0;
        var r = Math.Round(n, digits);

        // In Python, round(x) returns an int if ndigits is omitted
        if (args.Length == 1)
            return (object)(long)r;
        return (object)r;
    }

    // ── divmod() ──────────────────────────────────────────────────────────────

    public static object[] DivMod(object a, object b)
    {
        var la = Convert.ToInt64(a);
        var lb = Convert.ToInt64(b);
        return new object[] { (object)(la / lb), (object)(la % lb) };
    }

    // ── pow() ────────────────────────────────────────────────────────────────

    public static object Pow(object[] args)
    {
        var b = Convert.ToDouble(args[0]);
        var e = Convert.ToDouble(args[1]);
        return Math.Pow(b, e);
    }

    // ── open() ────────────────────────────────────────────────────────────────

    public static object Open(object[] args)
    {
        var path = ToStr(args[0]);
        var mode = args.Length > 1 ? ToStr(args[1]) : "r";
        return mode.Contains('w')
            ? (object)new System.IO.StreamWriter(path)
            : new System.IO.StreamReader(path);
    }

    // ── print() with sep/end support ─────────────────────────────────────────

    public static void Print(object[] args)
    {
        Console.WriteLine(string.Join(" ", args.Select(a => ToStr(a))));
    }

    // ── input() ───────────────────────────────────────────────────────────────

    public static string Input(string prompt = "")
    {
        if (!string.IsNullOrEmpty(prompt))
            Console.Write(prompt);
        return Console.ReadLine() ?? "";
    }

    // ── len() ─────────────────────────────────────────────────────────────────

    public static long Len(object obj)
    {
        if (obj is null)
            throw new Exception($"object of type 'NoneType' has no len()");

        // Check for Count property first (handles __len__ dunder method)
        var countProp = obj.GetType().GetProperty("Count", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (countProp is not null && countProp.PropertyType == typeof(int))
        {
            return (int)countProp.GetValue(obj)!;
        }

        // Check for __len__ method
        var lenMethod = obj.GetType().GetMethod("__len__",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (lenMethod is not null)
        {
            var result = lenMethod.Invoke(obj, null);
            return Convert.ToInt64(result);
        }

        return obj switch
        {
            string s => s.Length,
            System.Collections.Generic.List<object> l => l.Count,
            System.Collections.ICollection c => c.Count,
            System.Collections.IEnumerable e => e.Cast<object>().LongCount(),
            _ => throw new Exception($"object of type '{obj?.GetType().Name}' has no len()")
        };
    }

    // ── range() ───────────────────────────────────────────────────────────────

    public static System.Collections.Generic.List<object> Range(object[] args)
    {
        long start = 0, stop, step = 1;

        if (args.Length == 1) stop = Convert.ToInt64(args[0]);
        else if (args.Length == 2) { start = Convert.ToInt64(args[0]); stop = Convert.ToInt64(args[1]); }
        else if (args.Length == 3) { start = Convert.ToInt64(args[0]); stop = Convert.ToInt64(args[1]); step = Convert.ToInt64(args[2]); }
        else throw new Exception("range expected 1-3 arguments");

        var result = new System.Collections.Generic.List<object>();
        for (long i = start; step > 0 ? i < stop : i > stop; i += step)
            result.Add((object)i);
        return result;
    }

    // ── Type conversions ──────────────────────────────────────────────────────

    public static long ToInt(object obj) => obj switch
    {
        long l => l,
        double d => (long)d,
        bool b => b ? 1L : 0L,
        string s => long.Parse(s),
        _ => Convert.ToInt64(obj)
    };

    public static double ToFloat(object obj) => obj switch
    {
        double d => d,
        long l => (double)l,
        bool b => b ? 1.0 : 0.0,
        string s => double.Parse(s),
        _ => Convert.ToDouble(obj)
    };

    public static string ToStr(object? obj) => obj switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        string s => s,
        long l => l.ToString(),
        int i => ((long)i).ToString(),
        double d => FormatFloat(d),
        float f => FormatFloat(f),
        Exception ex => ex.Message,
        System.Collections.Generic.List<object> l =>
            "[" + string.Join(", ", l.Select(x => Repr(x))) + "]",
        object[] arr =>
            "(" + string.Join(", ", arr.Select(x => Repr(x))) + ")",
        _ when obj.GetType().IsPrimitive =>
            Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture) ?? "None",
        _ => obj.ToString() ?? "None"
    };

    public static bool ToBool(object? obj) => obj switch
    {
        null => false,
        bool b => b,
        long l => l != 0,
        double d => d != 0.0,
        string s => s.Length > 0,
        System.Collections.ICollection c => c.Count > 0,
        _ => true
    };

    // ── Math ──────────────────────────────────────────────────────────────────

    public static object Abs(object obj) => obj switch
    {
        long l => (object)Math.Abs(l),
        double d => (object)Math.Abs(d),
        _ => throw new Exception($"bad operand type for abs(): '{obj?.GetType().Name}'")
    };

    public static object Max(object[] args)
    {
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e)
            args = e.Cast<object>().ToArray();
        return args.Aggregate((a, b) =>
            Comparer<object>.Default.Compare(a, b) >= 0 ? a : b);
    }

    public static object Min(object[] args)
    {
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e)
            args = e.Cast<object>().ToArray();
        return args.Aggregate((a, b) =>
            Comparer<object>.Default.Compare(a, b) <= 0 ? a : b);
    }

    public static object Sum(object[] args)
    {
        var items = args.Length == 1 && args[0] is System.Collections.IEnumerable e
            ? e.Cast<object>()
            : args.AsEnumerable();

        return items.Aggregate((object)0L, (acc, x) =>
        {
            if (acc is double || x is double)
                return Convert.ToDouble(acc) + Convert.ToDouble(x);
            return Convert.ToInt64(acc) + Convert.ToInt64(x);
        });
    }

    // ── Collections ───────────────────────────────────────────────────────────

    public static System.Collections.Generic.List<object> Sorted(object obj)
    {
        var items = ((System.Collections.IEnumerable)obj).Cast<object>().ToList();
        items.Sort(Comparer<object>.Default);
        return items;
    }

    public static System.Collections.Generic.List<object> MakeList(object[] args)
    {
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e && !(args[0] is string))
            return e.Cast<object>().ToList();
        return args.ToList();
    }

    public static System.Collections.Generic.Dictionary<object, object> MakeDict(object[] args)
    {
        var d = new System.Collections.Generic.Dictionary<object, object>();
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e)
        {
            if (e is System.Collections.Generic.Dictionary<object, object> otherDict)
                foreach (var kv in otherDict) d[kv.Key] = kv.Value;
            else
                foreach (var item in e)
                {
                    if (item is object[] pair && pair.Length == 2)
                        d[pair[0]] = pair[1];
                    else if (item is System.Collections.Generic.List<object> listPair && listPair.Count == 2)
                        d[listPair[0]] = listPair[1];
                }
        }
        return d;
    }

    // ── Type checking (legacy — kept for IL call sites) ───────────────────────

    public static string Repr(object? obj) => obj switch
    {
        null => "None",
        bool b => b ? "True" : "False",
        string s => $"'{s}'",
        long l => l.ToString(),
        double d => FormatFloat(d),
        System.Collections.Generic.List<object> l =>
            "[" + string.Join(", ", l.Select(x => Repr(x))) + "]",
        object[] arr =>
            "(" + string.Join(", ", arr.Select(x => Repr(x))) + ")",
        System.Collections.Generic.Dictionary<object, object> dict =>
            "{" + string.Join(", ", dict.Select(kv => $"{Repr(kv.Key)}: {Repr(kv.Value)}")) + "}",
        System.Collections.Generic.HashSet<object> set =>
            "{" + string.Join(", ", set.Select(x => Repr(x))) + "}",
        System.Collections.Immutable.ImmutableHashSet<object> fset =>
            "frozenset({" + string.Join(", ", fset.Select(x => Repr(x))) + "})",
        _ => obj.ToString() ?? "None"
    };

    public static object TypeOf(object obj) => obj?.GetType() ?? typeof(void);

    // ── First-class callable (H5) ─────────────────────────────────────────────

    /// <summary>Call any Python callable: delegate/lambda, MethodInfo, or __call__ object.</summary>
    public static object? CallCallable(object? func, object[] args)
    {
        if (func is null)
            throw new Exception("TypeError: 'NoneType' object is not callable");

        // Delegate (lambda or Func<...>)
        if (func is Delegate d)
            return d.DynamicInvoke(args.Length == 0 ? null : (object?[])args);

        // Reflection MethodInfo stored in a variable (nested function reference)
        if (func is System.Reflection.MethodInfo mi)
        {
            var ps = mi.GetParameters();
            var invokeArgs = new object?[ps.Length];
            for (int i = 0; i < Math.Min(args.Length, ps.Length); i++)
            {
                try { invokeArgs[i] = Convert.ChangeType(args[i],
                    Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType,
                    System.Globalization.CultureInfo.InvariantCulture); }
                catch { invokeArgs[i] = args[i]; }
            }
            return mi.Invoke(null, invokeArgs);
        }

        // Callable object with __call__
        var callMethod = func.GetType().GetMethod("__call__");
        if (callMethod is not null)
            return callMethod.Invoke(func, (object?[])args);

        throw new Exception($"TypeError: '{func.GetType().Name}' object is not callable");
    }

    // ── Exception cause (raise X from Y) ─────────────────────────────────────

    // ── assert ─────────────────────────────────────

    public static void Assert(object? condition, object? message = null)
    {
        if (condition is bool b && b) return;
        if (condition is long l && l != 0) return;
        if (condition is not null and not false) return;

        var msg = message is not null ? message.ToString() : "AssertionError";
        throw new InvalidOperationException(msg!);
    }

    public static Exception SetExceptionCause(Exception ex, object? cause) => ex;

    // ── Starred-unpack helpers (H2) ───────────────────────────────────────────

    /// <summary>Convert any iterable to an object list for unpacking.</summary>
    public static System.Collections.Generic.List<object?> UnpackIterable(object? obj)
    {
        if (obj is null) throw new Exception("TypeError: cannot unpack non-iterable None");
        return ((System.Collections.IEnumerable)obj).Cast<object?>().ToList();
    }

    /// <summary>Slice a list for starred unpack: start inclusive, endFromEnd exclusive (negative counts from end).</summary>
    public static System.Collections.Generic.List<object?> GetUnpackSlice(
        System.Collections.Generic.List<object?> lst, int start, int endFromEnd)
    {
        int end = lst.Count - endFromEnd;
        int count = end - start;
        if (count <= 0) return new System.Collections.Generic.List<object?>();
        return lst.GetRange(start, count);
    }



    // ── Membership ────────────────────────────────────────────────────────────

    public static bool Contains(object item, object collection) => collection switch
    {
        string s => s.Contains(ToStr(item)),
        System.Collections.Generic.List<object> l => l.Contains(item),
        System.Collections.Generic.Dictionary<object, object> d => d.ContainsKey(item),
        System.Collections.IEnumerable e => e.Cast<object>().Contains(item),
        _ => false
    };

    // ── Collections (remaining) ───────────────────────────────────────────────

    public static System.Collections.Generic.List<object> Reversed(object obj)
    {
        var items = ((System.Collections.IEnumerable)obj).Cast<object>().ToList();
        items.Reverse();
        return items;
    }

    public static System.Collections.Generic.HashSet<object> MakeSet(object[] args)
    {
        var s = new System.Collections.Generic.HashSet<object>();
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e)
            foreach (var x in e) s.Add(x);
        else
            foreach (var x in args) s.Add(x);
        return s;
    }

    public static System.Collections.Immutable.ImmutableHashSet<object> MakeFrozenSet(object[] args)
    {
        var builder = System.Collections.Immutable.ImmutableHashSet.CreateBuilder<object>();
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e && !(args[0] is string))
            foreach (var x in e) builder.Add(x);
        else if (args.Length == 1 && args[0] is string str)
            foreach (var x in str) builder.Add(x.ToString());
        else
            foreach (var x in args) builder.Add(x);
        return builder.ToImmutable();
    }

    public static object[] MakeTuple(object[] args)
    {
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e && !(args[0] is string))
            return e.Cast<object>().ToArray();
        return args;
    }

    // ── id() / hash() ─────────────────────────────────────────────────────────

    public static long Id(object obj) =>
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);

    public static long Hash(object obj) => obj?.GetHashCode() ?? 0;

    // ── hasattr() / callable() ────────────────────────────────────────────────

    public static bool HasAttr(object obj, object name)
    {
        var n = ToStr(name);
        var t = obj?.GetType();
        if (t is null) return false;
        return t.GetProperty(n) is not null ||
               t.GetField(n) is not null ||
               t.GetMethod(n) is not null;
    }

    public static bool Callable(object obj) =>
        obj?.GetType().GetMethod("Invoke") is not null ||
        obj?.GetType().GetMethod("__call__") is not null;

    // ── vars() / dir() ────────────────────────────────────────────────────────

    public static System.Collections.Generic.Dictionary<object, object> Vars(object obj)
    {
        var d = new System.Collections.Generic.Dictionary<object, object>();
        foreach (var f in obj.GetType().GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            d[f.Name] = f.GetValue(obj) ?? "None";
        return d;
    }

    public static System.Collections.Generic.List<object> Dir(object obj)
    {
        var t = obj is Type type ? type : obj?.GetType() ?? typeof(object);
        var names = t.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                                  System.Reflection.BindingFlags.Instance |
                                  System.Reflection.BindingFlags.Static)
                     .Select(m => (object)m.Name)
                     .Distinct()
                     .OrderBy(n => n)
                     .ToList();
        return names;
    }

    // ── format() ──────────────────────────────────────────────────────────────

    public static string Format(object value, object spec = null!)
    {
        var s = spec is null ? "" : ToStr(spec);
        if (string.IsNullOrEmpty(s)) return ToStr(value);
        // Basic format specs: d, f, e, g, x, b, o, s, .Nf
        if (s.EndsWith("d")) return Convert.ToInt64(value).ToString();
        if (s.EndsWith("x")) return Convert.ToInt64(value).ToString("x");
        if (s.EndsWith("X")) return Convert.ToInt64(value).ToString("X");
        if (s.EndsWith("b")) return Convert.ToString(Convert.ToInt64(value), 2);
        if (s.EndsWith("o")) return Convert.ToString(Convert.ToInt64(value), 8);
        if (s.EndsWith("e")) return Convert.ToDouble(value).ToString("e6");
        if (s.EndsWith("s")) return ToStr(value);
        // .Nf — fixed decimal places
        if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^\.\d+f$"))
        {
            int decimals = int.Parse(s[1..^1]);
            return Convert.ToDouble(value).ToString($"F{decimals}",
                System.Globalization.CultureInfo.InvariantCulture);
        }
        return ToStr(value);
    }

    // ── iter() / next() ───────────────────────────────────────────────────────

    public static System.Collections.IEnumerator Iter(object iterable)
    {
        if (iterable is null)
            throw new Exception("TypeError: 'NoneType' object is not iterable");

        if (iterable is System.Collections.IEnumerator er) return er;
        if (iterable is System.Collections.IEnumerable e) return e.GetEnumerator();
        throw new Exception($"TypeError: '{iterable.GetType().Name}' object is not iterable");
    }

    public static object? Next(object iterator)
    {
        if (iterator is null)
            throw new Exception("TypeError: 'NoneType' object is not an iterator. Did you forget to return a generator from your function?");

        var e = (System.Collections.IEnumerator)iterator;
        if (e.MoveNext()) return e.Current;
        throw new Exception("StopIteration");
    }

    public static object? Next(object iterator, object? defaultValue)
    {
        if (iterator is null)
            throw new Exception("TypeError: 'NoneType' object is not an iterator. Did you forget to return a generator from your function?");

        var e = (System.Collections.IEnumerator)iterator;
        if (e.MoveNext()) return e.Current;
        return defaultValue;
    }

    /// <summary>Helper for implementing IEnumerator.MoveNext() using Python __next__ method.</summary>
    public static bool IteratorMoveNext(Func<object> nextMethod, ref object currentValue, ref bool exhausted)
    {
        if (exhausted) return false;
        
        try
        {
            currentValue = nextMethod();
            return true;
        }
        catch (Exception ex) when (ex.Message == "StopIteration")
        {
            exhausted = true;
            return false;
        }
    }

    // ── isinstance() with tuple of types ─────────────────────────────────────

    public static bool IsInstance(object obj, object typeOrTuple)
    {
        var types = typeOrTuple is object[] arr
            ? arr.OfType<Type>()
            : typeOrTuple is Type t
                ? new[] { t }.AsEnumerable()
                : Enumerable.Empty<Type>();
        return types.Any(ty => ty.IsInstanceOfType(obj));
    }

    // ── String method bridge ──────────────────────────────────────────────────
    // Called via EmitMethodCall when obj is a string

    public static string StrUpper(object s) => ToStr(s).ToUpper();
    public static string StrLower(object s) => ToStr(s).ToLower();
    public static string StrStrip(object s) => ToStr(s).Trim();
    public static string StrLStrip(object s) => ToStr(s).TrimStart();
    public static string StrRStrip(object s) => ToStr(s).TrimEnd();
    public static bool StrStartsWith(object s, object p) => ToStr(s).StartsWith(ToStr(p));
    public static bool StrEndsWith(object s, object p) => ToStr(s).EndsWith(ToStr(p));
    public static bool StrIsDigit(object s) => ToStr(s).All(char.IsDigit);
    public static bool StrIsAlpha(object s) => ToStr(s).All(char.IsLetter);
    public static bool StrIsAlNum(object s) => ToStr(s).All(char.IsLetterOrDigit);
    public static long StrFind(object s, object sub) => ToStr(s).IndexOf(ToStr(sub));
    public static long StrIndex(object s, object sub) =>
        ToStr(s).Contains(ToStr(sub)) ? ToStr(s).IndexOf(ToStr(sub)) : throw new Exception($"ValueError: substring not found");
    public static string StrReplace(object s, object old, object @new) => ToStr(s).Replace(ToStr(old), ToStr(@new));
    public static string StrCenter(object s, object w, object fill = null!) =>
        ToStr(s).PadLeft((int)((Convert.ToInt64(w) + ToStr(s).Length) / 2), (fill == null ? " " : ToStr(fill))[0]).PadRight((int)Convert.ToInt64(w), (fill == null ? " " : ToStr(fill))[0]);
    public static string StrLJust(object s, object w, object fill = null!) =>
        ToStr(s).PadRight((int)Convert.ToInt64(w), (fill == null ? " " : ToStr(fill))[0]);
    public static string StrRJust(object s, object w, object fill = null!) =>
        ToStr(s).PadLeft((int)Convert.ToInt64(w), (fill == null ? " " : ToStr(fill))[0]);
    public static string StrZFill(object s, object w) => ToStr(s).PadLeft((int)Convert.ToInt64(w), '0');
    public static long StrCount(object s, object sub) =>
        (ToStr(s).Length - ToStr(s).Replace(ToStr(sub), "").Length) / (ToStr(sub).Length > 0 ? ToStr(sub).Length : 1);
    public static string StrJoin(object sep, object items) =>
        string.Join(ToStr(sep), ((System.Collections.IEnumerable)items).Cast<object>().Select(ToStr));
    public static System.Collections.Generic.List<object> StrSplit(object s, object sep = null!)
    {
        var str = ToStr(s);
        var parts = sep is null || ToStr(sep) == ""
            ? str.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            : str.Split(new[] { ToStr(sep) }, StringSplitOptions.None);
        return parts.Select(p => (object)p).ToList();
    }
    public static System.Collections.Generic.List<object> StrSplitLines(object s) =>
        ToStr(s).Replace("\r\n", "\n").Split('\n').Select(p => (object)p).ToList();
    public static string StrTitle(object s) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(ToStr(s).ToLower());

    // ── List method bridge ────────────────────────────────────────────────────

    public static void ListAppend(System.Collections.Generic.List<object> l, object item)
        => l.Add(item);
    public static void ListExtend(System.Collections.Generic.List<object> l, object items)
        => l.AddRange(((System.Collections.IEnumerable)items).Cast<object>());
    public static void ListInsert(System.Collections.Generic.List<object> l, long idx, object item)
        => l.Insert((int)idx, item);
    public static object ListPop(System.Collections.Generic.List<object> l, object? idx = null)
    {
        long index = idx is null ? -1 : Convert.ToInt64(idx);
        int i = index < 0 ? l.Count + (int)index : (int)index;
        var v = l[i]; l.RemoveAt(i); return v;
    }
    public static void ListRemove(System.Collections.Generic.List<object> l, object item)
        => l.Remove(item);
    public static void ListReverse(System.Collections.Generic.List<object> l)
        => l.Reverse();
    public static void ListSort(System.Collections.Generic.List<object> l)
        => l.Sort(Comparer<object>.Default);
    public static long ListIndex(System.Collections.Generic.List<object> l, object item)
        => l.IndexOf(item);
    public static long ListCount(System.Collections.Generic.List<object> l, object item)
        => l.Count(x => Equals(x, item));
    public static System.Collections.Generic.List<object> ListCopy(
        System.Collections.Generic.List<object> l) => new(l);
    public static void ListClear(System.Collections.Generic.List<object> l) => l.Clear();

    // ── Dict method bridge ────────────────────────────────────────────────────

    public static System.Collections.Generic.List<object> DictKeys(
        System.Collections.Generic.Dictionary<object, object> d) =>
        d.Keys.ToList<object>();
    public static System.Collections.Generic.List<object> DictValues(
        System.Collections.Generic.Dictionary<object, object> d) =>
        d.Values.ToList<object>();
    public static System.Collections.Generic.List<object> DictItems(
        System.Collections.Generic.Dictionary<object, object> d) =>
        d.Select(kv => (object)new object[] { kv.Key, kv.Value }).ToList();
    public static object? DictGet(
        System.Collections.Generic.Dictionary<object, object> d,
        object key, object? def = null) =>
        d.TryGetValue(key, out var v) ? v : def;
    public static object DictPop(
        System.Collections.Generic.Dictionary<object, object> d,
        object key, object? def = null)
    {
        if (d.TryGetValue(key, out var v)) { d.Remove(key); return v; }
        if (def is not null) return def;
        throw new Exception($"KeyError: {Repr(key)}");
    }
    public static void DictUpdate(
        System.Collections.Generic.Dictionary<object, object> d, object other)
    {
        if (other is System.Collections.Generic.Dictionary<object, object> od)
            foreach (var kv in od) d[kv.Key] = kv.Value;
    }
    public static void DictClear(
        System.Collections.Generic.Dictionary<object, object> d) => d.Clear();
    public static System.Collections.Generic.Dictionary<object, object> DictCopy(
        System.Collections.Generic.Dictionary<object, object> d) => new(d);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatFloat(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        var s = d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }
}



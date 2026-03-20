using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Reflection and dynamic call utilities for .NET object interop.
/// Handles dynamic method calls, attribute access, and event subscriptions on .NET objects from Python code.
/// </summary>
public static class ReflectionHelpers
{
    /// <summary>Get a static attribute (property, field, or enum value) from a .NET type.</summary>
    public static object? GetStaticAttr(Type type, string name)
    {
        if (type is null) return null;

        // 1. Static property (Color.White, SystemInformation.WorkingArea, etc.)
        var prop = type.GetProperty(name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
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
        FieldInfo? field = null;
        try
        {
            field = type.GetField(name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        }
        catch (AmbiguousMatchException)
        {
            // Multiple fields with same name in hierarchy - use first match
            field = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        if (field is not null)
            return field.GetValue(null);

        // Return null instead of throwing - caller should handle missing fields
        return null;
    }

    /// <summary>Check if an object has an attribute (property, field, or method).</summary>
    public static bool HasAttr(object obj, object name)
    {
        var nameStr = name?.ToString() ?? "";
        var t = obj?.GetType();
        if (t is null) return false;
        return t.GetProperty(nameStr) is not null ||
               t.GetField(nameStr) is not null ||
               t.GetMethod(nameStr) is not null;
    }

    /// <summary>Get an instance attribute (property or field) from a .NET object.</summary>
    public static object? GetAttr(object obj, string name)
    {
        if (obj is null) return null;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        PropertyInfo? prop = null;
        try { prop = t.GetProperty(name, flags); }
        catch (AmbiguousMatchException)
        {
            // WinForms heavily uses 'new' properties (like Form.Size hiding Control.Size).
            // Grab the first match which is always the most derived type.
            prop = t.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (prop is not null) return prop.GetValue(obj);

        FieldInfo? field = null;
        try { field = t.GetField(name, flags); }
        catch (AmbiguousMatchException)
        {
            field = t.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (field is not null) return field.GetValue(obj);

        // Try dict-like storage for Python objects
        if (obj is System.Collections.Generic.Dictionary<string, object> d &&
            d.TryGetValue(name, out var v)) return v;

        throw new Exception($"AttributeError: '{t.Name}' object has no attribute '{name}'");
    }

    /// <summary>Set an instance attribute (property or field) on a .NET object.</summary>
    public static void SetAttr(object obj, string name, object? value)
    {
        if (obj is null) return;
        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        PropertyInfo? prop = null;
        try { prop = t.GetProperty(name, flags); }
        catch (AmbiguousMatchException)
        {
            prop = t.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (prop is not null && prop.CanWrite)
        {
            try { prop.SetValue(obj, TypeSystem.CoerceValue(value, prop.PropertyType)); }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            { throw new Exception($"AttributeError: setting '{t.Name}.{name}' failed: {tie.InnerException.Message}", tie.InnerException); }
            return;
        }

        FieldInfo? field = null;
        try { field = t.GetField(name, flags); }
        catch (AmbiguousMatchException)
        {
            field = t.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (field is not null)
        {
            try { field.SetValue(obj, TypeSystem.CoerceValue(value, field.FieldType)); }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            { throw new Exception($"AttributeError: setting '{t.Name}.{name}' failed: {tie.InnerException.Message}", tie.InnerException); }
            return;
        }

        throw new Exception($"AttributeError: '{t.Name}' object attribute '{name}' is read-only");
    }

    /// <summary>Get an item from a container using Python semantics (list[i], dict[key], etc.).</summary>
    public static object? GetItem(object obj, object key)
    {
        if (obj is null)
            throw new Exception($"TypeError: 'NoneType' object is not subscriptable");

        if (obj is string s)
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += s.Length;
            if (idx < 0 || idx >= s.Length) throw new Exception($"IndexError: string index out of range");
            return s[idx].ToString();
        }

        if (obj is System.Collections.Generic.List<object> l)
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += l.Count;
            if (idx < 0 || idx >= l.Count) throw new Exception($"IndexError: list index out of range");
            return l[idx];
        }

        if (obj is System.Collections.Generic.Dictionary<object, object> d)
            return d.TryGetValue(key, out var val) ? val : throw new Exception($"KeyError: {TypeConversion.Repr(key)}");

        if (obj is object[] arr)
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += arr.Length;
            if (idx < 0 || idx >= arr.Length) throw new Exception($"IndexError: tuple index out of range");
            return arr[idx];
        }

        var getitemMethod = obj.GetType().GetMethod("__getitem__",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (getitemMethod is not null)
        {
            try { return getitemMethod.Invoke(obj, new object[] { key }); }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        throw new Exception($"TypeError: '{obj.GetType().Name}' object is not subscriptable");
    }

    /// <summary>Set an item in a container using Python semantics (list[i] = x, dict[key] = x, etc.).</summary>
    public static void SetItem(object obj, object key, object? value)
    {
        if (obj is null) throw new Exception($"TypeError: 'NoneType' object does not support item assignment");

        if (obj is System.Collections.Generic.List<object> l)
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += l.Count;
            if (idx < 0 || idx >= l.Count) throw new Exception($"IndexError: list assignment index out of range");
            l[idx] = value;
            return;
        }

        if (obj is System.Collections.Generic.Dictionary<object, object> d)
        {
            d[key] = value;
            return;
        }

        var setitemMethod = obj.GetType().GetMethod("__setitem__",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (setitemMethod is not null)
        {
            try { setitemMethod.Invoke(obj, new object?[] { key, value }); return; }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        throw new Exception($"TypeError: '{obj.GetType().Name}' object does not support item assignment");
    }

    /// <summary>Call a method dynamically on a .NET object from Python code.</summary>
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
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);

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
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                    throw; // unreachable
                }
            }
        }

        var t = obj.GetType();
        var flags2 = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var candidates2 = t.GetMethods(flags2)
            .Where(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase))
            .Cast<MethodBase>();

        if (!TryBindBestCallable(candidates2, args, out var mb, out var boundArgs))
            throw new Exception($"AttributeError: '{t.Name}' object has no method '{method}' matching {args.Length} argument(s)");

        var mi = (MethodInfo)mb;
        try { return mi.Invoke(obj, boundArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Invoke a static method on a .NET type.</summary>
    public static object? StaticCall(Type type, string methodName, object[] args)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase;
        var candidates = type.GetMethods(flags)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));

        if (!TryBindBestCallable(candidates, args, out var method, out var boundArgs))
            throw new Exception($"AttributeError: type '{type.FullName}' has no static method '{methodName}' matching {args.Length} argument(s)");

        try { return method.Invoke(null, boundArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    // ── Event handler utilities ──────────────────────────────────────────────────

    /// <summary>Subscribe a handler method to an event on a .NET object.</summary>
    public static void AddEventHandler(object target, string eventName, object? handlerTarget, string handlerMethodName)
    {
        if (target is null) return;
        var t = target.GetType();
        var evt = t.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (evt is null) throw new Exception($"EventError: type '{t.Name}' has no event '{eventName}'");

        var handlerType = evt.EventHandlerType!;
        var invoke = handlerType.GetMethod("Invoke")!;

        Delegate del;
        if (handlerTarget is Delegate d)
        {
            del = d;
        }
        else if (handlerTarget is null)
        {
            throw new Exception($"EventError: handler is null");
        }
        else
        {
            var ht = handlerTarget.GetType();
            var handlerMethod = ht.GetMethod(handlerMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (handlerMethod is null)
                throw new Exception($"EventError: method '{handlerMethodName}' not found on handler object");

            del = Delegate.CreateDelegate(handlerType, handlerTarget, handlerMethod);
        }

        evt.AddEventHandler(target, del);
    }

    /// <summary>Unsubscribe a handler method from an event on a .NET object.</summary>
    public static void RemoveEventHandler(object target, string eventName, object? handlerTarget, string handlerMethodName)
    {
        if (target is null) return;
        var t = target.GetType();
        var evt = t.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (evt is null) throw new Exception($"EventError: type '{t.Name}' has no event '{eventName}'");

        var handlerType = evt.EventHandlerType!;

        Delegate del;
        if (handlerTarget is Delegate d)
        {
            del = d;
        }
        else if (handlerTarget is null)
        {
            throw new Exception($"EventError: handler is null");
        }
        else
        {
            var ht = handlerTarget.GetType();
            var handlerMethod = ht.GetMethod(handlerMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (handlerMethod is null)
                throw new Exception($"EventError: method '{handlerMethodName}' not found on handler object");

            del = Delegate.CreateDelegate(handlerType, handlerTarget, handlerMethod);
        }

        evt.RemoveEventHandler(target, del);
    }

    // ── Helper methods ───────────────────────────────────────────────────────────

    private static int SafeToInt32(object key)
    {
        if (key is int i) return i;
        if (key is long l) return (int)l;
        if (key is short s) return s;
        return Convert.ToInt32(key);
    }

    // ── Type checking and identity operations ────────────────────────────────

    /// <summary>Check if an object is an instance of a class or type.</summary>
    public static bool IsInstance(object obj, object classOrType)
    {
        if (classOrType is Type t)
            return t.IsInstanceOfType(obj);
        if (classOrType is NajaFunction)
            return obj is NajaFunction;
        return false;
    }

    /// <summary>Check if an object is callable (has __call__ method or is a delegate/function).</summary>
    public static bool Callable(object obj)
    {
        if (obj is null) return false;
        if (obj is Delegate or NajaFunction) return true;
        var t = obj.GetType();
        return t.GetMethod("__call__", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) is not null
            || typeof(Delegate).IsAssignableFrom(t);
    }

    /// <summary>Get the identity (memory address hash) of an object.</summary>
    public static long Id(object obj)
        => obj is null ? 0 : RuntimeHelpers.GetHashCode(obj);

    /// <summary>Get the hash code of an object.</summary>
    public static int Hash(object obj)
    {
        if (obj is null) return 0;
        if (obj is int i) return i;
        if (obj is long l) return l.GetHashCode();
        if (obj is string s) return s.GetHashCode();
        if (obj is double d) return d.GetHashCode();
        if (obj is bool b) return b.GetHashCode();
        return obj.GetHashCode();
    }

    /// <summary>Get the __dict__ of an object (its attributes as a dictionary).</summary>
    public static object Vars(object obj)
    {
        if (obj is null)
            throw new Exception("vars() argument must have __dict__ attribute");

        var d = new Dictionary<object, object>();
        var t = obj.GetType();
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        foreach (var prop in t.GetProperties(flags))
            if (prop.CanRead) d[prop.Name] = prop.GetValue(obj) ?? "";
        foreach (var field in t.GetFields(flags))
            d[field.Name] = field.GetValue(obj) ?? "";

        return d;
    }

    /// <summary>Get a list of attributes of an object.</summary>
    public static List<object> Dir(object obj)
    {
        if (obj is null)
            return new List<object>();

        var names = new HashSet<string>();
        var t = obj.GetType();
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        foreach (var prop in t.GetProperties(flags))
            names.Add(prop.Name);
        foreach (var field in t.GetFields(flags))
            names.Add(field.Name);
        foreach (var method in t.GetMethods(flags))
            names.Add(method.Name);

        return names.OrderBy(n => n).Cast<object>().ToList();
    }

    /// <summary>Get an iterator from an iterable object.</summary>
    public static object Iter(object obj)
    {
        if (obj is System.Collections.IEnumerable e)
            return e.GetEnumerator();
        throw new Exception($"TypeError: '{obj?.GetType().Name}' object is not iterable");
    }

    /// <summary>Call any Python callable: delegate, MethodInfo, or __call__ object.</summary>
    public static object? CallCallable(object? func, object[] args)
    {
        if (func is null)
            throw new Exception("TypeError: 'NoneType' object is not callable");

        if (func is Delegate d)
            return d.DynamicInvoke(args.Length == 0 ? null : (object?[])args);

        if (func is MethodInfo mi)
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

        if (func is Type t)
            return TypeSystem.CreateDotNet(t, args);

        var callMethod = func.GetType().GetMethod("__call__");
        if (callMethod is not null)
        {
            var ps = callMethod.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == typeof(object[]))
                return callMethod.Invoke(func, new object?[] { args });
            return callMethod.Invoke(func, (object?[])args);
        }

        throw new Exception($"TypeError: '{func.GetType().Name}' object is not callable");
    }

    /// <summary>Check if an object contains a key or item (__contains__).</summary>
    public static bool Contains(object container, object item)
    {
        if (container is null) return false;
        if (container is string s && item is string si) return s.Contains(si);
        if (container is System.Collections.Generic.List<object> l) return l.Contains(item);
        if (container is System.Collections.Generic.Dictionary<object, object> d) return d.ContainsKey(item);
        if (container is System.Collections.IEnumerable e) return e.Cast<object>().Contains(item);
        return false;
    }

    /// <summary>
    /// Best-effort method binding for dynamic calls with argument coercion.
    /// Tries to match the given arguments to the best overload of a callable.
    /// </summary>
    private static bool TryBindBestCallable(
        IEnumerable<MethodBase> candidates,
        object[] args,
        out MethodBase method,
        out object[] boundArgs)
    {
        method = null!;
        boundArgs = null!;

        // Filter by parameter count
        var exact = candidates.Where(m => m.GetParameters().Length == args.Length).ToList();
        if (exact.Count > 0)
        {
            // Try to find one with perfect type match
            foreach (var mb in exact)
            {
                var ps = mb.GetParameters();
                var bound = new object?[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    bound[i] = args[i] is null && ps[i].ParameterType.IsValueType ? null : args[i];
                    if (bound[i] is not null && !ps[i].ParameterType.IsAssignableFrom(bound[i].GetType()))
                        ok = false;
                }
                if (ok)
                {
                    method = mb;
                    boundArgs = bound!;
                    return true;
                }
            }

            // Use first and try coercion
            if (exact.Count > 0)
            {
                var mb = exact[0];
                var ps = mb.GetParameters();
                var bound = new object?[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    bound[i] = TypeSystem.CoerceValue(args[i], ps[i].ParameterType);

                method = mb;
                boundArgs = bound;
                return true;
            }
        }

        // Try by count and type coercion
        foreach (var mb in candidates)
        {
            var ps = mb.GetParameters();
            if (ps.Length != args.Length) continue;

            var bound = new object?[ps.Length];
            bool ok = true;
            for (int i = 0; i < ps.Length; i++)
            {
                try
                {
                    bound[i] = TypeSystem.CoerceValue(args[i], ps[i].ParameterType);
                }
                catch
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                method = mb;
                boundArgs = bound;
                return true;
            }
        }

        // Try params-array overloads (e.g., Path.Combine(string, string, string, string, string))
        foreach (var mb in candidates)
        {
            var ps = mb.GetParameters();
            if (ps.Length == 0) continue;
            var lastParam = ps[ps.Length - 1];
            if (!lastParam.IsDefined(typeof(ParamArrayAttribute), false)) continue;
            int fixedCount = ps.Length - 1;
            if (args.Length < fixedCount) continue;
            var elementType = lastParam.ParameterType.GetElementType()!;
            var bound = new object?[ps.Length];
            bool ok = true;
            for (int i = 0; i < fixedCount; i++)
            {
                try { bound[i] = TypeSystem.CoerceValue(args[i], ps[i].ParameterType); }
                catch { ok = false; break; }
            }
            if (!ok) continue;
            int paramsCount = args.Length - fixedCount;
            var paramsArr = Array.CreateInstance(elementType, paramsCount);
            for (int i = 0; i < paramsCount; i++)
            {
                try { paramsArr.SetValue(TypeSystem.CoerceValue(args[fixedCount + i], elementType), i); }
                catch { ok = false; break; }
            }
            if (!ok) continue;
            bound[fixedCount] = paramsArr;
            method = mb;
            boundArgs = bound!;
            return true;
        }

        return false;
    }
}

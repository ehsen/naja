namespace Naja.StdLib.Core;

/// <summary>
/// Python protocol dispatch for dunder methods.
/// Handles __floor__, __ceil__, __index__, __float__, __int__, etc.
/// 
/// Protocol dispatch must happen BEFORE type coercion in every module.
/// This ensures that user-defined types with custom dunder methods are
/// respected before falling back to primitive conversions.
/// </summary>
public static class Protocols
{
    /// <summary>
    /// Find a dunder method on the given object's type.
    /// Returns the method if found, or null if the type doesn't implement it.
    /// </summary>
    public static System.Reflection.MethodInfo? FindMethod(object? obj, string methodName)
    {
        if (obj == null)
            return null;

        var t = obj.GetType();
        var method = t.GetMethod(methodName, 
            System.Reflection.BindingFlags.IgnoreCase | 
            System.Reflection.BindingFlags.Public | 
            System.Reflection.BindingFlags.Instance);
        
        return method;
    }

    /// <summary>
    /// Invoke a dunder method if it exists, otherwise fall back to a default behavior.
    /// </summary>
    public static object? InvokeIfExists(object? obj, string methodName, object? defaultValue = null)
    {
        if (obj == null)
            return defaultValue;

        var method = FindMethod(obj, methodName);
        if (method == null)
            return defaultValue;

        try
        {
            return method.Invoke(obj, null);
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Check if a type implements a dunder method.
    /// </summary>
    public static bool HasMethod(object? obj, string methodName)
    {
        return obj != null && FindMethod(obj, methodName) != null;
    }

    /// <summary>
    /// Invoke __floor__ protocol. Returns long or falls back to converting to double.
    /// </summary>
    public static long Floor(object x)
    {
        if (Protocols.HasMethod(x, "__floor__"))
        {
            var result = Protocols.InvokeIfExists(x, "__floor__");
            if (result is long l) return l;
            if (result is double d) return (long)Math.Floor(d);
        }
        
        // Fall back to double conversion
        double v = TypeCoercion.ToDouble(x);
        return (long)Math.Floor(v);
    }

    /// <summary>
    /// Invoke __ceil__ protocol. Returns long or falls back to converting to double.
    /// </summary>
    public static long Ceil(object x)
    {
        if (Protocols.HasMethod(x, "__ceil__"))
        {
            var result = Protocols.InvokeIfExists(x, "__ceil__");
            if (result is long l) return l;
            if (result is double d) return (long)Math.Ceiling(d);
        }
        
        // Fall back to double conversion
        double v = TypeCoercion.ToDouble(x);
        return (long)Math.Ceiling(v);
    }

    /// <summary>
    /// Invoke __index__ protocol. Returns long or throws TypeError.
    /// </summary>
    public static long Index(object x)
    {
        if (Protocols.HasMethod(x, "__index__"))
        {
            var result = Protocols.InvokeIfExists(x, "__index__");
            if (result is long l) return l;
            if (result is int i) return i;
        }
        
        // Fall back to __int__ or type coercion
        return TypeCoercion.ToLong(x);
    }

    /// <summary>
    /// Invoke __float__ protocol. Returns double or throws TypeError.
    /// </summary>
    public static double Float(object x)
    {
        if (Protocols.HasMethod(x, "__float__"))
        {
            var result = Protocols.InvokeIfExists(x, "__float__");
            if (result is double d) return d;
            if (result is float f) return f;
        }
        
        // Fall back to default coercion
        return TypeCoercion.ToDouble(x);
    }

    /// <summary>
    /// Invoke __int__ protocol. Returns long or throws TypeError.
    /// </summary>
    public static long Int(object x)
    {
        if (Protocols.HasMethod(x, "__int__"))
        {
            var result = Protocols.InvokeIfExists(x, "__int__");
            if (result is long l) return l;
            if (result is int i) return i;
        }
        
        // Fall back to default coercion
        return TypeCoercion.ToLong(x);
    }

    /// <summary>
    /// Invoke __str__ protocol. Returns string or falls back to ToString().
    /// </summary>
    public static string Str(object x)
    {
        if (x == null)
            return "None";

        if (Protocols.HasMethod(x, "__str__"))
        {
            var result = Protocols.InvokeIfExists(x, "__str__");
            if (result is string s) return s;
        }
        
        return x.ToString() ?? "";
    }

    /// <summary>
    /// Invoke __repr__ protocol. Returns string or falls back to ToString().
    /// </summary>
    public static string Repr(object x)
    {
        if (x == null)
            return "None";

        if (Protocols.HasMethod(x, "__repr__"))
        {
            var result = Protocols.InvokeIfExists(x, "__repr__");
            if (result is string s) return s;
        }
        
        return x.ToString() ?? "";
    }

    /// <summary>
    /// Invoke __len__ protocol. Returns long or throws TypeError.
    /// </summary>
    public static long Len(object x)
    {
        if (Protocols.HasMethod(x, "__len__"))
        {
            var result = Protocols.InvokeIfExists(x, "__len__");
            if (result is long l) return l;
            if (result is int i) return i;
        }
        
        throw PythonException.TypeError($"object of type '{x?.GetType().Name}' has no len()");
    }

    /// <summary>
    /// Invoke __bool__ protocol. Returns bool or falls back to truthiness.
    /// </summary>
    public static bool Bool(object x)
    {
        if (x == null)
            return false;

        if (Protocols.HasMethod(x, "__bool__"))
        {
            var result = Protocols.InvokeIfExists(x, "__bool__");
            if (result is bool b) return b;
        }
        
        // Fallback: __len__ based truthiness
        if (Protocols.HasMethod(x, "__len__"))
        {
            try
            {
                return Len(x) != 0;
            }
            catch { }
        }
        
        // Default: all objects except None and False are truthy
        return !(x is false);
    }

    /// <summary>
    /// Invoke __iter__ protocol. Returns an enumerator or null.
    /// </summary>
    public static System.Collections.IEnumerator? Iter(object x)
    {
        if (x == null)
            return null;

        // Check for __iter__ method
        var iterMethod = FindMethod(x, "__iter__");
        if (iterMethod != null)
        {
            var result = iterMethod.Invoke(x, null);
            if (result is System.Collections.IEnumerable enumerable)
                return enumerable.GetEnumerator();
        }

        // Check if it's already enumerable
        if (x is System.Collections.IEnumerable enum_)
            return enum_.GetEnumerator();

        return null;
    }
}

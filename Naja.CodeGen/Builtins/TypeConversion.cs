using System.Reflection;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Type conversion utilities for converting Python values to strings, ints, floats, and booleans.
/// These methods match Python's conversion semantics.
/// </summary>
public static class TypeConversion
{
    /// <summary>Convert an object to a Python integer (long).</summary>
    public static long ToInt(object obj) => obj switch
    {
        long l => l,
        double d => (long)d,
        bool b => b ? 1L : 0L,
        string s => long.Parse(s),
        _ => Convert.ToInt64(obj)
    };

    /// <summary>Convert an object to a Python float (double).</summary>
    public static double ToFloat(object obj) => obj switch
    {
        double d => d,
        long l => (double)l,
        bool b => b ? 1.0 : 0.0,
        // Python float() accepts inf/infinity/nan spellings (any case, optional sign)
        string s => ParsePythonFloat(s),
        _ => Convert.ToDouble(obj)
    };

    private static double ParsePythonFloat(string s)
    {
        var t = s.Trim().ToLowerInvariant();
        var sign = 1.0;
        if (t.StartsWith('-')) { sign = -1.0; t = t[1..]; }
        else if (t.StartsWith('+')) { t = t[1..]; }
        return t switch
        {
            "inf" or "infinity" => sign * double.PositiveInfinity,
            "nan"              => double.NaN,
            _ => double.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Convert an object to a Python string representation.</summary>
    public static string ToStr(object? obj)
    {
        if (obj is null) return "None";
        if (obj is bool b) return b ? "True" : "False";
        if (obj is string s) return s;
        if (obj is long l) return l.ToString();
        if (obj is int i) return ((long)i).ToString();
        if (obj is double d) return FormatFloatHelper(d);
        if (obj is float f) return FormatFloatHelper(f);
        if (obj is Exception ex) return ex.Message;
        if (obj is System.Collections.Generic.List<object> listVal)
            return "[" + string.Join(", ", listVal.Select(x => Repr(x))) + "]";
        if (obj is object[] arrVal)
            return "(" + string.Join(", ", arrVal.Select(x => Repr(x))) + ")";

        // If object defines __str__, prefer that
        try
        {
            var t = obj.GetType();
            var m = t.GetMethod("__str__", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (m != null)
            {
                var res = ReflectionHelpers.DynamicCall(obj, "__str__", System.Array.Empty<object>());
                return ToStr(res);
            }
        }
        catch { /* fall back to ToString() */ }

        if (obj.GetType().IsPrimitive)
            return Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture) ?? "None";
        return obj.ToString() ?? "None";
    }

    /// <summary>Convert an object to a Python boolean value.</summary>
    public static bool ToBool(object? obj)
    {
        if (obj is null) return false;

        // If the object defines a Python-level __bool__, call it and coerce the result
        try
        {
            var t = obj.GetType();
            var m = t.GetMethod("__bool__", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (m != null)
            {
                var res = ReflectionHelpers.DynamicCall(obj, "__bool__", System.Array.Empty<object>());
                return ToBool(res);
            }
        }
        catch { /* fall back to default behaviour */ }

        // Also support CLR-style boolean operator overrides (op_True/op_False/op_Implicit)
        try
        {
            var t2 = obj.GetType();
            var opTrue = t2.GetMethod("op_True", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (opTrue != null)
            {
                var val = (bool?)opTrue.Invoke(null, new[] { obj });
                if (val.HasValue) return val.Value;
            }
        }
        catch { /* ignore */ }

        // Primitive and collection fallbacks
        if (obj is bool b) return b;
        if (obj is long l) return l != 0;
        if (obj is double d) return d != 0.0;
        if (obj is string s) return s.Length > 0;
        if (obj is System.Collections.ICollection c) return c.Count > 0;
        return true;
    }

    /// <summary>Get the repr() string of an object for debugging.</summary>
    public static string Repr(object? obj)
    {
        if (obj is null) return "None";
        if (obj is bool b) return b ? "True" : "False";
        if (obj is string s) return $"'{s}'";
        if (obj is long lnum) return lnum.ToString();
        if (obj is double dnum) return FormatFloatHelper(dnum);
        if (obj is System.Collections.Generic.List<object> listVal)
            return "[" + string.Join(", ", listVal.Select(x => Repr(x))) + "]";
        if (obj is object[] arrVal)
            return "(" + string.Join(", ", arrVal.Select(x => Repr(x))) + ")";
        if (obj is System.Collections.Generic.Dictionary<object, object> dictVal)
            return "{" + string.Join(", ", dictVal.Select(kv => $"{Repr(kv.Key)}: {Repr(kv.Value)}")) + "}";
        if (obj is System.Collections.Generic.HashSet<object> setVal)
            return "{" + string.Join(", ", setVal.Select(x => Repr(x))) + "}";
        if (obj is System.Collections.Immutable.ImmutableHashSet<object> fsetVal)
            return "frozenset({" + string.Join(", ", fsetVal.Select(x => Repr(x))) + "})";

        // If the object defines a Python-level __repr__, prefer calling that
        try
        {
            var t = obj.GetType();
            var m = t.GetMethod("__repr__", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (m != null)
            {
                var res = ReflectionHelpers.DynamicCall(obj, "__repr__", System.Array.Empty<object>());
                return ToStr(res);
            }
        }
        catch { /* fall back to ToString() */ }

        return obj.ToString() ?? "None";
    }

    /// <summary>Helper method for formatting float values in Python representation.</summary>
    private static string FormatFloatHelper(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        var s = d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }
}

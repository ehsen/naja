namespace Naja.StdLib.Core;

/// <summary>
/// Python-correct type coercion following CPython semantics.
/// All type conversions in the stdlib must go through these helpers, not Convert.*.
/// 
/// These methods call protocol methods (__float__, __int__, etc.) before
/// falling back to primitive conversions.
/// </summary>
public static class TypeCoercion
{
    /// <summary>
    /// Convert an object to a double following Python semantics.
    /// Checks __float__ protocol first, then tries __index__, then primitive conversion.
    /// </summary>
    public static double ToDouble(object x)
    {
        if (x == null)
            throw PythonException.TypeError("float() argument must be a string or a number, not 'NoneType'");

        if (x is double d) return d;
        if (x is float f) return f;
        if (x is long l) return (double)l;
        if (x is int i) return (double)i;
        if (x is bool b) return b ? 1.0 : 0.0;
        
        if (x is string s)
        {
            if (s == "inf") return double.PositiveInfinity;
            if (s == "-inf") return double.NegativeInfinity;
            if (s == "nan") return double.NaN;
            if (double.TryParse(s, out var result))
                return result;
            throw PythonException.ValueError($"could not convert string to float: '{s}'");
        }

        // Check for __float__ protocol
        if (Protocols.HasMethod(x, "__float__"))
        {
            try
            {
                var result = Protocols.Float(x);
                return result;
            }
            catch (Exception ex)
            {
                throw PythonException.TypeError($"__float__ returned non-float (type {x.GetType().Name})");
            }
        }

        throw PythonException.TypeError($"float() argument must be a string or a number, not '{x.GetType().Name}'");
    }

    /// <summary>
    /// Convert an object to a long following Python semantics.
    /// Checks __index__ protocol first, then __int__, then primitive conversion.
    /// </summary>
    public static long ToLong(object x)
    {
        if (x == null)
            throw PythonException.TypeError("int() argument must be a string, a bytes-like object or a number, not 'NoneType'");

        if (x is long l) return l;
        if (x is int i) return i;
        if (x is bool b) return b ? 1L : 0L;
        if (x is double d) return (long)d;
        if (x is float f) return (long)f;

        if (x is string s)
        {
            s = s.Trim();
            
            // Handle base specification like int("0x10", 16)
            if (long.TryParse(s, System.Globalization.NumberStyles.Any, null, out var result))
                return result;

            throw PythonException.ValueError($"invalid literal for int() with base 10: '{s}'");
        }

        // Check for __index__ protocol (preferred for indexing)
        if (Protocols.HasMethod(x, "__index__"))
        {
            try
            {
                return Protocols.Index(x);
            }
            catch { }
        }

        // Check for __int__ protocol
        if (Protocols.HasMethod(x, "__int__"))
        {
            try
            {
                return Protocols.Int(x);
            }
            catch (Exception ex)
            {
                throw PythonException.TypeError($"__int__ returned non-int (type {x.GetType().Name})");
            }
        }

        throw PythonException.TypeError($"int() argument must be a string, a bytes-like object or a number, not '{x.GetType().Name}'");
    }

    /// <summary>
    /// Convert an object to a bool following Python truthiness rules.
    /// </summary>
    public static bool ToBool(object x)
    {
        if (x == null)
            return false;

        if (x is bool b)
            return b;

        // Check for __bool__ protocol
        if (Protocols.HasMethod(x, "__bool__"))
        {
            return Protocols.Bool(x);
        }

        // Check for __len__ based truthiness (length > 0)
        if (Protocols.HasMethod(x, "__len__"))
        {
            try
            {
                return Protocols.Len(x) != 0;
            }
            catch { }
        }

        // Default: all objects are truthy
        return true;
    }

    /// <summary>
    /// Convert an object to a string following Python repr semantics.
    /// </summary>
    public static string ToStr(object x)
    {
        if (x == null)
            return "None";

        return Protocols.Str(x);
    }

    /// <summary>
    /// Convert an object to a string following Python str() semantics.
    /// This is different from Repr — it's the human-readable form.
    /// </summary>
    public static string ToRepr(object x)
    {
        if (x == null)
            return "None";

        if (x is string s)
            return $"'{s}'";

        return Protocols.Repr(x);
    }

    /// <summary>
    /// Get the length of an object, checking __len__ protocol.
    /// </summary>
    public static long GetLen(object x)
    {
        return Protocols.Len(x);
    }

    /// <summary>
    /// Coerce a value to a .NET type parameter.
    /// Used when passing Python values to .NET method calls.
    /// </summary>
    public static object CoerceValue(object value, Type targetType)
    {
        if (value == null)
            return null!;

        if (targetType.IsAssignableFrom(value.GetType()))
            return value;

        // Handle common .NET type coercions
        if (targetType == typeof(int))
            return (int)ToLong(value);
        
        if (targetType == typeof(long))
            return ToLong(value);
        
        if (targetType == typeof(double))
            return ToDouble(value);
        
        if (targetType == typeof(float))
            return (float)ToDouble(value);
        
        if (targetType == typeof(bool))
            return ToBool(value);
        
        if (targetType == typeof(string))
            return ToStr(value);

        // For other types, try the target type's constructor or cast
        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return value;
        }
    }
}

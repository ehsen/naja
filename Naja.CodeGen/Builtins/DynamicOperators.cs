using System.Reflection;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Dynamic arithmetic operators (+, -, *, /, //, %, **).
/// Handles dunder method dispatch (__add__, __radd__, etc.) before numeric fallback.
/// </summary>
public static class DynamicOperators
{
    /// <summary>Returns true if the boxed value is an integer type (Python int semantics).</summary>
    private static bool IsIntegerType(object? v) =>
        v is long or int or short or ushort or sbyte or byte or uint or bool;
    /// <summary>
    /// Dynamic addition: tries __add__ then __radd__ then numeric coercion.
    /// </summary>
    public static object DynamicAdd(object a, object b)
    {
        if (a is string sa && b is string sb) return sa + sb;

        // Try __add__ on left operand
        var addM = a?.GetType().GetMethod("__add__", BindingFlags.Public | BindingFlags.Instance);
        if (addM is not null)
        {
            try
            {
                var result = addM.Invoke(a, new[] { b });
                if (result == null) throw new Exception("TypeError: __add__ returned None");
                return result;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        // Try __radd__ on right operand
        var raddM = b?.GetType().GetMethod("__radd__", BindingFlags.Public | BindingFlags.Instance);
        if (raddM is not null)
        {
            try
            {
                var result = raddM.Invoke(b, new[] { a });
                if (result == null) throw new Exception("TypeError: __radd__ returned None");
                return result;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        // Only convert to numeric if both operands are IConvertible (primitives)
        if (a is System.Numerics.Complex ca)
        {
            var rb = b is System.Numerics.Complex cb2 ? cb2 : new System.Numerics.Complex(Convert.ToDouble(b), 0);
            return ca + rb;
        }
        if (b is System.Numerics.Complex cb)
        {
            var ra = new System.Numerics.Complex(Convert.ToDouble(a), 0);
            return ra + cb;
        }
        if (a is IConvertible && b is IConvertible)
        {
            try
            {
                // Preserve integer arithmetic: both integer types → return long
                if (IsIntegerType(a) && IsIntegerType(b))
                    return Convert.ToInt64(a) + Convert.ToInt64(b);
                return Convert.ToDouble(a) + Convert.ToDouble(b);
            }
            catch
            {
                throw new TypeError($"unsupported operand type(s) for +: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
            }
        }

        throw new TypeError($"unsupported operand type(s) for +: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    /// <summary>
    /// Dynamic subtraction.
    /// </summary>
    public static object DynamicSub(object a, object b)
    {
        if (a is System.Numerics.Complex ca)
        {
            var rb = b is System.Numerics.Complex cb2 ? cb2 : new System.Numerics.Complex(Convert.ToDouble(b), 0);
            return ca - rb;
        }
        if (b is System.Numerics.Complex cb)
        {
            var ra = new System.Numerics.Complex(Convert.ToDouble(a), 0);
            return ra - cb;
        }
        if ((a is IConvertible) && (b is IConvertible))
        {
            try
            {
                if (IsIntegerType(a) && IsIntegerType(b))
                    return Convert.ToInt64(a) - Convert.ToInt64(b);
                return Convert.ToDouble(a) - Convert.ToDouble(b);
            }
            catch { throw new TypeError($"unsupported operand type(s) for -: '{a?.GetType().Name}' and '{b?.GetType().Name}'"); }
        }
        throw new TypeError($"unsupported operand type(s) for -: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    /// <summary>
    /// Dynamic multiplication.
    /// </summary>
    public static object DynamicMul(object a, object b)
    {
        // String repetition: "ab" * 3 = "ababab"
        if (a is string s && b is IConvertible)
        {
            try
            {
                var count = (int)Convert.ToInt64(b);
                if (count <= 0) return "";
                var sb = new System.Text.StringBuilder(s.Length * count);
                for (int i = 0; i < count; i++) sb.Append(s);
                return sb.ToString();
            }
            catch { }
        }

        // Reversed string repetition: 3 * "ab" = "ababab"
        if (b is string sb2 && a is IConvertible)
        {
            try
            {
                var count = (int)Convert.ToInt64(a);
                if (count <= 0) return "";
                var result = new System.Text.StringBuilder(sb2.Length * count);
                for (int i = 0; i < count; i++) result.Append(sb2);
                return result.ToString();
            }
            catch { }
        }

        if ((a is IConvertible) && (b is IConvertible))
        {
            try
            {
                if (IsIntegerType(a) && IsIntegerType(b))
                    return Convert.ToInt64(a) * Convert.ToInt64(b);
                return Convert.ToDouble(a) * Convert.ToDouble(b);
            }
            catch { throw new TypeError($"unsupported operand type(s) for *: '{a?.GetType().Name}' and '{b?.GetType().Name}'"); }
        }
        throw new TypeError($"unsupported operand type(s) for *: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    /// <summary>
    /// Dynamic modulo (Python sign semantics).
    /// </summary>
    public static object DynamicMod(object a, object b)
    {
        if ((a is IConvertible) && (b is IConvertible))
        {
            try { return PyMod(Convert.ToInt64(a), Convert.ToInt64(b)); }
            catch { throw new TypeError($"unsupported operand type(s) for %: '{a?.GetType().Name}' and '{b?.GetType().Name}'"); }
        }
        throw new TypeError($"unsupported operand type(s) for %: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    /// <summary>
    /// Python floor division for integers (sign semantics).
    /// </summary>
    public static long PyFloorDiv(long a, long b)
    {
        if (b == 0) throw new DivideByZeroException();
        long q = a / b;
        long r = a % b;
        // Adjust for negative remainder (Python semantics vs. C# truncation)
        if ((r != 0) && ((r < 0) != (b < 0)))
            q--;
        return q;
    }

    /// <summary>
    /// Python floor division for floats.
    /// </summary>
    public static double PyFloorDivF(double a, double b)
    {
        if (b == 0.0) throw new DivideByZeroException();
        return System.Math.Floor(a / b);
    }

    /// <summary>
    /// Python modulo for integers (sign semantics).
    /// </summary>
    public static long PyMod(long a, long b)
    {
        if (b == 0) throw new DivideByZeroException();
        long r = a % b;
        // Adjust for Python semantics: result has same sign as divisor
        if ((r != 0) && ((r < 0) != (b < 0)))
            r += b;
        return r;
    }

    /// <summary>
    /// Python modulo for floats.
    /// </summary>
    public static double PyModF(double a, double b)
    {
        if (b == 0.0) throw new DivideByZeroException();
        double r = a % b;
        // Adjust for Python semantics
        if ((r != 0) && ((r < 0) != (b < 0)))
            r += b;
        return r;
    }

    /// <summary>
    /// Dynamic matrix multiply (@): delegates to __matmul__ / __rmatmul__ on operands.
    /// </summary>
    public static object DynamicMatMul(object a, object b)
    {
        // Try __matmul__ on left operand
        var matmulM = a?.GetType().GetMethod("__matmul__", BindingFlags.Public | BindingFlags.Instance);
        if (matmulM is not null)
        {
            try
            {
                var result = matmulM.Invoke(a, new[] { b });
                if (result == null) throw new Exception("TypeError: __matmul__ returned None");
                return result;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        // Try __rmatmul__ on right operand
        var rmatmulM = b?.GetType().GetMethod("__rmatmul__", BindingFlags.Public | BindingFlags.Instance);
        if (rmatmulM is not null)
        {
            try
            {
                var result = rmatmulM.Invoke(b, new[] { a });
                if (result == null) throw new Exception("TypeError: __rmatmul__ returned None");
                return result;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                throw tie.InnerException;
            }
        }

        throw new TypeError($"unsupported operand type(s) for @: '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }
}

/// <summary>
/// Represents a Python TypeError exception.
/// </summary>
public class TypeError : Exception
{
    public TypeError(string message) : base(message) { }
}

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Mathematical functions matching Python's builtin math operations.
/// Handles abs(), min(), max(), sum(), round(), pow(), and divmod().
/// </summary>
public static class MathFunctions
{
    /// <summary>Absolute value of a number.</summary>
    public static object Abs(object obj) => obj switch
    {
        long l => (object)Math.Abs(l),
        double d => (object)Math.Abs(d),
        _ => throw new Exception($"bad operand type for abs(): '{obj?.GetType().Name}'")
    };

    /// <summary>Maximum value from arguments or an iterable.</summary>
    public static object Max(object[] args)
    {
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e)
            args = e.Cast<object>().ToArray();
        return args.Aggregate((a, b) =>
            Comparer<object>.Default.Compare(a, b) >= 0 ? a : b);
    }

    /// <summary>Minimum value from arguments or an iterable.</summary>
    public static object Min(object[] args)
    {
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e)
            args = e.Cast<object>().ToArray();
        return args.Aggregate((a, b) =>
            Comparer<object>.Default.Compare(a, b) <= 0 ? a : b);
    }

    /// <summary>Sum of values in an iterable or arguments.</summary>
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

    /// <summary>Round a number to the nearest integer or specified number of digits.</summary>
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

    /// <summary>Divide with quotient and remainder (divmod).</summary>
    public static object[] DivMod(object a, object b)
    {
        var la = Convert.ToInt64(a);
        var lb = Convert.ToInt64(b);
        return new object[] { (object)(la / lb), (object)(la % lb) };
    }

    /// <summary>Raise base to power (base ** exponent).</summary>
    public static object Pow(object[] args)
    {
        var b = Convert.ToDouble(args[0]);
        var e = Convert.ToDouble(args[1]);
        return Math.Pow(b, e);
    }

    /// <summary>Python floor division for integers.</summary>
    public static long PyFloorDiv(long a, long b)
    {
        var q = a / b;
        // If the remainder is non-zero and signs differ, floor by subtracting 1
        if ((a ^ b) < 0 && q * b != a) q--;
        return q;
    }

    /// <summary>Python floor division for floats.</summary>
    public static double PyFloorDivF(double a, double b)
    {
        if (b == 0) throw new Exception("ZeroDivisionError: float floor division by zero");
        return Math.Floor(a / b);
    }

    /// <summary>Python modulo for integers.</summary>
    public static long PyMod(long a, long b)
    {
        if (b == 0) throw new Exception("ZeroDivisionError: integer division or modulo by zero");
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

    /// <summary>
    /// Python integer power: base ** exponent for long operands, promoting to
    /// BigInteger on overflow (Python ints are arbitrary precision). Negative
    /// exponents produce a float, matching CPython. Returns object: long for
    /// results that fit, BigInteger otherwise.
    /// </summary>
    public static object PyPow(long b, long e)
    {
        if (e < 0)
            return Math.Pow(b, e);
        if (e == 0)
            return 1L;

        // Guard: astronomically large exponents fall back to double (the
        // result would be astronomically large anyway and a loop would hang).
        if (e > 1_000_000)
            return Math.Pow(b, e);

        // Fast path: compute in long with overflow detection.
        try
        {
            long result = 1;
            for (long i = 0; i < e; i++)
                result = checked(result * b);
            return result;
        }
        catch (OverflowException)
        {
            // Promote to BigInteger for the exact result.
            var big = System.Numerics.BigInteger.Pow(b, (int)e);
            if (big >= long.MinValue && big <= long.MaxValue)
                return (long)big;
            return big;
        }
    }

    /// <summary>Float/complex-aware power used when operands aren't both ints.</summary>
    public static object PyPowDynamic(object b, object e)
    {
        if (b is System.Numerics.BigInteger bi)
        {
            var exp = Convert.ToInt64(e);
            if (exp >= 0 && exp <= int.MaxValue)
                return System.Numerics.BigInteger.Pow(bi, (int)exp);
            return Math.Pow((double)bi, exp);
        }
        if (e is System.Numerics.BigInteger be)
            return Math.Pow(Convert.ToDouble(b), (double)be);

        if (b is long lb && e is long le)
            return PyPow(lb, le);

        return Math.Pow(Convert.ToDouble(b), Convert.ToDouble(e));
    }

    /// <summary>
    /// Exact dynamic floor division (a // b) for Unknown-typed operands.
    /// Keeps integer semantics exact (int//int → int, never 1.0) and handles
    /// BigInteger; falls back to double floor only for true float operands.
    /// Replaces the old ToFloat-everything path where `3 // 2` → 1.0.
    /// </summary>
    public static object PyFloorDivDynamic(object a, object b)
    {
        // BigInteger operands stay exact
        if (a is System.Numerics.BigInteger || b is System.Numerics.BigInteger)
        {
            var la = ToBig(a);
            var lb = ToBig(b);
            if (lb.IsZero) throw new DivideByZeroException("ZeroDivisionError: integer division or modulo by zero");
            var q = System.Numerics.BigInteger.Divide(la, lb);
            var r = la - q * lb;
            if (!r.IsZero && ((r < 0) != (lb < 0))) q -= 1;
            return q;
        }

        // both integer-ish → exact long floor division
        if (IsIntLike(a) && IsIntLike(b))
        {
            long la = ToLong(a), lb = ToLong(b);
            if (lb == 0) throw new DivideByZeroException("ZeroDivisionError: integer division or modulo by zero");
            long q = la / lb;
            long r = la % lb;
            if (r != 0 && ((r < 0) != (lb < 0))) q--;
            return q;
        }

        // float path (anything IConvertible)
        if (a is IConvertible && b is IConvertible)
        {
            double fa = Convert.ToDouble(a), fb = Convert.ToDouble(b);
            if (fb == 0) throw new DivideByZeroException("ZeroDivisionError: float floor division by zero");
            return Math.Floor(fa / fb);
        }

        throw new InvalidCastException(
            $"TypeError: unsupported operand type(s) for //: '{TypeName(a)}' and '{TypeName(b)}'");

        static bool IsIntLike(object v)
            => v is long || v is int || v is short || v is sbyte || v is byte || v is ushort || v is uint || v is bool;

        static long ToLong(object v) => v switch
        {
            bool bo => bo ? 1L : 0L,
            IConvertible ic => Convert.ToInt64(ic),
            _ => Convert.ToInt64(v)
        };

        static System.Numerics.BigInteger ToBig(object v) => v switch
        {
            System.Numerics.BigInteger big => big,
            bool bo => bo ? 1 : 0,
            double d => (System.Numerics.BigInteger)d,
            _ => (System.Numerics.BigInteger)ToLong(v)
        };
    }

    /// <summary>Python unary minus with type checking. TypeError for str/bytes etc.</summary>
    public static object PyNeg(object v) => v switch
    {
        long l            => (object)(-l),
        double d          => (object)(-d),
        System.Numerics.BigInteger bi => (object)(-bi),
        System.Numerics.Complex c => (object)new System.Numerics.Complex(-c.Real, -c.Imaginary),
        bool bo           => (object)(bo ? -1L : 0L),   // True == 1
        int i             => (object)(-(long)i),
        _ => throw new InvalidCastException(
            $"TypeError: bad operand type for unary -: '{TypeName(v)}'")
    };

    /// <summary>Python unary plus with type checking.</summary>
    public static object PyPos(object v) => v switch
    {
        long l            => (object)l,
        double d          => (object)d,
        System.Numerics.BigInteger bi => (object)bi,
        System.Numerics.Complex c => (object)c,
        bool bo           => (object)(bo ? 1L : 0L),
        int i             => (object)(long)i,
        _ => throw new InvalidCastException(
            $"TypeError: bad operand type for unary +: '{TypeName(v)}'")
    };

    /// <summary>Python ~ (bitwise invert) with type checking. Float/complex/str → TypeError.</summary>
    public static object PyInvert(object v) => v switch
    {
        long l            => (object)(~l),
        System.Numerics.BigInteger bi => (object)(~bi),
        bool bo           => (object)(bo ? -2L : -1L),   // ~True == -2
        int i             => (object)(~(long)i),
        _ => throw new InvalidCastException(
            $"TypeError: bad operand type for unary ~: '{TypeName(v)}'")
    };

    private static string TypeName(object? v) => v?.GetType().Name ?? "NoneType";

    /// <summary>Parse a decimal string into a BigInteger (for literals that overflow long).</summary>
    public static object ParseBigInt(string s) => System.Numerics.BigInteger.Parse(s);

    /// <summary>Negate a BigInteger value (for unary minus on big integer literals).</summary>
    public static object NegateBigInt(object n)
    {
        if (n is System.Numerics.Complex c)
            return new System.Numerics.Complex(-c.Real, -c.Imaginary);
        return -(System.Numerics.BigInteger)n;
    }
}


using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python 'math' module emulation via System.Math.
/// Exposes constants (pi, e, inf, nan, tau) and common math functions.
/// 
/// All type coercion goes through Protocols and TypeCoercion to ensure
/// dunder methods are checked before primitive conversion (as per CPython).
/// </summary>
public sealed class NajaMath
{
    public static readonly NajaMath Instance = new();

    // ── Constants ─────────────────────────────────────────────────────────────
    public double pi  => Math.PI;
    public double e   => Math.E;
    public double tau => Math.Tau;
    public double inf => double.PositiveInfinity;
    public double nan => double.NaN;

    // ── Rounding / truncation ─────────────────────────────────────────────────
    /// <summary>math.floor(x) — returns the floor of x as an int.</summary>
    public long floor(object x)
    {
        // Protocol dispatch: check for __floor__ first
        if (Protocols.HasMethod(x, "__floor__"))
        {
            try
            {
                var result = Protocols.InvokeIfExists(x, "__floor__");
                if (result is long l) return l;
                if (result is int i) return i;
            }
            catch { }
        }

        return (long)Math.Floor(TypeCoercion.ToDouble(x));
    }

    /// <summary>math.ceil(x) — returns the ceiling of x as an int.</summary>
    public long ceil(object x)
    {
        // Protocol dispatch: check for __ceil__ first
        if (Protocols.HasMethod(x, "__ceil__"))
        {
            try
            {
                var result = Protocols.InvokeIfExists(x, "__ceil__");
                if (result is long l) return l;
                if (result is int i) return i;
            }
            catch { }
        }

        return (long)Math.Ceiling(TypeCoercion.ToDouble(x));
    }

    /// <summary>math.trunc(x) — truncates x to an integer.</summary>
    public long trunc(object x) => (long)Math.Truncate(TypeCoercion.ToDouble(x));

    /// <summary>math.fabs(x) — returns the absolute value of x.</summary>
    public double fabs(object x) => Math.Abs(TypeCoercion.ToDouble(x));

    // ── Powers and logarithms ─────────────────────────────────────────────────
    public double sqrt(object x)        => Math.Sqrt(TypeCoercion.ToDouble(x));
    public double cbrt(object x)        => Math.Cbrt(TypeCoercion.ToDouble(x));
    public double exp(object x)         => Math.Exp(TypeCoercion.ToDouble(x));
    public double log(object x)         => Math.Log(TypeCoercion.ToDouble(x));
    public double log(object x, object base_) => Math.Log(TypeCoercion.ToDouble(x), TypeCoercion.ToDouble(base_));
    public double log2(object x)        => Math.Log2(TypeCoercion.ToDouble(x));
    public double log10(object x)       => Math.Log10(TypeCoercion.ToDouble(x));
    public double pow(object x, object y) => Math.Pow(TypeCoercion.ToDouble(x), TypeCoercion.ToDouble(y));

    // ── Trigonometry ──────────────────────────────────────────────────────────
    public double sin(object x)   => Math.Sin(TypeCoercion.ToDouble(x));
    public double cos(object x)   => Math.Cos(TypeCoercion.ToDouble(x));
    public double tan(object x)   => Math.Tan(TypeCoercion.ToDouble(x));
    public double asin(object x)  => Math.Asin(TypeCoercion.ToDouble(x));
    public double acos(object x)  => Math.Acos(TypeCoercion.ToDouble(x));
    public double atan(object x)  => Math.Atan(TypeCoercion.ToDouble(x));
    public double atan2(object y, object x) => Math.Atan2(TypeCoercion.ToDouble(y), TypeCoercion.ToDouble(x));
    public double sinh(object x)  => Math.Sinh(TypeCoercion.ToDouble(x));
    public double cosh(object x)  => Math.Cosh(TypeCoercion.ToDouble(x));
    public double tanh(object x)  => Math.Tanh(TypeCoercion.ToDouble(x));
    public double degrees(object x) => TypeCoercion.ToDouble(x) * (180.0 / Math.PI);
    public double radians(object x) => TypeCoercion.ToDouble(x) * (Math.PI / 180.0);

    // ── Number theory ─────────────────────────────────────────────────────────
    /// <summary>math.factorial(n) — returns n! (n must be a non-negative integer).</summary>
    public long factorial(object n)
    {
        long v = TypeCoercion.ToLong(n);
        if (v < 0) throw PythonException.ValueError("factorial() not defined for negative values");
        if (v > 20922789888000) throw PythonException.OverflowError("factorial() result too large");

        long result = 1;
        for (long i = 2; i <= v; i++) result *= i;
        return result;
    }

    /// <summary>math.gcd(a, b) — returns the greatest common divisor of a and b.</summary>
    public long gcd(object a, object b)
    {
        long x = Math.Abs(TypeCoercion.ToLong(a));
        long y = Math.Abs(TypeCoercion.ToLong(b));
        while (y != 0) { long t = y; y = x % y; x = t; }
        return x;
    }

    public bool isfinite(object x)  => double.IsFinite(TypeCoercion.ToDouble(x));
    public bool isinf(object x)     => double.IsInfinity(TypeCoercion.ToDouble(x));
    public bool isnan(object x)     => double.IsNaN(TypeCoercion.ToDouble(x));

    public double hypot(object x, object y) => Math.Sqrt(TypeCoercion.ToDouble(x) * TypeCoercion.ToDouble(x) + TypeCoercion.ToDouble(y) * TypeCoercion.ToDouble(y));
    public double copysign(object x, object y) => Math.CopySign(TypeCoercion.ToDouble(x), TypeCoercion.ToDouble(y));
    public double remainder(object x, object y) => Math.IEEERemainder(TypeCoercion.ToDouble(x), TypeCoercion.ToDouble(y));

    // ── Combinatorics ─────────────────────────────────────────────────────────
    public double comb(object n, object k)
    {
        long nv = TypeCoercion.ToLong(n);
        long kv = TypeCoercion.ToLong(k);
        if (kv < 0 || kv > nv) return 0;
        if (kv == 0 || kv == nv) return 1;
        kv = Math.Min(kv, nv - kv);
        double result = 1;
        for (long i = 0; i < kv; i++)
            result = result * (nv - i) / (i + 1);
        return result;
    }

    public double perm(object n, object k)
    {
        long nv = TypeCoercion.ToLong(n);
        long kv = TypeCoercion.ToLong(k);
        if (kv < 0 || kv > nv) return 0;
        double result = 1;
        for (long i = nv - kv + 1; i <= nv; i++) result *= i;
        return result;
    }
}

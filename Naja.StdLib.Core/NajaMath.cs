using System.Diagnostics.CodeAnalysis;

namespace Naja.StdLib.Core;

/// <summary>
/// Python 'math' module emulation via System.Math.
/// Exposes constants (pi, e, inf, nan, tau) and common math functions.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
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
    public long floor(object x)  => (long)Math.Floor(ToDouble(x));
    public long ceil(object x)   => (long)Math.Ceiling(ToDouble(x));
    public long trunc(object x)  => (long)Math.Truncate(ToDouble(x));
    public double fabs(object x)   => Math.Abs(ToDouble(x));

    // ── Powers and logarithms ─────────────────────────────────────────────────
    public double sqrt(object x)        => Math.Sqrt(ToDouble(x));
    public double exp(object x)         => Math.Exp(ToDouble(x));
    public double log(object x)         => Math.Log(ToDouble(x));
    public double log(object x, object base_) => Math.Log(ToDouble(x), ToDouble(base_));
    public double log2(object x)        => Math.Log2(ToDouble(x));
    public double log10(object x)       => Math.Log10(ToDouble(x));
    public double pow(object x, object y) => Math.Pow(ToDouble(x), ToDouble(y));

    // ── Trigonometry ──────────────────────────────────────────────────────────
    public double sin(object x)   => Math.Sin(ToDouble(x));
    public double cos(object x)   => Math.Cos(ToDouble(x));
    public double tan(object x)   => Math.Tan(ToDouble(x));
    public double asin(object x)  => Math.Asin(ToDouble(x));
    public double acos(object x)  => Math.Acos(ToDouble(x));
    public double atan(object x)  => Math.Atan(ToDouble(x));
    public double atan2(object y, object x) => Math.Atan2(ToDouble(y), ToDouble(x));
    public double sinh(object x)  => Math.Sinh(ToDouble(x));
    public double cosh(object x)  => Math.Cosh(ToDouble(x));
    public double tanh(object x)  => Math.Tanh(ToDouble(x));
    public double degrees(object x) => ToDouble(x) * (180.0 / Math.PI);
    public double radians(object x) => ToDouble(x) * (Math.PI / 180.0);

    // ── Number theory ─────────────────────────────────────────────────────────
    public long factorial(object n)
    {
        long v = ToLong(n);
        if (v < 0) throw new ArgumentException("math domain error");
        long result = 1;
        for (long i = 2; i <= v; i++) result *= i;
        return result;
    }

    public long gcd(object a, object b)
    {
        long x = Math.Abs(ToLong(a));
        long y = Math.Abs(ToLong(b));
        while (y != 0) { long t = y; y = x % y; x = t; }
        return x;
    }

    public bool isfinite(object x)  => double.IsFinite(ToDouble(x));
    public bool isinf(object x)     => double.IsInfinity(ToDouble(x));
    public bool isnan(object x)     => double.IsNaN(ToDouble(x));

    public double hypot(object x, object y) => Math.Sqrt(ToDouble(x) * ToDouble(x) + ToDouble(y) * ToDouble(y));
    public double copysign(object x, object y) => Math.CopySign(ToDouble(x), ToDouble(y));
    public double remainder(object x, object y) => Math.IEEERemainder(ToDouble(x), ToDouble(y));

    // ── Combinatorics ─────────────────────────────────────────────────────────
    public double comb(object n, object k)
    {
        long nv = ToLong(n);
        long kv = ToLong(k);
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
        long nv = ToLong(n);
        long kv = ToLong(k);
        if (kv < 0 || kv > nv) return 0;
        double result = 1;
        for (long i = nv - kv + 1; i <= nv; i++) result *= i;
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static double ToDouble(object o) => o switch
    {
        double d => d,
        long l   => (double)l,
        int i    => (double)i,
        float f  => (double)f,
        _        => Convert.ToDouble(o)
    };

    private static long ToLong(object o) => o switch
    {
        long l   => l,
        int i    => (long)i,
        double d => (long)d,
        _        => Convert.ToInt64(o)
    };
}

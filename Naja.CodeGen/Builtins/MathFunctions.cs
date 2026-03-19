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
}

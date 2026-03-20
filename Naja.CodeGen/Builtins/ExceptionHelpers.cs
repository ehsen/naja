using System;
using System.Reflection;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Exception handling, context management, and assertion support.
/// Implements assert statements, exception chaining, with-block semantics, and f-string formatting.
/// </summary>
public static class ExceptionHelpers
{
    /// <summary>Assert that a condition is true, raising AssertionError with optional message.</summary>
    public static void Assert(object? condition, object? message = null)
    {
        if (condition is bool b && b) return;
        if (condition is long l && l != 0) return;
        if (condition is not null and not false) return;

        var msg = message is not null ? message.ToString() : "AssertionError";
        throw new InvalidOperationException(msg!);
    }

    /// <summary>Set the cause exception for exception chaining (Python 'raise X from Y').</summary>
    public static Exception SetExceptionCause(Exception ex, object? cause) => ex;

    /// <summary>Ensure the object is an Exception instance or exception type, creating if needed.</summary>
    public static Exception EnsureException(object? ex)
    {
        if (ex is Exception e) return e;
        if (ex is Type t)
        {
            if (!typeof(Exception).IsAssignableFrom(t))
                throw new Exception($"TypeError: exceptions must derive from Exception");
            try { return (Exception)Activator.CreateInstance(t)!; }
            catch (Exception createEx) { throw new Exception($"Error instantiating exception {t.Name}: {createEx.Message}", createEx); }
        }
        throw new Exception("TypeError: exceptions must be Exception instances or exception types");
    }

    /// <summary>Enter a with-block context manager (__enter__ protocol).</summary>
    public static object? ContextEnter(object obj)
    {
        // Try __enter__ method
        var m = obj?.GetType().GetMethod("__enter__");
        if (m is not null) return m.Invoke(obj, null);
        // IDisposable pattern — return self
        return obj;
    }

    /// <summary>Exit a with-block normally (__exit__ with no exception).</summary>
    public static void ContextExit(object? obj) => ContextExitWithException(obj, null);

    /// <summary>Exit a with-block due to exception (__exit__ with exception info).</summary>
    public static bool ContextExitWithException(object? obj, Exception? exc)
    {
        if (obj is null) return false;

        var exitM = obj.GetType().GetMethod("__exit__");
        if (exitM is not null)
        {
            var ps = exitM.GetParameters();
            object?[] exitArgs;
            if (ps.Length == 0)
                exitArgs = Array.Empty<object?>();
            else if (ps.Length >= 3)
                exitArgs = new object?[] {
                    exc?.GetType() as object,  // exc_type (Type or null)
                    exc as object,             // exc_val
                    null                       // traceback (no CLR equivalent)
                };
            else
                exitArgs = new object?[ps.Length];

            object? result;
            try { result = exitM.Invoke(obj, exitArgs); }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
            { throw tie.InnerException; }

            return TypeConversion.ToBool(result);
        }

        // Standard IDisposable fallback — never suppresses
        if (exc is null)
        {
            // Naja-emitted Dispose override
            var najaDispose = obj.GetType().GetMethod("Dispose",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (najaDispose is not null)
            {
                var ps = najaDispose.GetParameters();
                if (ps.Length == 0) najaDispose.Invoke(obj, null);
                else najaDispose.Invoke(obj, new object?[] { true });
            }
            else if (obj is IDisposable d) d.Dispose();
        }
        return false;
    }

    /// <summary>Create a NajaFunction wrapper from a delegate and default argument values.</summary>
    public static object CreateFunctionWithDefaults(Delegate d, object?[] defaults)
    {
        return new NajaFunction(d, defaults);
    }

    /// <summary>Format a value according to Python format specification.</summary>
    public static string Format(object value, object spec = null!)
    {
        var s = spec is null ? "" : TypeConversion.ToStr(spec);
        if (string.IsNullOrEmpty(s)) return TypeConversion.ToStr(value);

        // Parse Python format spec: [[fill]align][sign][#][0][width][grouping_option][.precision][type]
        var parsed = ParseFormatSpec(s);

        // Step 1: Produce the raw formatted string based on type code
        string raw = parsed.Type switch
        {
            'b' => Convert.ToString(Convert.ToInt64(value), 2),   // binary
            'o' => Convert.ToString(Convert.ToInt64(value), 8),   // octal
            'x' => Convert.ToInt64(value).ToString("x"),          // hex lowercase
            'X' => Convert.ToInt64(value).ToString("X"),          // hex uppercase
            'd' => Convert.ToInt64(value).ToString(),             // decimal
            'f' => FormatFloatFixed(value, parsed.Precision ?? 6),
            'e' => FormatFloatSci(value, parsed.Precision ?? 6),
            'g' => FormatFloatGeneral(value, parsed.Precision ?? 6),
            '%' => FormatPercent(value, parsed.Precision ?? 6),
            's' or '\0' => TypeConversion.ToStr(value),
            _ => TypeConversion.ToStr(value)
        };

        // Step 2: Apply alternate form prefix (# flag)
        if (parsed.AltForm && !raw.StartsWith("-"))
        {
            raw = parsed.Type switch
            {
                'b' => "0b" + raw,
                'o' => "0o" + raw,
                'x' => "0x" + raw,
                'X' => "0X" + raw,
                _ => raw
            };
        }

        // Step 3: Apply padding
        if (parsed.Width.HasValue && raw.Length < parsed.Width.Value)
        {
            var fillChar = parsed.FillChar;
            var padding = new string(fillChar, parsed.Width.Value - raw.Length);
            raw = parsed.Align switch
            {
                '<' => raw + padding,              // left align
                '^' => padding.Substring(0, padding.Length / 2) + raw + padding.Substring((padding.Length + 1) / 2), // center
                _ => padding + raw                 // right align (default)
            };
        }

        return raw;
    }

    // ── Format specification parsing ──────────────────────────────────────────

    private class FormatSpec
    {
        public char FillChar { get; set; } = ' ';
        public char Align { get; set; } // '<', '>', '^', '='
        public char? Sign { get; set; }  // '+', '-', ' '
        public bool AltForm { get; set; }
        public bool ZeroPad { get; set; }
        public int? Width { get; set; }
        public int? Precision { get; set; }
        public char Type { get; set; } = 's';
    }

    private static FormatSpec ParseFormatSpec(string spec)
    {
        var result = new FormatSpec();
        int i = 0;

        // [[fill]align]
        if (i < spec.Length)
        {
            if (i + 1 < spec.Length && "<>^=".Contains(spec[i + 1]))
            {
                result.FillChar = spec[i];
                result.Align = spec[i + 1];
                i += 2;
            }
            else if ("<>^=".Contains(spec[i]))
            {
                result.Align = spec[i];
                i++;
            }
        }

        // [sign]
        if (i < spec.Length && "+-  ".Contains(spec[i]))
        {
            result.Sign = spec[i];
            i++;
        }

        // [#]
        if (i < spec.Length && spec[i] == '#')
        {
            result.AltForm = true;
            i++;
        }

        // [0]
        if (i < spec.Length && spec[i] == '0' && (i + 1 >= spec.Length || !char.IsDigit(spec[i + 1])))
        {
            result.ZeroPad = true;
            i++;
        }

        // [width]
        if (i < spec.Length && char.IsDigit(spec[i]))
        {
            int widthEnd = i;
            while (widthEnd < spec.Length && char.IsDigit(spec[widthEnd])) widthEnd++;
            result.Width = int.Parse(spec.Substring(i, widthEnd - i));
            i = widthEnd;
        }

        // [.precision]
        if (i < spec.Length && spec[i] == '.')
        {
            i++;
            int precEnd = i;
            while (precEnd < spec.Length && char.IsDigit(spec[precEnd])) precEnd++;
            result.Precision = int.Parse(spec.Substring(i, precEnd - i));
            i = precEnd;
        }

        // [type]
        if (i < spec.Length)
        {
            result.Type = spec[i];
        }

        return result;
    }

    private static string FormatFloatFixed(object value, int precision)
    {
        var d = Convert.ToDouble(value);
        return d.ToString($"F{precision}");
    }

    private static string FormatFloatSci(object value, int precision)
    {
        var d = Convert.ToDouble(value);
        return d.ToString($"E{precision}");
    }

    private static string FormatFloatGeneral(object value, int precision)
    {
        var d = Convert.ToDouble(value);
        return d.ToString($"G{precision}");
    }

    private static string FormatPercent(object value, int precision)
    {
        var d = Convert.ToDouble(value) * 100;
        return d.ToString($"F{precision}") + "%";
    }
}

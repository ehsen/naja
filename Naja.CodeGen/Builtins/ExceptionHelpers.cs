using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Exception handling, context management, and assertion support.
/// Implements assert statements, exception chaining, with-block semantics, and f-string formatting.
/// </summary>
public static class ExceptionHelpers
{
    // ── Python exception-variable deletion sentinel ───────────────────────────
    // Python 3 deletes 'as e' variables at the end of except blocks.
    // We store this sentinel in the local so any subsequent load raises NameError.

    /// <summary>
    /// Sentinel stored in deleted exception-handler variables (Python 3 'as e' cleanup).
    /// A local holding this value raises NameError when accessed.
    /// </summary>
    public static readonly object DeletedSentinel = new();

    // ── Python exception chaining ─────────────────────────────────────────────
    // CLR Exception has no __cause__, __context__, __suppress_context__ properties.
    // We store them in a side-table keyed by exception identity (weak, so GC can collect).

    private sealed class NajaExceptionChain
    {
        public object? Cause { get; set; }            // __cause__ (explicit: raise X from Y)
        public object? Context { get; set; }          // __context__ (implicit chaining)
        public bool SuppressContext { get; set; }     // __suppress_context__ (raise X from None)
    }

    private static readonly ConditionalWeakTable<Exception, NajaExceptionChain> _chains = new();

    private static NajaExceptionChain GetOrCreateChain(Exception ex) =>
        _chains.GetOrCreateValue(ex);

    /// <summary>
    /// Get a Python exception-chaining attribute (__cause__, __context__, __suppress_context__)
    /// from the side-table, or null/false if not set.
    /// Returns <c>null</c> if <paramref name="name"/> is not a chaining attribute.
    /// </summary>
    public static (bool found, object? value) TryGetChainingAttr(object obj, string name)
    {
        if (obj is not Exception ex)
            return (false, null);

        switch (name)
        {
            case "__cause__":
                return _chains.TryGetValue(ex, out var ch1)
                    ? (true, ch1.Cause)
                    : (true, null);            // default: no cause
            case "__context__":
                return _chains.TryGetValue(ex, out var ch2)
                    ? (true, ch2.Context)
                    : (true, null);            // default: no context
            case "__suppress_context__":
                return _chains.TryGetValue(ex, out var ch3)
                    ? (true, (object)(ch3.SuppressContext))
                    : (true, (object)false);   // default: false
            default:
                return (false, null);
        }
    }

    /// <summary>Assert that a condition is true, raising AssertionError with optional message.</summary>
    public static void Assert(object? condition, object? message = null)
    {
        if (condition is bool b && b) return;
        if (condition is long l && l != 0) return;
        if (condition is not null and not false) return;

        var msg = message is not null ? message.ToString() : "AssertionError";
        throw new InvalidOperationException(msg!);
    }

    /// <summary>
    /// Set the cause exception for exception chaining (Python 'raise X from Y' / 'raise X from None').
    /// Stores the cause in the side-table and sets __suppress_context__ when cause is null.
    /// </summary>
    public static Exception SetExceptionCause(Exception ex, object? cause)
    {
        var chain = GetOrCreateChain(ex);
        chain.Cause = cause;
        // 'raise X from None' sets __suppress_context__ = True
        chain.SuppressContext = cause is null;
        return ex;
    }

    /// <summary>
    /// Set the implicit context (Python __context__) when an exception is raised
    /// inside an except handler.  Called automatically by the emitted catch block.
    /// </summary>
    public static Exception SetExceptionContext(Exception ex, Exception? context)
    {
        if (context is null) return ex;
        var chain = GetOrCreateChain(ex);
        chain.Context ??= context;  // only set if not already set
        return ex;
    }

    /// <summary>
    /// PEP 479: Wrap a StopIteration that escaped a generator body as RuntimeError.
    /// Creates <c>RuntimeError("generator raised StopIteration")</c> with
    /// <c>__cause__</c>, <c>__context__</c> set to a StopIteration instance and
    /// <c>__suppress_context__ = True</c>.
    /// </summary>
    public static InvalidOperationException WrapGeneratorStopIteration(Exception stopEx)
    {
        // __cause__ / __context__ must have type() == StopIteration == NajaStopIteration
        var cause = new NajaStopIteration(null);
        var rte   = new InvalidOperationException("RuntimeError: generator raised StopIteration");
        var chain = GetOrCreateChain(rte);
        chain.Cause           = cause;
        chain.Context         = cause;
        chain.SuppressContext = true;
        return rte;
    }

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

    /// <summary>
    /// Bare `raise` with no active exception — Python raises
    /// RuntimeError("No active exception to re-raise"). Emitted by
    /// EmitRaise when no except handler is active (IL `rethrow` is illegal
    /// outside a catch block and would crash the process).
    /// InvalidOperationException = Naja's RuntimeError mapping.
    /// </summary>
    public static InvalidOperationException NoActiveException() =>
        new("RuntimeError: No active exception to re-raise");

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
            'b' => Convert.ToString(Convert.ToInt64(value), 2),
            'o' => Convert.ToString(Convert.ToInt64(value), 8),
            'x' => Convert.ToInt64(value).ToString("x"),
            'X' => Convert.ToInt64(value).ToString("X"),
            'd' => Convert.ToInt64(value).ToString(),
            'f' => FormatFloatFixed(value, parsed.Precision ?? 6),
            'e' => FormatFloatSci(value, parsed.Precision ?? 6),
            'g' => FormatFloatGeneral(value, parsed.Precision ?? 6),
            '%' => FormatPercent(value, parsed.Precision ?? 6),
            's' or '\0' => TypeConversion.ToStr(value),
            _ => TypeConversion.ToStr(value)
        };

        // Step 2: Apply alternate form prefix (#)
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

        // Step 3: Sign handling
        bool isNumeric = parsed.Type is 'd' or 'b' or 'o' or 'x' or 'X' or 'f' or 'e' or 'g' or '%';
        if (isNumeric && !raw.StartsWith("-"))
        {
            if (parsed.Sign == '+') raw = "+" + raw;
            else if (parsed.Sign == ' ') raw = " " + raw;
        }

        // Step 4: Zero-padding (after sign/prefix, before fill/align)
        if (parsed.ZeroPad && parsed.Width.HasValue && raw.Length < parsed.Width.Value)
        {
            int totalWidth = parsed.Width.Value;
            if (raw.StartsWith("-") || raw.StartsWith("+") || raw.StartsWith(" "))
            {
                raw = raw[0] + raw[1..].PadLeft(totalWidth - 1, '0');
            }
            else if (raw.Length >= 2 && raw[0] == '0' &&
                     (raw[1] == 'b' || raw[1] == 'o' || raw[1] == 'x' || raw[1] == 'X'))
            {
                raw = raw[..2] + raw[2..].PadLeft(totalWidth - 2, '0');
            }
            else
            {
                raw = raw.PadLeft(totalWidth, '0');
            }
        }

        // Step 5: Fill/align padding
        if (parsed.Width.HasValue && raw.Length < parsed.Width.Value)
        {
            char fillChar = parsed.FillChar;
            int totalPad = parsed.Width.Value - raw.Length;
            char effectiveAlign = parsed.Align;
            if (effectiveAlign == '\0')
                effectiveAlign = value is string ? '<' : '>';
            int leftPad = totalPad / 2;
            int rightPad = totalPad - leftPad;
            raw = effectiveAlign switch
            {
                '<' => raw + new string(fillChar, totalPad),
                '^' => new string(fillChar, leftPad) + raw + new string(fillChar, rightPad),
                _ => new string(fillChar, totalPad) + raw
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
        if (i < spec.Length && spec[i] == '0' && result.Align == '\0')
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
        return d.ToString($"F{precision}", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatFloatSci(object value, int precision)
    {
        var d = Convert.ToDouble(value);
        // .NET 'e' format gives lowercase 'e'; normalize exponent to 2-digit minimum (Python style)
        string result = d.ToString($"e{precision}", System.Globalization.CultureInfo.InvariantCulture);
        int eIdx = result.IndexOf('e');
        if (eIdx >= 0)
        {
            string mantissa = result[..eIdx];
            string expPart = result[(eIdx + 1)..];
            char expSign = expPart[0];
            string digits = expPart[1..].TrimStart('0');
            if (digits.Length == 0) digits = "0";
            if (digits.Length < 2) digits = "0" + digits;
            result = mantissa + "e" + expSign + digits;
        }
        return result;
    }

    private static string FormatFloatGeneral(object value, int precision)
    {
        var d = Convert.ToDouble(value);
        return d.ToString($"G{precision}", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(object value, int precision)
    {
        var d = Convert.ToDouble(value) * 100;
        return d.ToString($"F{precision}", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }
}

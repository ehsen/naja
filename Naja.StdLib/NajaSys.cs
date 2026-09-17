using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python 'sys' module emulation.
/// Exposes argv, exit(), path, version, platform, maxsize, stdin/stdout/stderr stubs.
/// </summary>
public sealed class NajaSys
{
    public static readonly NajaSys Instance = new();

    // ── Command-line arguments ────────────────────────────────────────────────
    /// <summary>sys.argv — command-line arguments as a list of strings.</summary>
    public List<object> argv
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            return args.Select(a => (object)a).ToList();
        }
    }

    // ── Module search path ────────────────────────────────────────────────────
    /// <summary>sys.path — module search path (read-only stub returning ['.']).</summary>
    public List<object> path => new List<object> { "." };

    // ── Version information ───────────────────────────────────────────────────
    /// <summary>sys.version — Python-compatible version string.</summary>
    public string version => "3.11.0 (Naja .NET 10 Compiler)";

    /// <summary>sys.version_info — simplified tuple (major, minor, micro).</summary>
    public object[] version_info => new object[] { (long)3, (long)11, (long)0 };

    // ── Platform ─────────────────────────────────────────────────────────────
    /// <summary>sys.platform — 'win32', 'linux', 'darwin' etc.</summary>
    public string platform
    {
        get
        {
            if (OperatingSystem.IsWindows()) return "win32";
            if (OperatingSystem.IsMacOS())   return "darwin";
            return "linux";
        }
    }

    // ── Numeric limits ────────────────────────────────────────────────────────
    public long maxsize => long.MaxValue;

    // ── I/O streams ──────────────────────────────────────────────────────────
    public object stdin  => Console.In;
    public object stdout => Console.Out;
    public object stderr => Console.Error;

    // ── Process control ───────────────────────────────────────────────────────
    /// <summary>sys.exit([code]) — exit with optional exit code.</summary>
    public void exit()
    {
        System.Environment.Exit(0);
    }

    public void exit(object code)
    {
        int exitCode = (int)TypeCoercion.ToLong(code);
        System.Environment.Exit(exitCode);
    }

    // ── Executable path ───────────────────────────────────────────────────────
    public string executable => System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";

    // ── Recursion limit ───────────────────────────────────────────────────────
    public long getrecursionlimit() => 1000L;
    public void setrecursionlimit(object limit) { /* no-op */ }

    // ── Integer <-> string digit limit (PEP 618) ─────────────────────────────
    // Default and threshold must mirror CPython's _PY_LONG_DEFAULT_MAX_STR_DIGITS
    // (4300) and _PY_LONG_MAX_STR_DIGITS_THRESHOLD (640).
    internal const int IntMaxStrDigitsDefault = 4300;
    internal const int IntMaxStrDigitsThreshold = 640;
    internal static int _intMaxStrDigits = IntMaxStrDigitsDefault;

    /// <summary>sys.get_int_max_str_digits() — current non-binary int↔str limit.</summary>
    public long get_int_max_str_digits() => _intMaxStrDigits;

    /// <summary>sys.set_int_max_str_digits(maxdigits) — set the limit; 0 disables it,
    /// otherwise must be &gt;= 640 (the str_digits_check_threshold), else ValueError.</summary>
    public void set_int_max_str_digits(object maxdigits)
    {
        long n = TypeCoercion.ToLong(maxdigits);
        if (n != 0 && n < IntMaxStrDigitsThreshold)
            throw PythonException.ValueError($"maxdigits must be 0 or larger than {IntMaxStrDigitsThreshold}");
        _intMaxStrDigits = (int)n;
    }

    /// <summary>sys.int_info — mirrors CPython's sys.int_info() namedtuple subset
    /// relevant to the string-digit limit.</summary>
    public object int_info => new NajaSysIntInfo();

    /// <summary>sys.int_info value object (CPython namedtuple int_info).</summary>
    public sealed class NajaSysIntInfo
    {
        public long default_digits_limit => IntMaxStrDigitsDefault;
        public long str_digits_check_threshold => IntMaxStrDigitsThreshold;
        public long max_str_digits => _intMaxStrDigits;
    }

    /// <summary>
    /// Parse a string of ASCII digits in an arbitrary base (already sign-stripped,
    /// with underscores removed) into a long. Used by the compiled int() builtin.
    /// Throws FormatException on invalid input (callers convert to ValueError).
    /// </summary>
    public static long ParseStringToLong(string digits, long base_)
    {
        if (base_ == 10)
            return long.Parse(digits);
        if (base_ < 2 || base_ > 36)
            throw new System.FormatException();
        long value = 0;
        foreach (var c in digits)
        {
            int dv = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'z' => c - 'a' + 10,
                >= 'A' and <= 'Z' => c - 'A' + 10,
                _ => -1
            };
            if (dv < 0 || dv >= base_) throw new System.FormatException();
            value = checked(value * base_ + dv);
        }
        return value;
    }

    /// <summary>
    /// Enforce the non-binary int↔str digit limit for an input string (decimal /
    /// user-supplied base, i.e. NOT a power-of-2 base). Throws ValueError when the
    /// count of real digits (ignoring whitespace, +/- sign and underscores) exceeds
    /// the current limit, and the limit is &gt; 0. Base 0 is treated as "plain decimal"
    /// (auto-detection, still limited); any base in the power-of-2 set is unlimited.
    /// </summary>
    public static void CheckIntStrDigitLimit(string s, long explicitBase = 0)
    {
        int limit = _intMaxStrDigits;
        if (limit <= 0) return;
        // Power-of-2 bases are unlimited.
        if (explicitBase is 2 or 4 or 8 or 16 or 32)
            return;

        // Count real digits: strip surrounding whitespace, leading +/- sign, and
        // underscores are NOT counted (matches CPython). For a prefixed literal like
        // "0x..."/"0o..."/"0b..." the prefix chars are skipped naturally since they
        // are non-digits; the trailing digits still count toward the limit.
        var t = s.Trim();
        if (t.Length == 0) return;
        int idx = 0;
        if (t[idx] == '+' || t[idx] == '-') idx++;

        // Optional prefix (0x/0o/0b/0d). Digits are counted from the first digit
        // onward regardless, so we simply count all ASCII decimal digits.
        long digits = 0;
        for (; idx < t.Length; idx++)
        {
            char c = t[idx];
            if (c >= '0' && c <= '9')
                digits++;
        }
        if (digits > limit)
            throw PythonException.ValueError(
                $"Exceeds the limit ({limit} digits) for integer string conversion; use sys.set_int_max_str_digits() to increase the limit");
    }

    // ── Tracing (no-op stubs — Naja has no trace infrastructure) ─────────────
    private object? _traceFunc = null;
    /// <summary>sys.settrace(func) — set a trace function; no-op in Naja.</summary>
    public void settrace(object? func) { _traceFunc = func; }
    /// <summary>sys.gettrace() — return current trace function (always None in Naja).</summary>
    public object? gettrace() => _traceFunc;
    /// <summary>sys.getframe([depth]) — no-op stub returns None.</summary>
    public object? getframe() => null;
    public object? getframe(object depth) => null;
}


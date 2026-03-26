using System.Diagnostics.CodeAnalysis;

namespace Naja.StdLib.Core;

/// <summary>
/// Python 'sys' module emulation.
/// Exposes argv, exit(), path, version, platform, maxsize, stdin/stdout/stderr stubs.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
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
        int exitCode = Convert.ToInt32(code);
        System.Environment.Exit(exitCode);
    }

    // ── Executable path ───────────────────────────────────────────────────────
    public string executable => System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";

    // ── Recursion limit ───────────────────────────────────────────────────────
    public long getrecursionlimit() => 1000L;
    public void setrecursionlimit(object limit) { /* no-op */ }

    // ── Tracing (no-op stubs — Naja has no trace infrastructure) ─────────────
    private object? _traceFunc = null;
    /// <summary>sys.settrace(func) — set a trace function; no-op in Naja.</summary>
    public void settrace(object? func) { _traceFunc = func; }
    /// <summary>sys.settrace() — raises TypeError (matches CPython: requires exactly 1 argument).</summary>
    public void settrace() => throw new InvalidCastException("settrace() takes exactly one argument (0 given)");
    /// <summary>sys.gettrace() — return current trace function (always None in Naja).</summary>
    public object? gettrace() => _traceFunc;
}

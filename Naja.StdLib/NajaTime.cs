namespace Naja.StdLib;

/// <summary>
/// Python 'time' module emulation.
/// Singleton pattern consistent with the other monolithic stdlib modules.
/// </summary>
public sealed class NajaTime
{
    public static readonly NajaTime Instance = new();

    private static readonly long _monotonicStart = Environment.TickCount64;
    private static readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>time.sleep(seconds) — suspend the current thread.</summary>
    public void sleep(object seconds)
    {
        var s = seconds switch
        {
            null => 0.0,
            double d => d,
            long l => l,
            int i => (double)i,
            _ => Convert.ToDouble(seconds)
        };
        if (s <= 0) return;
        System.Threading.Thread.Sleep((int)Math.Min(s * 1000, int.MaxValue));
    }

    /// <summary>time.time() — seconds since the Unix epoch as a float.</summary>
    public double time()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>time.time_ns() — nanoseconds since the Unix epoch.</summary>
    public long time_ns()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;

    /// <summary>time.monotonic() — monotonically increasing clock (seconds).</summary>
    public double monotonic()
        => _sw.Elapsed.TotalSeconds;

    /// <summary>time.monotonic_ns() — monotonic clock in nanoseconds.</summary>
    public long monotonic_ns()
        => _sw.ElapsedTicks * 100L; // Stopwatch tick = 100ns

    /// <summary>time.process_time() — CPU time consumed by the process (seconds).</summary>
    public double process_time()
        => System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;

    /// <summary>time.perf_counter() — high-resolution performance clock (seconds).</summary>
    public double perf_counter()
        => _sw.Elapsed.TotalSeconds;

    /// <summary>time.gmtime()/localtime() stub — returns current UTC datetime parts.</summary>
    public object[] localtime(object seconds = null!)
    {
        var dt = seconds is null
            ? DateTime.Now
            : DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(seconds)).LocalDateTime;
        return new object[]
        {
            (long)dt.Year, (long)dt.Month, (long)dt.Day,
            (long)dt.Hour, (long)dt.Minute, (long)dt.Second,
        };
    }

    /// <summary>time.ctime() — string like 'Mon Sep  7 12:00:00 2026'.</summary>
    public string ctime(object seconds = null!)
    {
        var dt = seconds is null
            ? DateTime.Now
            : DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(seconds)).LocalDateTime;
        return dt.ToString("ddd MMM dd HH:mm:ss yyyy",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>time.strftime(format) — format the current local time.</summary>
    public string strftime(object format)
        => DateTime.Now.ToString(format?.ToString() ?? "",
            System.Globalization.CultureInfo.InvariantCulture);
}
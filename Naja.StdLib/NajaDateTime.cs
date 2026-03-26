namespace Naja.StdLib;

/// <summary>
/// Python 'datetime' module emulation using System.DateTime and System.TimeSpan.
/// Provides datetime, date, time, timedelta, and related types.
/// </summary>
public sealed class NajaDateTime
{
    public static readonly NajaDateTime Instance = new();

    // ── datetime class (mimics Python datetime.datetime) ──────────────────────

    /// <summary>
    /// Wraps System.DateTime to provide Python-compatible interface.
    /// Supports construction from (year, month, day[, hour, minute, second, microsecond]).
    /// </summary>
    public class datetime
    {
        public System.DateTime Value { get; }

        public datetime()
        {
            Value = System.DateTime.Now;
        }

        public datetime(object year, object month, object day)
        {
            int y = ToInt(year), m = ToInt(month), d = ToInt(day);
            Value = new System.DateTime(y, m, d);
        }

        public datetime(object year, object month, object day, object hour, object minute, object second)
        {
            int y = ToInt(year), mo = ToInt(month), da = ToInt(day);
            int h = ToInt(hour), mi = ToInt(minute), s = ToInt(second);
            Value = new System.DateTime(y, mo, da, h, mi, s);
        }

        public datetime(object year, object month, object day, object hour, object minute, object second, object microsecond)
        {
            int y = ToInt(year), mo = ToInt(month), da = ToInt(day);
            int h = ToInt(hour), mi = ToInt(minute), s = ToInt(second);
            int us = ToInt(microsecond);
            int ms = us / 1000;  // Convert microseconds to milliseconds
            Value = new System.DateTime(y, mo, da, h, mi, s, ms);
        }

        // ── Properties (Python datetime attributes) ──────────────────────────
        public long year => Value.Year;
        public long month => Value.Month;
        public long day => Value.Day;
        public long hour => Value.Hour;
        public long minute => Value.Minute;
        public long second => Value.Second;
        public long microsecond => Value.Millisecond * 1000;  // Approximate
        
        /// <summary>Return day of week (0=Monday, 6=Sunday).</summary>
        public long weekday()
        {
            int dow = (int)Value.DayOfWeek;
            return (dow + 6) % 7;  // Convert Sunday=0 to Sunday=6
        }

        /// <summary>Return ISO weekday (1=Monday, 7=Sunday).</summary>
        public long isoweekday()
        {
            int dow = (int)Value.DayOfWeek;
            return dow == 0 ? 7 : dow;
        }

        /// <summary>Format datetime as string using strftime format codes.</summary>
        public string strftime(object format)
        {
            string fmt = format?.ToString() ?? "";
            try
            {
                // Convert Python strftime format to .NET format
                return FormatDatetime(Value, fmt);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"strftime format error: {ex.Message}", ex);
            }
        }

        /// <summary>Return ISO format string (YYYY-MM-DD HH:MM:SS.ffffff).</summary>
        public string isoformat()
        {
            return Value.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
        }

        public override string ToString()
        {
            return isoformat();
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            return obj is datetime other && Value == other.Value;
        }
    }

    // ── date class (mimics Python datetime.date) ────────────────────────────

    public class date
    {
        public System.DateTime Value { get; }

        public date()
        {
            Value = System.DateTime.Now.Date;
        }

        public date(object year, object month, object day)
        {
            int y = ToInt(year), m = ToInt(month), d = ToInt(day);
            Value = new System.DateTime(y, m, d);
        }

        public long year => Value.Year;
        public long month => Value.Month;
        public long day => Value.Day;

        public long weekday()
        {
            int dow = (int)Value.DayOfWeek;
            return (dow + 6) % 7;
        }

        public long isoweekday()
        {
            int dow = (int)Value.DayOfWeek;
            return dow == 0 ? 7 : dow;
        }

        public string isoformat()
        {
            return Value.ToString("yyyy-MM-dd");
        }

        public override string ToString()
        {
            return isoformat();
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            return obj is date other && Value.Date == other.Value.Date;
        }
    }

    // ── timedelta class (mimics Python datetime.timedelta) ────────────────────

    public class timedelta
    {
        public System.TimeSpan Value { get; }

        public timedelta()
        {
            Value = System.TimeSpan.Zero;
        }

        public timedelta(object days)
        {
            long d = ToLong(days);
            Value = System.TimeSpan.FromDays(d);
        }

        public timedelta(object days, object seconds)
        {
            long d = ToLong(days);
            long s = ToLong(seconds);
            Value = System.TimeSpan.FromDays(d).Add(System.TimeSpan.FromSeconds(s));
        }

        public timedelta(object days, object seconds, object microseconds)
        {
            long d = ToLong(days);
            long s = ToLong(seconds);
            long us = ToLong(microseconds);
            Value = System.TimeSpan.FromDays(d)
                .Add(System.TimeSpan.FromSeconds(s))
                .Add(System.TimeSpan.FromTicks(us * 10));
        }

        public long days => (long)Value.Days;
        public long seconds => (long)Value.Seconds;
        public long microseconds => (long)(Value.Ticks % 10000000) / 10;

        public double total_seconds()
        {
            return Value.TotalSeconds;
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            return obj is timedelta other && Value == other.Value;
        }
    }

    // ── Helper methods ────────────────────────────────────────────────────────

    private static int ToInt(object? obj)
    {
        return obj switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s => int.Parse(s),
            null => 0,
            _ => Convert.ToInt32(obj)
        };
    }

    private static long ToLong(object? obj)
    {
        return obj switch
        {
            int i => i,
            long l => l,
            double d => (long)d,
            string s => long.Parse(s),
            null => 0,
            _ => Convert.ToInt64(obj)
        };
    }

    /// <summary>
    /// Convert Python strftime format codes to .NET DateTime.ToString format.
    /// Implements common format codes: %Y, %m, %d, %H, %M, %S, %A, %a, %B, %b, %I, %p, etc.
    /// </summary>
    private static string FormatDatetime(System.DateTime dt, string fmt)
    {
        var result = new System.Text.StringBuilder();
        int i = 0;
        while (i < fmt.Length)
        {
            if (fmt[i] == '%' && i + 1 < fmt.Length)
            {
                char code = fmt[i + 1];
                string replacement = code switch
                {
                    'Y' => dt.ToString("yyyy"),              // 4-digit year
                    'y' => dt.ToString("yy"),               // 2-digit year
                    'm' => dt.ToString("MM"),               // Month 01-12
                    'd' => dt.ToString("dd"),               // Day 01-31
                    'H' => dt.ToString("HH"),               // Hour 00-23
                    'M' => dt.ToString("mm"),               // Minute 00-59
                    'S' => dt.ToString("ss"),               // Second 00-59
                    'f' => dt.ToString("ffffff"),           // Microseconds
                    'z' => dt.ToString("zzz"),              // UTC offset
                    'Z' => dt.ToString("zzz"),              // Timezone name
                    'A' => dt.ToString("dddd"),             // Full weekday name
                    'a' => dt.ToString("ddd"),              // Abbreviated weekday
                    'B' => dt.ToString("MMMM"),             // Full month name
                    'b' => dt.ToString("MMM"),              // Abbreviated month
                    'I' => dt.ToString("hh"),               // Hour 01-12
                    'p' => dt.ToString("tt"),               // AM/PM
                    'j' => dt.DayOfYear.ToString("D3"),    // Day of year 001-366
                    'w' => ((int)dt.DayOfWeek).ToString(),  // Weekday 0-6
                    'U' => GetWeekNumber(dt, 0).ToString("D2"),  // Week 00-53 (Sunday)
                    'W' => GetWeekNumber(dt, 1).ToString("D2"),  // Week 00-53 (Monday)
                    '%' => "%",
                    _ => "%" + code  // Unknown format, return as-is
                };
                result.Append(replacement);
                i += 2;
            }
            else
            {
                result.Append(fmt[i]);
                i++;
            }
        }
        return result.ToString();
    }

    private static int GetWeekNumber(System.DateTime dt, int weekStartDay)
    {
        // weekStartDay: 0 = Sunday, 1 = Monday
        var jan1 = new System.DateTime(dt.Year, 1, 1);
        int jan1DayOfWeek = (int)jan1.DayOfWeek;
        int dayOfYear = dt.DayOfYear;
        
        // Calculate first week
        int firstWeekStart = (7 - jan1DayOfWeek + weekStartDay) % 7;
        if (firstWeekStart == 0) firstWeekStart = 7;
        
        if (dayOfYear < firstWeekStart)
            return 0;
        
        return (dayOfYear - firstWeekStart) / 7 + 1;
    }

    // ── Module-level functions ────────────────────────────────────────────────

    /// <summary>Get current date and time.</summary>
    public datetime now()
    {
        return new datetime();
    }

    /// <summary>Get current date.</summary>
    public date today()
    {
        return new date();
    }

    /// <summary>Create a datetime from a POSIX timestamp.</summary>
    public datetime fromtimestamp(object timestamp)
    {
        long ts = ToLong(timestamp);
        var dt = System.DateTime.UnixEpoch.AddSeconds(ts);
        return new datetime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
    }
}

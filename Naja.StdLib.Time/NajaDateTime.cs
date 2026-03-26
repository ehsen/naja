using System.Diagnostics.CodeAnalysis;

namespace Naja.StdLib.Time;

/// <summary>
/// Python 'datetime' module emulation using System.DateTime and System.TimeSpan.
/// Provides datetime, date, time, timedelta, and related types.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicNestedTypes)]
public sealed class NajaDateTime
{
    public static readonly NajaDateTime Instance = new();

    // ── datetime class (mimics Python datetime.datetime) ──────────────────────

    /// <summary>
    /// Wraps System.DateTime to provide Python-compatible interface.
    /// Supports construction from (year, month, day[, hour, minute, second, microsecond]).
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicConstructors)]
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

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicConstructors)]
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
            return obj is date other && Value == other.Value;
        }
    }

    // ── time class (mimics Python datetime.time) ────────────────────────────

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicConstructors)]
    public class time
    {
        private readonly int _hour, _minute, _second, _microsecond;

        public time()
        {
            _hour = 0;
            _minute = 0;
            _second = 0;
            _microsecond = 0;
        }

        public time(object hour, object minute, object second)
        {
            _hour = ToInt(hour);
            _minute = ToInt(minute);
            _second = ToInt(second);
            _microsecond = 0;
        }

        public time(object hour, object minute, object second, object microsecond)
        {
            _hour = ToInt(hour);
            _minute = ToInt(minute);
            _second = ToInt(second);
            _microsecond = ToInt(microsecond);
        }

        public long hour => _hour;
        public long minute => _minute;
        public long second => _second;
        public long microsecond => _microsecond;

        public string isoformat()
        {
            return $"{_hour:D2}:{_minute:D2}:{_second:D2}.{_microsecond:D6}";
        }

        public override string ToString() => isoformat();
    }

    // ── timedelta class (mimics Python datetime.timedelta) ──────────────────

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicConstructors)]
    public class timedelta
    {
        public System.TimeSpan Value { get; }

        public timedelta()
        {
            Value = System.TimeSpan.Zero;
        }

        public timedelta(object days)
        {
            Value = System.TimeSpan.FromDays(ToDouble(days));
        }

        public timedelta(object days, object seconds)
        {
            Value = System.TimeSpan.FromDays(ToDouble(days)) + System.TimeSpan.FromSeconds(ToDouble(seconds));
        }

        public timedelta(object days, object seconds, object microseconds)
        {
            Value = System.TimeSpan.FromDays(ToDouble(days)) 
                  + System.TimeSpan.FromSeconds(ToDouble(seconds))
                  + System.TimeSpan.FromMicroseconds(ToDouble(microseconds));
        }

        public long days => (long)Value.TotalDays;
        public long seconds => Value.Seconds;
        public long microseconds => Value.Microseconds;
        public double total_seconds()
        {
            return Value.TotalSeconds;
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }

    // ── Helper methods ───────────────────────────────────────────────────────

    private static int ToInt(object o) => o switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        _ => Convert.ToInt32(o)
    };

    private static double ToDouble(object o) => o switch
    {
        double d => d,
        long l => (double)l,
        int i => (double)i,
        _ => Convert.ToDouble(o)
    };

    private static string FormatDatetime(System.DateTime dt, string format)
    {
        // Convert Python strftime format to .NET format
        // This is a basic implementation; full Python strftime is more complex
        return dt.ToString(format);
    }
}

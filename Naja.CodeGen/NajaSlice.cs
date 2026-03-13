namespace Naja.CodeGen;

/// <summary>
/// Runtime representation of a Python slice object: a[start:stop:step]
/// Used when a SubscriptExpr contains a SliceExpr.
/// </summary>
public sealed class NajaSlice
{
    public object? Start { get; }
    public object? Stop  { get; }
    public object? Step  { get; }

    public NajaSlice(object? start, object? stop, object? step)
    {
        Start = start;
        Stop  = stop;
        Step  = step;
    }

    /// <summary>Apply this slice to a list.</summary>
    public System.Collections.Generic.List<object> ApplyToList(
        System.Collections.Generic.List<object> list)
    {
        int len   = list.Count;
        int step  = Step  is null ? 1   : Convert.ToInt32(Step);
        int start = Start is null ? (step > 0 ? 0 : len - 1) : NormalizeIndex(Convert.ToInt32(Start), len, step);
        int stop  = Stop  is null ? (step > 0 ? len : -1)    : NormalizeIndex(Convert.ToInt32(Stop),  len, step);

        var result = new System.Collections.Generic.List<object>();
        if (step > 0)
            for (int i = start; i < stop; i += step) result.Add(list[i]);
        else
            for (int i = start; i > stop; i += step) result.Add(list[i]);
        return result;
    }

    /// <summary>Apply this slice to a string.</summary>
    public string ApplyToString(string s)
    {
        int len   = s.Length;
        int step  = Step  is null ? 1   : Convert.ToInt32(Step);
        int start = Start is null ? (step > 0 ? 0 : len - 1) : NormalizeIndex(Convert.ToInt32(Start), len, step);
        int stop  = Stop  is null ? (step > 0 ? len : -1)    : NormalizeIndex(Convert.ToInt32(Stop),  len, step);

        var sb = new System.Text.StringBuilder();
        if (step > 0)
            for (int i = start; i < stop; i += step) sb.Append(s[i]);
        else
            for (int i = start; i > stop; i += step) sb.Append(s[i]);
        return sb.ToString();
    }

    private static int NormalizeIndex(int idx, int len, int step)
    {
        // Apply Python-style negative index wrapping
        if (idx < 0) idx += len;
        // For stop with negative step, allow -1 sentinel (meaning "include index 0")
        if (step < 0)
            return Math.Max(-1, Math.Min(idx, len - 1));
        return Math.Max(0, Math.Min(idx, len));
    }
}

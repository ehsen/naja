using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Python Match object wrapper for System.Text.RegularExpressions.Match.
/// Provides group(), groups(), start(), end(), and span() methods compatible with Python's Match API.
/// </summary>
public sealed class NajaMatch
{
    private readonly Match _m;

    public NajaMatch(Match m)
    {
        _m = m;
    }

    /// <summary>Return the matched string (entire match).</summary>
    public object group()
        => _m.Value;

    /// <summary>Return the matched string, or a specific group by number or name.</summary>
    public object group(object n)
    {
        int groupNum = Convert.ToInt32(n);
        if (groupNum < 0 || groupNum >= _m.Groups.Count)
            return null!;

        return _m.Groups[groupNum].Value;
    }

    /// <summary>Return a tuple of all the subgroups of the match (excluding group 0).</summary>
    public object[] groups()
        => _m.Groups.Cast<Group>()
               .Skip(1)
               .Select(g => (object)g.Value)
               .ToArray();

    /// <summary>Return the starting index of the entire match.</summary>
    public long start()
        => _m.Index;

    /// <summary>Return the starting index of a specific group.</summary>
    public long start(object n)
    {
        int groupNum = Convert.ToInt32(n);
        if (groupNum < 0 || groupNum >= _m.Groups.Count)
            return -1;

        return _m.Groups[groupNum].Index;
    }

    /// <summary>Return the ending index of the entire match.</summary>
    public long end()
        => _m.Index + _m.Length;

    /// <summary>Return the ending index of a specific group.</summary>
    public long end(object n)
    {
        int groupNum = Convert.ToInt32(n);
        if (groupNum < 0 || groupNum >= _m.Groups.Count)
            return -1;

        var g = _m.Groups[groupNum];
        return g.Index + g.Length;
    }

    /// <summary>Return a tuple of (start, end) indices of the entire match.</summary>
    public object[] span()
        => new object[] { start(), end() };

    /// <summary>Return a tuple of (start, end) indices of a specific group.</summary>
    public object[] span(object n)
        => new object[] { start(n), end(n) };
}


using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Python 're' module emulation via System.Text.RegularExpressions.
/// Provides search, match, compile, sub, and findall methods compatible with Python's re module.
/// </summary>
public sealed class NajaReModule
{
    public static readonly NajaReModule Instance = new();

    private static string S(object o) => o is string s ? s : o?.ToString() ?? "";

    /// <summary>Search for a pattern in text and return a match object or null.</summary>
    public NajaMatch? search(object pattern, object text)
    {
        var m = Regex.Match(S(text), S(pattern));
        return m.Success ? new NajaMatch(m) : null;
    }

    /// <summary>Match a pattern at the beginning of text.</summary>
    public NajaMatch? match(object pattern, object text)
    {
        var m = Regex.Match(S(text), "^(?:" + S(pattern) + ")");
        return m.Success ? new NajaMatch(m) : null;
    }

    /// <summary>Compile a pattern (simplified — returns pattern string for now).</summary>
    public object compile(object pattern) => pattern;

    /// <summary>Replace all occurrences of pattern with replacement in text.</summary>
    public string sub(object pattern, object repl, object text)
        => Regex.Replace(S(text), S(pattern), S(repl));

    /// <summary>Find all non-overlapping matches of pattern in text.</summary>
    public List<object> findall(object pattern, object text)
        => Regex.Matches(S(text), S(pattern)).Cast<Match>()
               .Select(m => (object)m.Value).ToList();
}

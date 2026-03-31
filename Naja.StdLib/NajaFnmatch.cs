using System.Text.RegularExpressions;

namespace Naja.StdLib;

/// <summary>
/// Python 'fnmatch' module emulation.
/// Provides Unix filename pattern matching using wildcards.
/// 
/// Supported patterns:
/// - * matches everything
/// - ? matches any single character
/// - [seq] matches any character in seq
/// - [!seq] matches any character not in seq
/// 
/// This is a minimal implementation focused on test_windows requirements:
/// - fnmatch.fnmatch(name, pat) → bool
/// - fnmatch.filter(names, pattern) → List[str]
/// - fnmatch.translate(pat) → regex pattern
/// </summary>
public sealed class NajaFnmatch
{
    public static readonly NajaFnmatch Instance = new();

    // Simple cache for translated patterns
    private readonly Dictionary<string, Regex> _patternCache = new();

    // ── Main functions ─────────────────────────────────────────────────

    /// <summary>
    /// Test whether filename matches pattern.
    /// Case-insensitive on Windows, case-sensitive on Unix.
    /// </summary>
    public bool fnmatch(object name, object pattern)
    {
        var nameStr = name?.ToString() ?? "";
        var patStr = pattern?.ToString() ?? "";

        // Case handling: case-insensitive on Windows
        var regexOptions = OperatingSystem.IsWindows()
            ? RegexOptions.IgnoreCase
            : RegexOptions.None;

        var regex = TranslatePattern(patStr, regexOptions);
        return regex.IsMatch(nameStr);
    }

    /// <summary>
    /// Filter a list of names by pattern.
    /// Returns names that match the pattern.
    /// </summary>
    public List<object> filter(object names, object pattern)
    {
        var patStr = pattern?.ToString() ?? "";
        var regexOptions = OperatingSystem.IsWindows()
            ? RegexOptions.IgnoreCase
            : RegexOptions.None;
        
        var regex = TranslatePattern(patStr, regexOptions);

        var result = new List<object>();

        // Handle iterable names
        if (names is List<object> list)
        {
            foreach (var name in list)
            {
                var nameStr = name?.ToString() ?? "";
                if (regex.IsMatch(nameStr))
                    result.Add(name);
            }
        }
        else if (names is string[] arr)
        {
            foreach (var name in arr)
            {
                if (regex.IsMatch(name))
                    result.Add(name);
            }
        }
        else if (names is object[] objArr)
        {
            foreach (var name in objArr)
            {
                var nameStr = name?.ToString() ?? "";
                if (regex.IsMatch(nameStr))
                    result.Add(name);
            }
        }
        else
        {
            // Try to enumerate
            var nameStr = names?.ToString() ?? "";
            if (regex.IsMatch(nameStr))
                result.Add(names);
        }

        return result;
    }

    /// <summary>
    /// Translate a shell pattern to a regular expression.
    /// Returns the regex string (not compiled).
    /// </summary>
    public string translate(object pattern)
    {
        var patStr = pattern?.ToString() ?? "";
        return PatternToRegex(patStr);
    }

    // ── Helper methods ─────────────────────────────────────────────────

    /// <summary>
    /// Convert fnmatch pattern to regex and cache it
    /// </summary>
    private Regex TranslatePattern(string pattern, RegexOptions options)
    {
        var cacheKey = pattern + "|" + options;
        
        if (_patternCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var regexPattern = PatternToRegex(pattern);
        var regex = new Regex($"^{regexPattern}$", options);
        
        _patternCache[cacheKey] = regex;
        return regex;
    }

    /// <summary>
    /// Convert fnmatch pattern to regex pattern
    /// </summary>
    private string PatternToRegex(string pattern)
    {
        var result = new System.Text.StringBuilder();
        int i = 0;

        while (i < pattern.Length)
        {
            char c = pattern[i];

            switch (c)
            {
                case '*':
                    // * matches everything except /
                    result.Append("[^/]*");
                    i++;
                    break;

                case '?':
                    // ? matches any single character except /
                    result.Append("[^/]");
                    i++;
                    break;

                case '[':
                    // Character class [abc] or [!abc]
                    i++;
                    if (i < pattern.Length && pattern[i] == '!')
                    {
                        result.Append("[^");
                        i++;
                    }
                    else
                    {
                        result.Append("[");
                    }

                    // Collect characters until ]
                    while (i < pattern.Length && pattern[i] != ']')
                    {
                        if (pattern[i] == '\\' && i + 1 < pattern.Length)
                        {
                            result.Append(Regex.Escape(pattern[i + 1].ToString()));
                            i += 2;
                        }
                        else
                        {
                            result.Append(Regex.Escape(pattern[i].ToString()));
                            i++;
                        }
                    }

                    result.Append("]");
                    if (i < pattern.Length)
                        i++; // Skip the closing ]
                    break;

                default:
                    // Escape special regex characters
                    result.Append(Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }

        return result.ToString();
    }
}

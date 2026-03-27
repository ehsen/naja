using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace Naja.StdLib.Text;

/// <summary>
/// Python 're' module emulation using System.Text.RegularExpressions.
/// Provides regex pattern matching, search, substitution, and related operations.
/// 
/// Implements the core API:
/// - compile(pattern, flags=0) -> Pattern
/// - search(pattern, string, flags=0) -> Match | None
/// - match(pattern, string, flags=0) -> Match | None
/// - fullmatch(pattern, string, flags=0) -> Match | None
/// - findall(pattern, string, flags=0) -> list[str | tuple[str, ...]]
/// - finditer(pattern, string, flags=0) -> Iterator[Match]
/// - split(pattern, string, maxsplit=0, flags=0) -> list[str]
/// - sub(pattern, repl, string, count=0, flags=0) -> str
/// - subn(pattern, repl, string, count=0, flags=0) -> tuple[str, int]
/// </summary>
[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicProperties |
    DynamicallyAccessedMemberTypes.PublicMethods |
    DynamicallyAccessedMemberTypes.PublicConstructors)]
public sealed class NajaRe
{
    public static readonly NajaRe Instance = new();

    // ── Regex Flags ───────────────────────────────────────────────────────────
    /// <summary>re.ASCII - Perform ASCII-only matching (default in .NET)</summary>
    public int ASCII => 256;

    /// <summary>re.IGNORECASE / re.I - Case-insensitive matching</summary>
    public int IGNORECASE => 2;

    /// <summary>re.LOCALE / re.L - Make \w, \W, \b, \B, \d, \D locale dependent</summary>
    public int LOCALE => 4;

    /// <summary>re.MULTILINE / re.M - ^ and $ match at line boundaries</summary>
    public int MULTILINE => 8;

    /// <summary>re.DOTALL / re.S - . matches newlines too</summary>
    public int DOTALL => 16;

    /// <summary>re.VERBOSE / re.X - Allow verbose patterns with comments</summary>
    public int VERBOSE => 64;

    /// <summary>re.UNICODE / re.U - Make \w, \W, \b, \B, \d, \D, \s, \S unicode aware</summary>
    public int UNICODE => 32;

    // ── Aliases ───────────────────────────────────────────────────────────────
    public int I => 2;     // Alias for IGNORECASE
    public int L => 4;     // Alias for LOCALE
    public int M => 8;     // Alias for MULTILINE
    public int S => 16;    // Alias for DOTALL
    public int X => 64;    // Alias for VERBOSE
    public int U => 32;    // Alias for UNICODE
    public int A => 256;   // Alias for ASCII

    // ── Module-level API ──────────────────────────────────────────────────────

    /// <summary>
    /// Compile a regex pattern string into a Pattern object.
    /// re.compile(pattern, flags=0) -> Pattern
    /// </summary>
    public NajaRePattern compile(object pattern, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        int flagValue = flags != null ? ToInt(flags) : 0;
        return new NajaRePattern(patternStr, flagValue);
    }

    /// <summary>
    /// Search for pattern at any location in string.
    /// re.search(pattern, string, flags=0) -> Match | None
    /// </summary>
    public object search(object pattern, object string_, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        string str = ToString(string_);
        int flagValue = flags != null ? ToInt(flags) : 0;
        var compiled = compile(patternStr, flagValue);
        return compiled.search(str);
    }

    /// <summary>
    /// Match pattern against string at the beginning.
    /// re.match(pattern, string, flags=0) -> Match | None
    /// </summary>
    public object match(object pattern, object string_, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        string str = ToString(string_);
        int flagValue = flags != null ? ToInt(flags) : 0;
        var compiled = compile(patternStr, flagValue);
        return compiled.match(str);
    }

    /// <summary>
    /// Match entire string against pattern.
    /// re.fullmatch(pattern, string, flags=0) -> Match | None
    /// </summary>
    public object fullmatch(object pattern, object string_, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        string str = ToString(string_);
        int flagValue = flags != null ? ToInt(flags) : 0;
        var compiled = compile(patternStr, flagValue);
        return compiled.fullmatch(str);
    }

    /// <summary>
    /// Find all non-overlapping matches.
    /// re.findall(pattern, string, flags=0) -> list[str | tuple[str, ...]]
    /// </summary>
    public List<object> findall(object pattern, object string_, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        string str = ToString(string_);
        int flagValue = flags != null ? ToInt(flags) : 0;
        var compiled = compile(patternStr, flagValue);
        return compiled.findall(str);
    }

    /// <summary>
    /// Find all non-overlapping matches as iterator.
    /// re.finditer(pattern, string, flags=0) -> Iterator[Match]
    /// </summary>
    public object finditer(object pattern, object string_, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        string str = ToString(string_);
        int flagValue = flags != null ? ToInt(flags) : 0;
        var compiled = compile(patternStr, flagValue);
        return compiled.finditer(str);
    }

    /// <summary>
    /// Split string by regex pattern.
    /// re.split(pattern, string, maxsplit=0, flags=0) -> list[str]
    /// </summary>
    public List<object> split(object pattern, object string_, object maxsplit = null!, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        string str = ToString(string_);
        int maxsplitVal = maxsplit != null ? ToInt(maxsplit) : 0;
        int flagValue = flags != null ? ToInt(flags) : 0;
        var compiled = compile(patternStr, flagValue);
        return compiled.split(str, maxsplitVal);
    }

    /// <summary>
    /// Replace pattern with replacement string.
    /// re.sub(pattern, repl, string, count=0, flags=0) -> str
    /// </summary>
    public string sub(object pattern, object repl, object string_, object count = null!, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        string replStr = ToString(repl);
        string str = ToString(string_);
        int countVal = count != null ? ToInt(count) : 0;
        int flagValue = flags != null ? ToInt(flags) : 0;
        var compiled = compile(patternStr, flagValue);
        return compiled.sub(replStr, str, countVal);
    }

    /// <summary>
    /// Replace pattern with replacement string and return (result, count).
    /// re.subn(pattern, repl, string, count=0, flags=0) -> tuple[str, int]
    /// </summary>
    public object[] subn(object pattern, object repl, object string_, object count = null!, object flags = null!)
    {
        string patternStr = ToPatternString(pattern);
        string replStr = ToString(repl);
        string str = ToString(string_);
        int countVal = count != null ? ToInt(count) : 0;
        int flagValue = flags != null ? ToInt(flags) : 0;
        var compiled = compile(patternStr, flagValue);
        return compiled.subn(replStr, str, countVal);
    }

    // ── Escape function ───────────────────────────────────────────────────────

    /// <summary>
    /// Escape special regex characters in a string.
    /// re.escape(pattern) -> str
    /// </summary>
    public string escape(object pattern)
    {
        string patternStr = ToString(pattern);
        return Regex.Escape(patternStr);
    }

    // ── Helper methods ────────────────────────────────────────────────────────

    private static string ToPatternString(object o) => ToString(o);

    private static string ToString(object o) => o switch
    {
        null => "",
        string s => s,
        _ => o.ToString() ?? ""
    };

    private static int ToInt(object o) => o switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        _ => Convert.ToInt32(o)
    };

    // ── Nested Pattern class ──────────────────────────────────────────────────

    /// <summary>
    /// Represents a compiled regular expression pattern.
    /// Created via re.compile() or used internally by module-level functions.
    /// </summary>
    public sealed class NajaRePattern
    {
        private readonly string _pattern;
        private readonly int _flags;
        private readonly Regex _regex;

        public NajaRePattern(string pattern, int flags)
        {
            _pattern = pattern;
            _flags = flags;
            _regex = CreateRegex(pattern, flags);
        }

        // ── Public API ────────────────────────────────────────────────────────
        public string pattern => _pattern;
        public int flags => _flags;

        public object search(object string_)
        {
            string str = ToString(string_);
            var match = _regex.Match(str);
            return match.Success ? new NajaReMatch(match, _regex, str) : (object)null;
        }

        public object match(object string_)
        {
            string str = ToString(string_);
            var match = _regex.Match(str);
            if (!match.Success || match.Index != 0)
                return null;
            return new NajaReMatch(match, _regex, str);
        }

        public object fullmatch(object string_)
        {
            string str = ToString(string_);
            var match = _regex.Match(str);
            if (!match.Success || match.Index != 0 || match.Length != str.Length)
                return null;
            return new NajaReMatch(match, _regex, str);
        }

        public List<object> findall(object string_)
        {
            string str = ToString(string_);
            var result = new List<object>();
            var groupCount = _regex.GetGroupNames().Length - 1; // -1 for group 0 (full match)

            foreach (Match match in _regex.Matches(str))
            {
                if (groupCount == 0)
                {
                    // No capturing groups: return strings
                    result.Add(match.Value);
                }
                else if (groupCount == 1)
                {
                    // One capturing group: return group value
                    result.Add(match.Groups[1].Value);
                }
                else
                {
                    // Multiple capturing groups: return tuple of group values
                    var groups = new object[groupCount];
                    for (int i = 1; i <= groupCount; i++)
                    {
                        groups[i - 1] = match.Groups[i].Value;
                    }
                    result.Add(groups);
                }
            }

            return result;
        }

        public object finditer(object string_)
        {
            string str = ToString(string_);
            var matches = _regex.Matches(str);
            var result = new List<object>();

            foreach (Match match in matches)
            {
                result.Add(new NajaReMatch(match, _regex, str));
            }

            return result; // Python would return an iterator; we return a list for simplicity
        }

        public List<object> split(object string_, int maxsplit)
        {
            string str = ToString(string_);
            var result = new List<object>();
            var splitStrings = maxsplit > 0 ? _regex.Split(str, maxsplit + 1) : _regex.Split(str);
            foreach (var part in splitStrings)
            {
                result.Add(part);
            }
            return result;
        }

        public string sub(object repl, object string_, int count)
        {
            string str = ToString(string_);
            string replStr = ToString(repl);

            if (count == 0)
            {
                return _regex.Replace(str, replStr);
            }

            int replacementCount = 0;
            return _regex.Replace(str, m =>
            {
                if (replacementCount >= count)
                    return m.Value;
                replacementCount++;
                return ExpandReplacement(replStr, m);
            });
        }

        public object[] subn(object repl, object string_, int count)
        {
            string str = ToString(string_);
            string replStr = ToString(repl);
            int replacementCount = 0;

            string result;
            if (count == 0)
            {
                result = _regex.Replace(str, m =>
                {
                    replacementCount++;
                    return ExpandReplacement(replStr, m);
                });
            }
            else
            {
                result = _regex.Replace(str, m =>
                {
                    if (replacementCount >= count)
                        return m.Value;
                    replacementCount++;
                    return ExpandReplacement(replStr, m);
                });
            }

            return new object[] { result, replacementCount };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Regex CreateRegex(string pattern, int flags)
        {
            var options = RegexOptions.None;

            // Map Python flags to .NET RegexOptions
            if ((flags & 2) != 0)  // IGNORECASE
                options |= RegexOptions.IgnoreCase;

            if ((flags & 8) != 0)  // MULTILINE
                options |= RegexOptions.Multiline;

            if ((flags & 16) != 0) // DOTALL
                options |= RegexOptions.Singleline;

            if ((flags & 64) != 0) // VERBOSE
                options |= RegexOptions.IgnorePatternWhitespace;

            return new Regex(pattern, options);
        }

        private static string ExpandReplacement(string replacement, Match match)
        {
            // Simple expansion: \1, \2 for groups
            string result = replacement;

            for (int i = match.Groups.Count - 1; i >= 0; i--)
            {
                result = result.Replace($"\\{i}", match.Groups[i].Value);
                result = result.Replace($"${i}", match.Groups[i].Value);
            }

            return result;
        }

        private static string ToString(object o) => o switch
        {
            null => "",
            string s => s,
            _ => o.ToString() ?? ""
        };
    }

    // ── Nested Match class ────────────────────────────────────────────────────

    /// <summary>
    /// Represents a successful regex match.
    /// Created via Pattern.search(), Pattern.match(), or Pattern.finditer().
    /// </summary>
    public sealed class NajaReMatch
    {
        private readonly Match _match;
        private readonly Regex _regex;
        private readonly string _string;

        public NajaReMatch(Match match, Regex regex, string matchString)
        {
            _match = match;
            _regex = regex;
            _string = matchString;
        }

        // ── Public API ────────────────────────────────────────────────────────
        public string group() => _match.Value;
        public string group(int num) => num >= 0 && num < _match.Groups.Count ? _match.Groups[num].Value : "";

        public int start() => _match.Index;
        public int start(int group) => group >= 0 && group < _match.Groups.Count ? _match.Groups[group].Index : -1;

        public int end() => _match.Index + _match.Length;
        public int end(int group) => 
            group >= 0 && group < _match.Groups.Count && _match.Groups[group].Success
                ? _match.Groups[group].Index + _match.Groups[group].Length
                : -1;

        public string string_ => _string;
        public object pattern => _regex.ToString();
        public int pos => 0;
        public int endpos => _string.Length;

        public object[] groups()
        {
            var result = new object[_match.Groups.Count - 1]; // -1 to skip group 0
            for (int i = 1; i < _match.Groups.Count; i++)
            {
                result[i - 1] = _match.Groups[i].Value;
            }
            return result;
        }

        public Dictionary<object, object> groupdict()
        {
            var result = new Dictionary<object, object>();
            foreach (var name in _regex.GetGroupNames())
            {
                if (name != "0") // Skip group 0
                {
                    result[(object)name] = (object)_match.Groups[name].Value;
                }
            }
            return result;
        }

        public string expand(object template)
        {
            string templateStr = ToString(template);
            // Simple expansion
            return templateStr.Replace("\\0", _match.Value);
        }

        private static string ToString(object o) => o switch
        {
            null => "",
            string s => s,
            _ => o.ToString() ?? ""
        };
    }
}

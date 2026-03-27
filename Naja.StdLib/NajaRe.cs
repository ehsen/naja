using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python 're' module emulation using System.Text.RegularExpressions.
/// 
/// Implements the core CPython re API:
///   compile(pattern, flags=0) -> Pattern
///   search(pattern, string, flags=0) -> Match | None
///   match(pattern, string, flags=0) -> Match | None
///   fullmatch(pattern, string, flags=0) -> Match | None
///   findall(pattern, string, flags=0) -> list
///   finditer(pattern, string, flags=0) -> list[Match]
///   split(pattern, string, maxsplit=0, flags=0) -> list
///   sub(pattern, repl, string, count=0, flags=0) -> str
///   subn(pattern, repl, string, count=0, flags=0) -> tuple[str, int]
///   escape(pattern) -> str
/// </summary>
[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicProperties |
    DynamicallyAccessedMemberTypes.PublicMethods |
    DynamicallyAccessedMemberTypes.PublicConstructors)]
public sealed class NajaRe
{
    public static readonly NajaRe Instance = new();

    // ── Regex Flags (match CPython re module constants) ────────────────────────
    public int ASCII     => 256;   // re.A / re.ASCII
    public int IGNORECASE => 2;    // re.I / re.IGNORECASE
    public int LOCALE    => 4;     // re.L / re.LOCALE
    public int MULTILINE => 8;     // re.M / re.MULTILINE
    public int DOTALL    => 16;    // re.S / re.DOTALL
    public int UNICODE   => 32;    // re.U / re.UNICODE
    public int VERBOSE   => 64;    // re.X / re.VERBOSE

    // ── Single-letter aliases ─────────────────────────────────────────────────
    public int A => 256;
    public int I => 2;
    public int L => 4;
    public int M => 8;
    public int S => 16;
    public int U => 32;
    public int X => 64;

    // ── Module-level API ──────────────────────────────────────────────────────

    /// <summary>re.compile(pattern, flags=0) -> Pattern</summary>
    public NajaRePattern compile(object pattern, object flags = null!)
    {
        string p = Str(pattern);
        int f = flags != null ? (int)TypeCoercion.ToLong(flags) : 0;
        return new NajaRePattern(p, f);
    }

    /// <summary>re.search(pattern, string, flags=0) -> Match | None</summary>
    public object search(object pattern, object string_, object flags = null!)
        => compile(pattern, flags).search(string_);

    /// <summary>re.match(pattern, string, flags=0) -> Match | None</summary>
    public object match(object pattern, object string_, object flags = null!)
        => compile(pattern, flags).match(string_);

    /// <summary>re.fullmatch(pattern, string, flags=0) -> Match | None</summary>
    public object fullmatch(object pattern, object string_, object flags = null!)
        => compile(pattern, flags).fullmatch(string_);

    /// <summary>re.findall(pattern, string, flags=0) -> list</summary>
    public List<object> findall(object pattern, object string_, object flags = null!)
        => compile(pattern, flags).findall(string_);

    /// <summary>re.finditer(pattern, string, flags=0) -> list[Match]</summary>
    public List<object> finditer(object pattern, object string_, object flags = null!)
        => compile(pattern, flags).finditer(string_);

    /// <summary>re.split(pattern, string, maxsplit=0, flags=0) -> list</summary>
    public List<object> split(object pattern, object string_, object maxsplit = null!, object flags = null!)
    {
        int ms = maxsplit != null ? (int)TypeCoercion.ToLong(maxsplit) : 0;
        return compile(pattern, flags).split(string_, ms);
    }

    /// <summary>re.sub(pattern, repl, string, count=0, flags=0) -> str</summary>
    public string sub(object pattern, object repl, object string_, object count = null!, object flags = null!)
    {
        int c = count != null ? (int)TypeCoercion.ToLong(count) : 0;
        return compile(pattern, flags).sub(repl, string_, c);
    }

    /// <summary>re.subn(pattern, repl, string, count=0, flags=0) -> tuple[str, int]</summary>
    public object[] subn(object pattern, object repl, object string_, object count = null!, object flags = null!)
    {
        int c = count != null ? (int)TypeCoercion.ToLong(count) : 0;
        return compile(pattern, flags).subn(repl, string_, c);
    }

    /// <summary>re.escape(pattern) -> str — escape special regex characters</summary>
    public string escape(object pattern) => Regex.Escape(Str(pattern));

    // ── Helpers ───────────────────────────────────────────────────────────────
    internal static string Str(object o) => o switch
    {
        null   => "",
        string s => s,
        _      => o.ToString() ?? ""
    };

    // ── Compiled Pattern ──────────────────────────────────────────────────────

    /// <summary>
    /// Python re.Pattern — a compiled regular expression.
    /// Created via re.compile() or used internally by module-level functions.
    /// </summary>
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.PublicConstructors)]
    public sealed class NajaRePattern
    {
        private readonly string _pattern;
        private readonly int _flags;
        private readonly Regex _regex;

        public NajaRePattern(string pattern, int flags)
        {
            _pattern = pattern;
            _flags   = flags;
            _regex   = BuildRegex(pattern, flags);
        }

        // ── CPython Pattern attributes ─────────────────────────────────────────
        public string pattern => _pattern;
        public int    flags   => _flags;

        // ── Core matching methods ──────────────────────────────────────────────

        public object search(object string_)
        {
            string s = Str(string_);
            Match m = _regex.Match(s);
            return m.Success ? (object)new NajaReMatch(m, _regex, s, 0) : null!;
        }

        public object match(object string_)
        {
            string s = Str(string_);
            Match m = _regex.Match(s);
            if (!m.Success || m.Index != 0) return null!;
            return new NajaReMatch(m, _regex, s, 0);
        }

        public object fullmatch(object string_)
        {
            string s = Str(string_);
            Match m = _regex.Match(s);
            if (!m.Success || m.Index != 0 || m.Length != s.Length) return null!;
            return new NajaReMatch(m, _regex, s, 0);
        }

        public List<object> findall(object string_)
        {
            string s = Str(string_);
            var result = new List<object>();

            // Get real group count (exclude group 0 — full match)
            string[] groupNames = _regex.GetGroupNames();
            int groupCount = groupNames.Count(n => n != "0" && !int.TryParse(n, out _) || int.TryParse(n, out int gi) && gi > 0);
            // Simpler: total groups minus 1 (group 0)
            int numGroups = _regex.GetGroupNumbers().Length - 1;

            foreach (Match m in _regex.Matches(s))
            {
                if (numGroups == 0)
                {
                    result.Add(m.Value);
                }
                else if (numGroups == 1)
                {
                    result.Add(m.Groups[1].Value);
                }
                else
                {
                    var tuple = new object[numGroups];
                    for (int i = 1; i <= numGroups; i++)
                        tuple[i - 1] = m.Groups[i].Value;
                    result.Add(tuple);
                }
            }

            return result;
        }

        public List<object> finditer(object string_)
        {
            string s = Str(string_);
            var result = new List<object>();
            foreach (Match m in _regex.Matches(s))
                result.Add(new NajaReMatch(m, _regex, s, 0));
            return result;
        }

        public List<object> split(object string_, int maxsplit = 0)
        {
            string s = Str(string_);
            var result = new List<object>();

            if (maxsplit <= 0)
            {
                foreach (var part in _regex.Split(s))
                    result.Add(part);
                return result;
            }

            // Manual split respecting maxsplit
            int splitsDone = 0;
            int lastEnd = 0;
            foreach (Match m in _regex.Matches(s))
            {
                if (splitsDone >= maxsplit) break;
                result.Add(s.Substring(lastEnd, m.Index - lastEnd));
                lastEnd = m.Index + m.Length;
                splitsDone++;
            }
            result.Add(s.Substring(lastEnd));
            return result;
        }

        public string sub(object repl, object string_, int count = 0)
        {
            string s    = Str(string_);
            string replStr = Str(repl);

            if (count <= 0)
                return _regex.Replace(s, m => ExpandBackreferences(replStr, m));

            int done = 0;
            return _regex.Replace(s, m =>
            {
                if (done >= count) return m.Value;
                done++;
                return ExpandBackreferences(replStr, m);
            });
        }

        public object[] subn(object repl, object string_, int count = 0)
        {
            string s       = Str(string_);
            string replStr = Str(repl);
            int replaced   = 0;

            string result;
            if (count <= 0)
            {
                result = _regex.Replace(s, m =>
                {
                    replaced++;
                    return ExpandBackreferences(replStr, m);
                });
            }
            else
            {
                result = _regex.Replace(s, m =>
                {
                    if (replaced >= count) return m.Value;
                    replaced++;
                    return ExpandBackreferences(replStr, m);
                });
            }

            return new object[] { result, (long)replaced };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Regex BuildRegex(string pattern, int flags)
        {
            var opts = RegexOptions.None;
            if ((flags & 2)  != 0) opts |= RegexOptions.IgnoreCase;          // IGNORECASE
            if ((flags & 8)  != 0) opts |= RegexOptions.Multiline;            // MULTILINE
            if ((flags & 16) != 0) opts |= RegexOptions.Singleline;           // DOTALL (. matches \n)
            if ((flags & 64) != 0) opts |= RegexOptions.IgnorePatternWhitespace; // VERBOSE
            return new Regex(pattern, opts);
        }

        private static string ExpandBackreferences(string replacement, Match m)
        {
            // Replace \1..\9 and \g<name> with captured group values
            var result = System.Text.RegularExpressions.Regex.Replace(
                replacement,
                @"\\(\d+)",
                mr =>
                {
                    int idx = int.Parse(mr.Groups[1].Value);
                    return idx < m.Groups.Count ? m.Groups[idx].Value : mr.Value;
                });

            // Named backreferences \g<name>
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"\\g<(\w+)>",
                mr =>
                {
                    string name = mr.Groups[1].Value;
                    var grp = m.Groups[name];
                    return grp?.Success == true ? grp.Value : mr.Value;
                });

            return result;
        }
    }

    // ── Match object ──────────────────────────────────────────────────────────

    /// <summary>
    /// Python re.Match — result of a successful regex match.
    /// </summary>
    [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods |
        DynamicallyAccessedMemberTypes.PublicConstructors)]
    public sealed class NajaReMatch
    {
        private readonly Match  _m;
        private readonly Regex  _re;
        private readonly string _string;
        private readonly int    _pos;

        public NajaReMatch(Match m, Regex re, string matchString, int pos)
        {
            _m      = m;
            _re     = re;
            _string = matchString;
            _pos    = pos;
        }

        // ── CPython Match attributes ───────────────────────────────────────────
        public string @string => _string;
        public int    pos     => _pos;
        public int    endpos  => _string.Length;
        public string re      => _re.ToString();

        // ── group()/groups() ──────────────────────────────────────────────────

        /// <summary>m.group(0) returns the full match; m.group(n) returns group n.</summary>
        public object group(object which = null!)
        {
            if (which == null) return _m.Value;
            if (which is long l)   return GroupVal((int)l);
            if (which is int i)    return GroupVal(i);
            if (which is string name) return GroupVal(name);
            return GroupVal((int)TypeCoercion.ToLong(which));
        }

        public object[] groups(object default_ = null!)
        {
            int n = _m.Groups.Count - 1;
            var result = new object[n];
            for (int i = 1; i <= n; i++)
            {
                var g = _m.Groups[i];
                result[i - 1] = g.Success ? (object)g.Value : default_ ?? (object)null!;
            }
            return result;
        }

        public Dictionary<object, object> groupdict(object default_ = null!)
        {
            var d = new Dictionary<object, object>();
            foreach (string name in _re.GetGroupNames())
            {
                if (int.TryParse(name, out _)) continue; // skip numeric groups
                var g = _m.Groups[name];
                d[(object)name] = g.Success ? (object)g.Value : default_ ?? (object)null!;
            }
            return d;
        }

        // ── start()/end()/span() ──────────────────────────────────────────────

        public int start(object group = null!)
        {
            var g = GetGroup(group);
            return g?.Success == true ? g.Index : -1;
        }

        public int end(object group = null!)
        {
            var g = GetGroup(group);
            return g?.Success == true ? g.Index + g.Length : -1;
        }

        public object[] span(object group = null!)
        {
            var g = GetGroup(group);
            if (g?.Success == true)
                return new object[] { (long)g.Index, (long)(g.Index + g.Length) };
            return new object[] { (long)-1, (long)-1 };
        }

        public string expand(object template)
        {
            // Delegate to regex's standard replacement expansion
            string t = Str(template);
            return _re.Replace(_string, t, 1, _m.Index);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private object GroupVal(int n) =>
            n >= 0 && n < _m.Groups.Count ? (object)_m.Groups[n].Value : null!;

        private object GroupVal(string name)
        {
            var g = _m.Groups[name];
            return g?.Success == true ? (object)g.Value : null!;
        }

        private Group? GetGroup(object which)
        {
            if (which == null) return _m.Groups[0];
            if (which is long l)   return _m.Groups[(int)l];
            if (which is int i)    return _m.Groups[i];
            if (which is string s) return _m.Groups[s];
            return _m.Groups[(int)TypeCoercion.ToLong(which)];
        }
    }
}

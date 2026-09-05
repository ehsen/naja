namespace Naja.CodeGen.Builtins;

/// <summary>
/// String utility functions matching Python's str methods.
/// These are called as static methods from emitted IL but also serve as implementations
/// for string method calls on Python str objects.
/// </summary>
public static class StringFunctions
{
    /// <summary>Convert a character code to its string representation.</summary>
    public static string Chr(object code) => ((char)Convert.ToInt32(code)).ToString();

    /// <summary>Get the character code of the first character in a string.</summary>
    public static long Ord(object c) => (long)((string)c)[0];

    /// <summary>Convert an integer to hexadecimal string (0x prefix).</summary>
    public static string Hex(object n) => "0x" + Convert.ToInt64(n).ToString("x");

    /// <summary>Convert an integer to binary string (0b prefix).</summary>
    public static string Bin(object n) => "0b" + Convert.ToString(Convert.ToInt64(n), 2);

    /// <summary>Convert an integer to octal string (0o prefix).</summary>
    public static string Oct(object n) => "0o" + Convert.ToString(Convert.ToInt64(n), 8);

    // ── String methods ────────────────────────────────────────────────────────

    /// <summary>Convert string to uppercase.</summary>
    public static string StrUpper(object s) => TypeConversion.ToStr(s).ToUpper();

    /// <summary>Convert string to lowercase.</summary>
    public static string StrLower(object s) => TypeConversion.ToStr(s).ToLower();

    /// <summary>Remove leading and trailing whitespace.</summary>
    public static string StrStrip(object s) => TypeConversion.ToStr(s).Trim();

    /// <summary>Remove leading whitespace.</summary>
    public static string StrLStrip(object s) => TypeConversion.ToStr(s).TrimStart();

    /// <summary>Remove trailing whitespace.</summary>
    public static string StrRStrip(object s) => TypeConversion.ToStr(s).TrimEnd();

    /// <summary>Test if string starts with prefix.</summary>
    public static bool StrStartsWith(object s, object p) => TypeConversion.ToStr(s).StartsWith(TypeConversion.ToStr(p));

    /// <summary>Test if string ends with suffix.</summary>
    public static bool StrEndsWith(object s, object p) => TypeConversion.ToStr(s).EndsWith(TypeConversion.ToStr(p));

    /// <summary>Test if all characters are digits.</summary>
    public static bool StrIsDigit(object s) => TypeConversion.ToStr(s).All(char.IsDigit);

    /// <summary>Test if all characters are letters.</summary>
    public static bool StrIsAlpha(object s) => TypeConversion.ToStr(s).All(char.IsLetter);

    /// <summary>Test if all characters are alphanumeric.</summary>
    public static bool StrIsAlNum(object s) => TypeConversion.ToStr(s).All(char.IsLetterOrDigit);

    /// <summary>Find the index of a substring (-1 if not found).</summary>
    public static long StrFind(object s, object sub) => TypeConversion.ToStr(s).IndexOf(TypeConversion.ToStr(sub));

    /// <summary>Find the index of a substring (throws ValueError if not found).</summary>
    public static long StrIndex(object s, object sub) =>
        TypeConversion.ToStr(s).Contains(TypeConversion.ToStr(sub)) ? TypeConversion.ToStr(s).IndexOf(TypeConversion.ToStr(sub)) : throw new Exception($"ValueError: substring not found");

    /// <summary>Replace all occurrences of old with new.</summary>
    public static string StrReplace(object s, object old, object @new) => TypeConversion.ToStr(s).Replace(TypeConversion.ToStr(old), TypeConversion.ToStr(@new));

    /// <summary>Center string in a field of given width with optional fill character.</summary>
    public static string StrCenter(object s, object w, object fill = null!) =>
        TypeConversion.ToStr(s).PadLeft((int)((Convert.ToInt64(w) + TypeConversion.ToStr(s).Length) / 2), (fill == null ? " " : TypeConversion.ToStr(fill))[0]).PadRight((int)Convert.ToInt64(w), (fill == null ? " " : TypeConversion.ToStr(fill))[0]);

    /// <summary>Left-justify string in a field of given width.</summary>
    public static string StrLJust(object s, object w, object fill = null!) =>
        TypeConversion.ToStr(s).PadRight((int)Convert.ToInt64(w), (fill == null ? " " : TypeConversion.ToStr(fill))[0]);

    /// <summary>Right-justify string in a field of given width.</summary>
    public static string StrRJust(object s, object w, object fill = null!) =>
        TypeConversion.ToStr(s).PadLeft((int)Convert.ToInt64(w), (fill == null ? " " : TypeConversion.ToStr(fill))[0]);

    /// <summary>Pad string with zeros on the left.</summary>
    public static string StrZFill(object s, object w) => TypeConversion.ToStr(s).PadLeft((int)Convert.ToInt64(w), '0');

    /// <summary>Count occurrences of a substring.</summary>
    public static long StrCount(object s, object sub) =>
        (TypeConversion.ToStr(s).Length - TypeConversion.ToStr(s).Replace(TypeConversion.ToStr(sub), "").Length) / (TypeConversion.ToStr(sub).Length > 0 ? TypeConversion.ToStr(sub).Length : 1);

    /// <summary>Join an iterable of strings with a separator.</summary>
    public static string StrJoin(object sep, object items) =>
        string.Join(TypeConversion.ToStr(sep), ((System.Collections.IEnumerable)items).Cast<object>().Select(TypeConversion.ToStr));

    /// <summary>Split a string by separator.</summary>
    public static System.Collections.Generic.List<object> StrSplit(object s, object sep = null!)
    {
        var str = TypeConversion.ToStr(s);
        var parts = sep is null || TypeConversion.ToStr(sep) == ""
            ? str.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            : str.Split(new[] { TypeConversion.ToStr(sep) }, StringSplitOptions.None);
        return new System.Collections.Generic.List<object>(parts.Cast<object>());
    }

    /// <summary>Split a string into lines.</summary>
    public static System.Collections.Generic.List<object> StrSplitLines(object s) =>
        TypeConversion.ToStr(s).Replace("\r\n", "\n").Split('\n')
            .Select(p => (object)p).ToList();

    /// <summary>Convert string to title case.</summary>
    public static string StrTitle(object s) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo
            .ToTitleCase(TypeConversion.ToStr(s).ToLower());

    /// <summary>Encode string to bytes with specified encoding (default: utf-8).</summary>
    public static byte[] StrEncode(object s, object encoding = null!)
    {
        var str = TypeConversion.ToStr(s);
        var enc = encoding is null ? "utf-8" : TypeConversion.ToStr(encoding).ToLower();
        return enc switch
        {
            "utf-8" or "utf8" => System.Text.Encoding.UTF8.GetBytes(str),
            "latin-1" or "latin1" or "iso-8859-1" => System.Text.Encoding.GetEncoding("iso-8859-1").GetBytes(str),
            "ascii" => System.Text.Encoding.ASCII.GetBytes(str),
            _ => throw new Exception($"LookupError: unknown encoding: {enc}")
        };
    }

    /// <summary>
    /// Decode bytes (or a string passthrough) to a string — data.decode(encoding, errors).
    /// Mirrors bytes.decode(); accepting a string receiver keeps the bridge uniform
    /// with the other Str* helpers (subprocess.check_output returns a string).
    /// errors is accepted and ignored (decoder always uses fallback replacement).
    /// </summary>
    public static string StrDecode(object s, object encoding = null!, object errors = null!)
    {
        var enc = encoding is null ? "utf-8" : TypeConversion.ToStr(encoding).ToLower();
        var encodingObj = enc switch
        {
            "utf-8" or "utf8" => System.Text.Encoding.UTF8,
            "latin-1" or "latin1" or "iso-8859-1" => System.Text.Encoding.GetEncoding("iso-8859-1"),
            "ascii" => System.Text.Encoding.ASCII,
            "mbcs" => System.Text.Encoding.Default,
            _ => throw new Exception($"LookupError: unknown encoding: {enc}")
        };
        return s switch
        {
            byte[] b => encodingObj.GetString(b),
            _ => TypeConversion.ToStr(s) // already a string — no-op; Naja check_output returns str
        };
    }

    /// <summary>String format method: "template".format(*args) with {!r} and {!s} conversion support.</summary>
    public static string StrFormat(object self, object[] args)
    {
        var template = TypeConversion.ToStr(self);
        var result = new System.Text.StringBuilder();
        int argIndex = 0;

        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    result.Append('{');
                    i++; // skip the second '{'
                    continue;
                }

                // Find the closing '}'
                int closeIdx = template.IndexOf('}', i);
                if (closeIdx == -1)
                    throw new Exception("ValueError: Single '{' encountered in format string");

                var placeholder = template.Substring(i + 1, closeIdx - i - 1);
                i = closeIdx;

                // Parse placeholder: [index][!conversion][:format_spec]
                string? conversion = null;
                string? formatSpec = null;
                int explicitIndex = -1;

                // Check for conversion (!r, !s, !a)
                int bangIdx = placeholder.IndexOf('!');
                if (bangIdx >= 0)
                {
                    conversion = placeholder.Substring(bangIdx + 1, 1);
                    placeholder = placeholder.Substring(0, bangIdx);
                }

                // Check for format spec (:...)
                int colonIdx = placeholder.IndexOf(':');
                if (colonIdx >= 0)
                {
                    formatSpec = placeholder.Substring(colonIdx + 1);
                    placeholder = placeholder.Substring(0, colonIdx);
                }

                // Parse index (empty string means auto-increment)
                if (string.IsNullOrEmpty(placeholder))
                    explicitIndex = argIndex++;
                else if (int.TryParse(placeholder, out var idx))
                    explicitIndex = idx;
                else
                    throw new Exception($"ValueError: Invalid format string: {{{placeholder}}}");

                if (explicitIndex < 0 || explicitIndex >= args.Length)
                    throw new Exception($"IndexError: tuple index out of range");

                var value = args[explicitIndex];

                // Apply conversion
                string formatted;
                if (conversion == "r")
                    formatted = TypeConversion.Repr(value);
                else if (conversion == "s")
                    formatted = TypeConversion.ToStr(value);
                else if (conversion == "a")
                    formatted = TypeConversion.Repr(value); // ascii() is like repr() but escapes non-ASCII
                else if (formatSpec is not null)
                    formatted = ExceptionHelpers.Format(value, formatSpec);
                else
                    formatted = TypeConversion.ToStr(value);

                result.Append(formatted);
            }
            else if (template[i] == '}')
            {
                if (i + 1 < template.Length && template[i + 1] == '}')
                {
                    result.Append('}');
                    i++; // skip the second '}'
                }
                else
                    throw new Exception("ValueError: Single '}' encountered in format string");
            }
            else
            {
                result.Append(template[i]);
            }
        }

        return result.ToString();
    }
}

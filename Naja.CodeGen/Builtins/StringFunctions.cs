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
}

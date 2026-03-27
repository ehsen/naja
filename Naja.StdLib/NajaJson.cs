using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python 'json' module emulation using System.Text.Json.
///
/// Implements the core CPython json API:
///   dumps(obj, *, skipkeys=False, ensure_ascii=True, check_circular=True,
///         allow_nan=True, cls=None, indent=None, separators=None,
///         default=None, sort_keys=False, **kw) -> str
///   loads(s) -> object
///   dump(obj, fp, ...) -> None     (writes to file-like object)
///   load(fp) -> object             (reads from file-like object)
///
/// CPython compatibility:
/// - None  → null
/// - True  → true
/// - False → false
/// - int   → number (no decimal point)
/// - float → number (with decimal point / exponential notation)
/// - str   → JSON string
/// - list/tuple → JSON array
/// - dict  → JSON object (keys converted to strings)
/// - JSONDecodeError raised for invalid JSON input
/// </summary>
[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicProperties |
    DynamicallyAccessedMemberTypes.PublicMethods |
    DynamicallyAccessedMemberTypes.PublicConstructors)]
public sealed class NajaJson
{
    public static readonly NajaJson Instance = new();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// json.dumps(obj, *, indent=None, sort_keys=False, ensure_ascii=True,
    ///            separators=None, default=None) -> str
    /// </summary>
    public string dumps(object? obj,
                        object? indent = null,
                        object? sort_keys = null,
                        object? ensure_ascii = null,
                        object? separators = null,
                        object? default_ = null)
    {
        bool sortKeys = sort_keys != null && TypeCoercion.ToBool(sort_keys);
        bool ensureAscii = ensure_ascii == null || TypeCoercion.ToBool(ensure_ascii);
        int? indentSize = indent == null ? null : (int)TypeCoercion.ToLong(indent);

        var sb = new StringBuilder();
        SerializeValue(obj, sb, indentSize, sortKeys, ensureAscii, default_, 0);
        return sb.ToString();
    }

    /// <summary>
    /// json.loads(s) -> object
    /// Raises json.JSONDecodeError (ValueError) on invalid JSON.
    /// </summary>
    public object? loads(object s)
    {
        string text = s is string st ? st : s?.ToString() ?? throw PythonException.TypeError("the JSON object must be str, bytes or bytearray, not NoneType");
        try
        {
            var doc = JsonDocument.Parse(text);
            return ConvertElement(doc.RootElement);
        }
        catch (JsonException ex)
        {
            throw PythonException.ValueError($"JSONDecodeError: {ex.Message}");
        }
    }

    /// <summary>
    /// json.dump(obj, fp, ...) -> None
    /// Writes serialized JSON to a file-like object (must have write(str) method).
    /// </summary>
    public object? dump(object? obj, object fp,
                        object? indent = null,
                        object? sort_keys = null,
                        object? ensure_ascii = null,
                        object? separators = null,
                        object? default_ = null)
    {
        string text = dumps(obj, indent, sort_keys, ensure_ascii, separators, default_);
        // Call fp.write(text) — fp is a NajaStringIO, NajaTextIOWrapper, or any object with write()
        var writeMethod = fp.GetType().GetMethod("write",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (writeMethod is null)
            throw PythonException.TypeError($"Object of type {fp.GetType().Name} has no write() method");
        writeMethod.Invoke(fp, new object[] { text });
        return null;
    }

    /// <summary>
    /// json.load(fp) -> object
    /// Reads JSON from a file-like object (must have read() method).
    /// </summary>
    public object? load(object fp)
    {
        var readMethod = fp.GetType().GetMethod("read",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (readMethod is null)
            throw PythonException.TypeError($"Object of type {fp.GetType().Name} has no read() method");
        var result = readMethod.Invoke(fp, new object?[] { null });
        return loads(result!);
    }

    // ── Serialization ─────────────────────────────────────────────────────────

    private static void SerializeValue(object? value, StringBuilder sb, int? indent,
                                        bool sortKeys, bool ensureAscii, object? defaultFunc,
                                        int depth)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;

            case bool b:
                sb.Append(b ? "true" : "false");
                break;

            case long n:
                sb.Append(n);
                break;

            case int i:
                sb.Append(i);
                break;

            case double d:
                if (double.IsNaN(d) || double.IsInfinity(d))
                    throw PythonException.ValueError($"Out of range float values are not JSON compliant");
                sb.Append(FormatDouble(d));
                break;

            case float f:
                sb.Append(FormatDouble(f));
                break;

            case string str:
                AppendJsonString(sb, str, ensureAscii);
                break;

            case List<object> list:
                SerializeList(list, sb, indent, sortKeys, ensureAscii, defaultFunc, depth);
                break;

            case object[] arr:
                SerializeList(arr.ToList(), sb, indent, sortKeys, ensureAscii, defaultFunc, depth);
                break;

            case Dictionary<object, object> dict:
                SerializeDict(dict, sb, indent, sortKeys, ensureAscii, defaultFunc, depth);
                break;

            default:
                // Try the default callable (for custom types)
                if (defaultFunc != null)
                {
                    object? converted = CallDefault(defaultFunc, value);
                    SerializeValue(converted, sb, indent, sortKeys, ensureAscii, defaultFunc, depth);
                    break;
                }
                throw PythonException.TypeError(
                    $"Object of type {value.GetType().Name} is not JSON serializable");
        }
    }

    private static void SerializeList(List<object> list, StringBuilder sb, int? indent,
                                       bool sortKeys, bool ensureAscii, object? defaultFunc, int depth)
    {
        sb.Append('[');
        for (int i = 0; i < list.Count; i++)
        {
            if (indent.HasValue)
            {
                sb.Append('\n');
                sb.Append(' ', (depth + 1) * indent.Value);
            }
            SerializeValue(list[i], sb, indent, sortKeys, ensureAscii, defaultFunc, depth + 1);
            if (i < list.Count - 1)
            {
                sb.Append(',');
                if (!indent.HasValue) sb.Append(' ');
            }
        }
        if (indent.HasValue && list.Count > 0)
        {
            sb.Append('\n');
            sb.Append(' ', depth * indent.Value);
        }
        sb.Append(']');
    }

    private static void SerializeDict(Dictionary<object, object> dict, StringBuilder sb,
                                       int? indent, bool sortKeys, bool ensureAscii,
                                       object? defaultFunc, int depth)
    {
        sb.Append('{');
        IEnumerable<KeyValuePair<object, object>> pairs = dict;
        if (sortKeys)
            pairs = dict.OrderBy(kv => kv.Key?.ToString() ?? "", StringComparer.Ordinal);

        bool first = true;
        foreach (var (k, v) in pairs)
        {
            if (!first)
            {
                sb.Append(',');
                if (!indent.HasValue) sb.Append(' ');
            }
            first = false;

            if (indent.HasValue)
            {
                sb.Append('\n');
                sb.Append(' ', (depth + 1) * indent.Value);
            }

            // Keys must be strings in JSON
            string keyStr = k switch
            {
                string s  => s,
                long n    => n.ToString(),
                double d  => d.ToString(),
                bool b    => b ? "true" : "false",
                null      => throw PythonException.TypeError("keys must be strings"),
                _         => throw PythonException.TypeError($"keys must be strings, not {k.GetType().Name}")
            };
            AppendJsonString(sb, keyStr, ensureAscii);
            sb.Append(indent.HasValue ? ": " : ": ");
            SerializeValue(v, sb, indent, sortKeys, ensureAscii, defaultFunc, depth + 1);
        }
        if (indent.HasValue && !first)
        {
            sb.Append('\n');
            sb.Append(' ', depth * indent.Value);
        }
        sb.Append('}');
    }

    private static void AppendJsonString(StringBuilder sb, string s, bool ensureAscii)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b");  break;
                case '\f': sb.Append("\\f");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (ensureAscii && c > 127)
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    private static string FormatDouble(double d)
    {
        // CPython formats floats without trailing zeros but always with a decimal point.
        // Round-trip format "R" preserves precision; we post-process to match CPython style.
        string s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        // If no decimal point or exponent, add ".0"
        if (!s.Contains('.') && !s.Contains('e') && !s.Contains('E') && !s.Contains('n') && !s.Contains('N'))
            s += ".0";
        return s;
    }

    private static object? CallDefault(object defaultFunc, object value)
    {
        // Try __call__ protocol (NajaFunction and user-defined callables)
        var callMethod = defaultFunc.GetType().GetMethod("__call__",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (callMethod is not null)
            return callMethod.Invoke(defaultFunc, new object[] { new object[] { value } });

        // Try raw Delegate.DynamicInvoke
        if (defaultFunc is Delegate del)
            return del.DynamicInvoke(value);

        // Try Invoke method (Func<object,object?> etc.)
        var invokeMethod = defaultFunc.GetType().GetMethod("Invoke",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (invokeMethod is not null)
            return invokeMethod.Invoke(defaultFunc, new object[] { value });

        throw PythonException.TypeError("default is not callable");
    }

    // ── Deserialization ───────────────────────────────────────────────────────

    private static object? ConvertElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Null    => null,
            JsonValueKind.True    => true,
            JsonValueKind.False   => false,
            JsonValueKind.Number  => ConvertNumber(el),
            JsonValueKind.String  => el.GetString()!,
            JsonValueKind.Array   => ConvertArray(el),
            JsonValueKind.Object  => ConvertObject(el),
            _                     => null
        };
    }

    private static object ConvertNumber(JsonElement el)
    {
        // CPython: integers stay int, floats get float
        string raw = el.GetRawText();
        if (!raw.Contains('.') && !raw.Contains('e') && !raw.Contains('E'))
        {
            if (el.TryGetInt64(out long lv)) return lv;
        }
        return el.GetDouble();
    }

    private static List<object> ConvertArray(JsonElement el)
    {
        var list = new List<object>();
        foreach (var item in el.EnumerateArray())
            list.Add(ConvertElement(item)!);
        return list;
    }

    private static Dictionary<object, object> ConvertObject(JsonElement el)
    {
        var dict = new Dictionary<object, object>();
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = ConvertElement(prop.Value)!;
        return dict;
    }
}

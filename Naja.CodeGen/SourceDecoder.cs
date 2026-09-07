using System.Text;
using System.Text.RegularExpressions;

namespace Naja.CodeGen;

/// <summary>
/// PEP 263 source decoding for Python source files.
///
/// Reads source as BYTES and decodes with the declared encoding:
///   1. A UTF-8 BOM wins (decoded as UTF-8, BOM stripped).
///   2. Otherwise, a `# coding: <name>` (or `-*- coding: name -*-`) declaration
///      in the first two lines selects the codec.
///   3. Default is UTF-8 (PEP 3120). All decodes are strict: invalid bytes
///      raise a SyntaxError whose message names the codec — CPython emits
///      e.g. "'utf-8' codec can't decode byte ... in position ...".
/// </summary>
public static class SourceDecoder
{
    // PEP 263: coding[:=]\s*([-\w.]+) on the first or second line, inside a comment.
    private static readonly Regex CodingPattern = new(
        @"^[ \t\f]*#.*?coding[:=][ \t]*([-_.a-zA-Z0-9]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Read a source file, honouring the BOM and coding declaration.
    /// </summary>
    public static string ReadFileText(string path) =>
        DecodeBytes(File.ReadAllBytes(path), out _);

    /// <summary>
    /// Decode source bytes per PEP 263. When <paramref name="declaredCoding"/>
    /// is non-null after the call, the source carried an explicit coding cookie.
    /// </summary>
    public static string DecodeBytes(byte[] bytes, out string? declaredCoding)
    {
        declaredCoding = null;

        // 1. UTF-8 BOM: BOM overrides any cookie (PEP 263).
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            try
            {
                return new UTF8Encoding(false, true)
                    .GetString(bytes, 3, bytes.Length - 3);
            }
            catch (DecoderFallbackException ex)
            {
                throw SyntaxErrorFor("utf-8", ex);
            }
        }

        // 2. Coding cookie on the first two lines?
        var cookie = FindCodingCookie(bytes);

        // 3. Default UTF-8 (PEP 3120), strict.
        if (cookie is null)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw SyntaxErrorFor("utf-8", ex);
            }
        }

        declaredCoding = cookie;
        var canonical = CanonicalizeCoding(cookie);

        if (canonical == "utf-8")
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw SyntaxErrorFor("utf-8", ex);
            }
        }

        var enc = ResolveEncoding(canonical, cookie);
        try
        {
            return enc.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw SyntaxErrorFor(cookie, ex);
        }
    }

    /// <summary>
    /// ASCII-decode just enough of the source to find a coding cookie on the
    /// first two lines. Bytes outside ASCII are preserved as '?' so the
    /// regex can still run; the cookie itself is ASCII by definition.
    /// </summary>
    private static string? FindCodingCookie(byte[] bytes)
    {
        // Grab up to the end of the second line (or first 512 bytes).
        int limit = Math.Min(bytes.Length, 512);
        int newlinesSeen = 0;
        int end = 0;
        for (int i = 0; i < limit; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                newlinesSeen++;
                if (newlinesSeen == 2) { end = i; break; }
            }
            end = i + 1;
        }

        var ascii = new StringBuilder(end);
        for (int i = 0; i < end; i++)
        {
            char c = bytes[i] < 128 ? (char)bytes[i] : '?';
            ascii.Append(c);
        }

        var text = ascii.ToString();
        foreach (var line in text.Split('\n'))
        {
            var m = CodingPattern.Match(line.TrimEnd('\r'));
            if (m.Success)
                return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>Normalize a Python codec alias to the canonical .NET name.</summary>
    private static string CanonicalizeCoding(string cookie)
    {
        var name = cookie.Trim().ToLowerInvariant().Replace('_', '-');
        return name switch
        {
            "utf-8" or "utf8" or "u8" or "utf"             => "utf-8",
            "latin-1" or "latin1" or "latin" or "l1" or "iso-8859-1" or "8859" or "cp819" or "iso8859-1" => "iso-8859-1",
            "ascii" or "us-ascii"                          => "ascii",
            "mbcs" or "ansi" or "cp1252" or "windows-1252" => "mbcs",
            "utf-16" or "utf16" or "utf-16le" or "utf-16be" => "utf-16",
            _ => name
        };
    }

    private static Encoding ResolveEncoding(string canonical, string originalCookie)
    {
        try
        {
            var enc = canonical switch
            {
                "utf-8"  => new UTF8Encoding(false, true),
                "mbcs"   => Encoding.Default,
                _        => Encoding.GetEncoding(canonical)
            };
            if (enc is UTF8Encoding)
                return enc;
            var strict = (Encoding)enc.Clone();
            strict.DecoderFallback = DecoderFallback.ExceptionFallback;
            return strict;
        }
        catch (ArgumentException)
        {
            throw new PythonExceptions.SyntaxErrorException(
                $"unknown encoding: {originalCookie}");
        }
    }

    /// <summary>
    /// Build the CPython-style decode failure message. Crucially the message
    /// CONTAINS the codec name — CPython tests assert on it (e.g. 'utf-8').
    /// </summary>
    private static PythonExceptions.SyntaxErrorException SyntaxErrorFor(
        string codec, DecoderFallbackException ex)
    {
        var byteRepr = ex.BytesUnknown is { Length: > 0 } b
            ? $"byte 0x{b[0]:X2}"
            : "byte";
        return new PythonExceptions.SyntaxErrorException(
            $"'{codec}' codec can't decode {byteRepr} in position {ex.Index}: {ex.Message}");
    }
}
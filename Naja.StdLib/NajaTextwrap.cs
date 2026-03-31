using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python textwrap module - text formatting and wrapping utilities.
/// Implements wrap, fill, and dedent functions for text manipulation.
/// </summary>
public class NajaTextwrap
{
    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    private static int I(object? o) => o switch
    {
        int i => i,
        long l => (int)l,
        _ => Convert.ToInt32(o)
    };

    /// <summary>
    /// Wrap text to fit within a specified width.
    /// Returns a list of wrapped lines, mirroring textwrap.wrap().
    /// </summary>
    public List<object> wrap(object text, object? width = null, object? expand_tabs = null,
                            object? replace_whitespace = null, object? drop_whitespace = null,
                            object? break_long_words = null, object? break_on_hyphens = null)
    {
        var txt = S(text);
        int w = width is not null ? I(width) : 70;
        bool expandTabs = expand_tabs is bool b1 ? b1 : true;
        bool replaceWhitespace = replace_whitespace is bool b2 ? b2 : true;
        bool dropWhitespace = drop_whitespace is bool b3 ? b3 : true;
        bool breakLongWords = break_long_words is bool b4 ? b4 : true;
        bool breakOnHyphens = break_on_hyphens is bool b5 ? b5 : true;

        if (expandTabs)
            txt = txt.Replace("\t", "    ");

        if (replaceWhitespace)
            txt = System.Text.RegularExpressions.Regex.Replace(txt, @"\s+", " ");

        var words = txt.Split(' ');
        var result = new List<object>();
        var currentLine = new StringBuilder();

        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word) && dropWhitespace)
                continue;

            if (currentLine.Length == 0)
            {
                currentLine.Append(word);
            }
            else if (currentLine.Length + 1 + word.Length <= w)
            {
                currentLine.Append(' ').Append(word);
            }
            else
            {
                if (currentLine.Length > 0)
                    result.Add((object)currentLine.ToString());
                
                if (word.Length > w && breakLongWords)
                {
                    // Break long words at width boundary
                    int remaining = word.Length;
                    int offset = 0;
                    while (remaining > w)
                    {
                        result.Add((object)word.Substring(offset, w));
                        offset += w;
                        remaining -= w;
                    }
                    currentLine = new StringBuilder(word.Substring(offset));
                }
                else
                {
                    currentLine = new StringBuilder(word);
                }
            }
        }

        if (currentLine.Length > 0)
            result.Add((object)currentLine.ToString());

        return result;
    }

    /// <summary>
    /// Fill text to fit within a specified width, returning a single wrapped string.
    /// Mirrors textwrap.fill().
    /// </summary>
    public string fill(object text, object? width = null, object? expand_tabs = null,
                      object? replace_whitespace = null, object? drop_whitespace = null,
                      object? break_long_words = null, object? break_on_hyphens = null)
    {
        var lines = wrap(text, width, expand_tabs, replace_whitespace, 
                        drop_whitespace, break_long_words, break_on_hyphens);
        return string.Join("\n", lines.Cast<string>());
    }

    /// <summary>
    /// Remove common leading whitespace from text.
    /// Mirrors textwrap.dedent().
    /// </summary>
    public string dedent(object text)
    {
        var txt = S(text);
        var lines = txt.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        if (lines.Length == 0)
            return txt;

        // Find minimum indentation (excluding blank lines)
        int minIndent = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            int indent = 0;
            while (indent < line.Length && char.IsWhiteSpace(line[indent]))
                indent++;

            if (indent < minIndent)
                minIndent = indent;
        }

        if (minIndent == int.MaxValue || minIndent == 0)
            return txt;

        // Remove common indentation
        var result = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                result.Append(line);
            }
            else if (line.Length >= minIndent)
            {
                result.Append(line.Substring(minIndent));
            }
            else
            {
                result.Append(line);
            }

            if (i < lines.Length - 1)
                result.AppendLine();
        }

        return result.ToString();
    }

    /// <summary>
    /// Indent all lines in text with the given prefix.
    /// Mirrors textwrap.indent() (Python 3.3+).
    /// </summary>
    public string indent(object text, object prefix, object? predicate = null)
    {
        var txt = S(text);
        var pfx = S(prefix);
        var lines = txt.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        var result = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrEmpty(lines[i]))
                result.Append(pfx);
            result.Append(lines[i]);
            if (i < lines.Length - 1)
                result.AppendLine();
        }

        return result.ToString();
    }

    /// <summary>
    /// Shorten text to fit within a specified width, adding ellipsis.
    /// Mirrors textwrap.shorten().
    /// </summary>
    public string shorten(object text, object width, object? placeholder = null)
    {
        var txt = S(text);
        int w = I(width);
        var ellipsis = placeholder is not null ? S(placeholder) : "[...]";

        if (txt.Length <= w)
            return txt;

        // Try to fit text within width minus ellipsis length
        int availableWidth = w - ellipsis.Length;
        if (availableWidth <= 0)
            return ellipsis;

        // Find last space within available width
        var shortened = txt.Substring(0, availableWidth).TrimEnd();
        int lastSpace = shortened.LastIndexOf(' ');
        if (lastSpace > 0)
            shortened = shortened.Substring(0, lastSpace);

        return shortened + ellipsis;
    }
}

using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Emits IL opcodes for f-string expressions and related formatting utilities.
/// </summary>
public sealed class FStringEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public FStringEmitters(EmitContext ctx, ExpressionEmitter mainEmitter) : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException("Use EmitFString directly");
    }

    /// <summary>
    /// Emits IL for f-string interpolation with format specs and conversions.
    /// </summary>
    public NajaType EmitFString(FStringExpr e)
    {
        // Parse {expr} segments out of the raw template and build string via concat
        var parts = ParseFStringParts(e.RawTemplate);

        if (parts.Count == 0)
        {
            IL.Emit(OpCodes.Ldstr, "");
            return NajaTypes.Str;
        }

        // Build via string.Concat on object[]
        IL.Emit(OpCodes.Ldc_I4, parts.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));

        var toStr = NajaBuiltinsMethodCache.ToStr_Method;
        var repr = NajaBuiltinsMethodCache.Repr_Method;
        var najaFormat = NajaBuiltinsMethodCache.Format_Method;

        for (int i = 0; i < parts.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);

            var (isExpr, text, formatSpec, conversion) = parts[i];
            if (isExpr)
            {
                // Parse and emit the expression inside {}
                try
                {
                    // Special-case: nested f-string literal like "f'...'". The lexer
                    // cannot lex nested quotes inside f-strings correctly here, so
                    // detect and emit a nested FStringExpr directly.
                    if (text.Length >= 2 && (text[0] == 'f' || text[0] == 'F') && (text[1] == '"' || text[1] == '\'') && text[^1] == text[1])
                    {
                        var inner = text.Substring(2, text.Length - 3);
                        var nested = new Naja.Parser.FStringExpr(inner, e.Line, e.Column);
                        var tn = _mainEmitter.Emit(nested);
                        TypeMapper.EmitBox(IL, tn);
                        if (conversion == 'r' || conversion == 'a')
                            IL.Emit(OpCodes.Call, repr);
                        else
                            IL.Emit(OpCodes.Call, toStr);
                        goto SKIP_PARSE_EXPR;
                    }

                    var tokens = new Naja.Lexer.Lexer(text).Tokenize();
                    var expr = new Naja.Parser.Parser(tokens).ParseExpression();

                    if (string.IsNullOrEmpty(formatSpec))
                    {
                        // Path 1: No format spec — just stringify
                        var t = _mainEmitter.Emit(expr);
                        TypeMapper.EmitBox(IL, t);
                        // Apply !r / !s / !a conversion
                        if (conversion == 'r' || conversion == 'a')
                            IL.Emit(OpCodes.Call, repr);
                        else
                            IL.Emit(OpCodes.Call, toStr);
                    }
                    else if (IsNetNativeSpec(formatSpec, out var csSpec))
                    {
                        // Path 2: .NET-native spec (d, f, e, g, n, x, X with no flags)
                        // Safe to use string.Format
                        IL.Emit(OpCodes.Ldstr, "{0:" + csSpec + "}");
                        var t = _mainEmitter.Emit(expr);
                        TypeMapper.EmitBox(IL, t);
                        IL.Emit(OpCodes.Call, typeof(string).GetMethod("Format", new[] { typeof(string), typeof(object) })!);
                    }
                    else
                    {
                        // Path 3: Python-semantic spec (b, o, %, #, +, fill/align, _, etc.)
                        // Route to NajaBuiltins.Format(value, spec)
                        var t = _mainEmitter.Emit(expr);
                        TypeMapper.EmitBox(IL, t);
                        if (formatSpec != null && formatSpec.Contains('{'))
                        {
                            // Evaluate nested replacement fields inside the format spec
                            _mainEmitter.Emit(new Naja.Parser.FStringExpr(formatSpec, e.Line, e.Column));
                        }
                        else
                        {
                            IL.Emit(OpCodes.Ldstr, formatSpec ?? "");
                        }
                        IL.Emit(OpCodes.Call, najaFormat);
                    }
                }
                catch
                {
                    IL.Emit(OpCodes.Ldstr, $"{{{text}}}");
                }
            SKIP_PARSE_EXPR: ;
            }
            else
            {
                IL.Emit(OpCodes.Ldstr, text);
            }

            IL.Emit(OpCodes.Stelem_Ref);
        }

        var concat = typeof(string).GetMethod("Concat", new[] { typeof(object[]) })!;
        IL.Emit(OpCodes.Call, concat);
        return NajaTypes.Str;
    }

    /// <summary>
    /// Parses f-string template into parts: literal strings and {expr:format!conversion} segments.
    /// </summary>
    private static List<(bool IsExpr, string Text, string? FormatSpec, char Conversion)> ParseFStringParts(string template)
    {
        var parts = new List<(bool, string, string?, char)>();
        var sb = new System.Text.StringBuilder();
        int i = 0;

        while (i < template.Length)
        {
            if (i + 1 < template.Length && template[i] == '{' && template[i + 1] == '{')
            {
                sb.Append('{'); i += 2;
            }
            else if (i + 1 < template.Length && template[i] == '}' && template[i + 1] == '}')
            {
                sb.Append('}'); i += 2;
            }
            else if (template[i] == '{')
            {
                if (sb.Length > 0) { parts.Add((false, sb.ToString(), null, '\0')); sb.Clear(); }
                i++;
                int depth = 1;
                bool inFormat = false;
                char conversion = '\0';
                var exprSb = new System.Text.StringBuilder();
                var fmtSb = new System.Text.StringBuilder();

                while (i < template.Length && depth > 0)
                {
                    char ch = template[i];
                    if (ch == '{') { depth++; }
                    else if (ch == '}') { depth--; if (depth == 0) break; }
                    else if (depth == 1 && ch == '!' && !inFormat)
                    {
                        // Peek for conversion char: r, s, or a
                        if (i + 1 < template.Length && (template[i + 1] == 'r' || template[i + 1] == 's' || template[i + 1] == 'a'))
                        {
                            conversion = template[i + 1];
                            i += 2;
                            continue;
                        }
                    }
                    else if (depth == 1 && ch == ':' && !inFormat)
                    {
                        inFormat = true; i++; continue;
                    }

                    if (depth > 0)
                    {
                        if (inFormat) fmtSb.Append(template[i++]);
                        else exprSb.Append(template[i++]);
                    }
                }
                parts.Add((true, exprSb.ToString(), inFormat ? fmtSb.ToString() : null, conversion));
                sb.Clear();
                i++; // skip closing }
            }
            else
            {
                sb.Append(template[i++]);
            }
        }

        if (sb.Length > 0) parts.Add((false, sb.ToString(), null, '\0'));
        return parts;
    }

    /// <summary>
    /// Classifies a Python format spec into one of three categories:
    /// - Returns true if the spec is safe for string.Format (only .NET-native codes with no flags)
    /// - Returns false if the spec requires routing to NajaBuiltins.Format (Python-semantic codes)
    /// </summary>
    private static bool IsNetNativeSpec(string pySpec, out string csSpec)
    {
        csSpec = null;
        if (string.IsNullOrEmpty(pySpec))
            return false;

        // Dynamic spec: contains nested {variable} references — must use Python runtime
        if (pySpec.Contains('{'))
            return false;

        // Last character is the Python type code
        char typeChar = pySpec[^1];

        // Always route to runtime helper for Python-only format codes
        if ("bos%".Contains(typeChar))
            return false; // binary, octal, string %, percent
        if (pySpec.Contains('_'))
            return false; // _ grouping separator
        if (pySpec.Contains('+'))
            return false; // explicit positive sign
        if (pySpec.Contains('#'))
            return false; // alternate form (0b, 0o, 0x prefix)
        if (HasFillAlign(pySpec))
            return false; // fill/align characters present

        // Safe subset: d, f, n, x, X with optional width.precision only
        csSpec = TranslateSimpleSpec(pySpec, typeChar);
        return csSpec != null;
    }

    /// <summary>
    /// Detects if a format spec contains fill/align characters: < > ^ =
    /// </summary>
    private static bool HasFillAlign(string spec)
    {
        return spec.IndexOfAny(new[] { '<', '>', '^', '=' }) >= 0;
    }

    /// <summary>
    /// Translates a whitelisted Python format spec to .NET composite format code.
    /// Example: "05d" -> "D5", ".2f" -> "F2"
    /// </summary>
    private static string TranslateSimpleSpec(string spec, char typeChar)
    {
        // Strip the type char, leaving optional [[0]width][.precision]
        string body = spec[..^1];
        return typeChar switch
        {
            // 'd': route all to Python runtime — .NET D format counts digits excluding sign,
            // but Python's width includes the sign (e.g. {:06d} for -42 = "-00042" not "-000042").
            'd' => null,
            'f' => ParseFP(body, 'F'),  // {x:.2f} → {0:F2}
            // 'e'/'g': route to Python runtime — .NET E format uses uppercase and may differ in exponent digits
            'e' => null,
            'g' => null,
            'n' => "N" + body,          // {n:n} → {0:N}
            'x' => "x" + body,          // {n:x} → {0:x}
            'X' => "X" + body,          // {n:X} → {0:X}
            _ => null                   // unknown — punt to runtime
        };
    }

    /// <summary>
    /// Extracts precision from Python float spec body.
    /// Examples: ".2" → "2", "10.2" → "2", "10" → "", "" → ""
    /// .NET composite format only takes precision in the format token, not width.
    /// </summary>
    private static string ParseFP(string body, char netType)
    {
        // body examples: ".2"  "10.2"  "10"  ""
        int dotIdx = body.IndexOf('.');
        if (dotIdx >= 0)
            return netType + body[(dotIdx + 1)..]; // extract precision digits only
        return netType.ToString();
    }
}

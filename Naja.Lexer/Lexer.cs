using Naja.Lexer;
using System.Text;

namespace Naja.Lexer;

/// <summary>
/// Naja Lexer — converts Python 3.14+ source into a flat token stream.
///
/// Handles:
///   • All Python literal types (int, float, string, f-string, bytes)
///   • Numeric literal separators: 1_000_000
///   • All operators and augmented assignments
///   • INDENT / DEDENT via IndentTracker
///   • Implicit line continuation inside () [] {}
///   • Explicit line continuation with backslash
///   • Triple-quoted strings (single and double)
///   • F-strings (basic — full nested expression support in Phase 2)
///   • Comments (consumed, not emitted by default)
/// </summary>
public sealed class Lexer
{
    private readonly string _source;
    private int _pos;
    private int _line;
    private int _col;
    private int _bracketDepth;   // () [] {} nesting — suppresses NEWLINE
    private bool _atLineStart;
    private readonly IndentTracker _indent = new();
    private readonly List<Token> _tokens = new();

    // ── Keyword table ────────────────────────────────────────────────────────
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        ["and"] = TokenType.And,
        ["as"] = TokenType.As,
        ["assert"] = TokenType.Assert,
        ["async"] = TokenType.Async,
        ["await"] = TokenType.Await,
        ["break"] = TokenType.Break,
        ["class"] = TokenType.Class,
        ["continue"] = TokenType.Continue,
        ["def"] = TokenType.Def,
        ["del"] = TokenType.Del,
        ["elif"] = TokenType.Elif,
        ["else"] = TokenType.Else,
        ["except"] = TokenType.Except,
        ["finally"] = TokenType.Finally,
        ["for"] = TokenType.For,
        ["from"] = TokenType.From,
        ["global"] = TokenType.Global,
        ["if"] = TokenType.If,
        ["import"] = TokenType.Import,
        ["in"] = TokenType.In,
        ["is"] = TokenType.Is,
        ["lambda"] = TokenType.Lambda,
        ["match"] = TokenType.Match,
        ["case"] = TokenType.Case,
        ["nonlocal"] = TokenType.Nonlocal,
        ["not"] = TokenType.Not,
        ["or"] = TokenType.Or,
        ["pass"] = TokenType.Pass,
        ["raise"] = TokenType.Raise,
        ["return"] = TokenType.Return,
        ["try"] = TokenType.Try,
        ["type"] = TokenType.Type,
        ["while"] = TokenType.While,
        ["with"] = TokenType.With,
        ["yield"] = TokenType.Yield,
        ["True"] = TokenType.True,
        ["False"] = TokenType.False,
        ["None"] = TokenType.None,
    };

    public Lexer(string source)
    {
        _source = source;
        _pos = 0;
        _line = 1;
        _col = 1;
        _atLineStart = true;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Tokenize the entire source and return an immutable token list.</summary>
    public IReadOnlyList<Token> Tokenize()
    {
        while (_pos < _source.Length)
        {
            if (_atLineStart)
                HandleLineStart();
            else
                ScanToken();
        }

        // Flush remaining DEDENTs at EOF
        foreach (var dedent in _indent.FlushEof(_line))
            _tokens.Add(dedent);

        _tokens.Add(Token.Eof);
        return _tokens;
    }

    // ── Line start — measure indentation ─────────────────────────────────────

    private void HandleLineStart()
    {
        _atLineStart = false;

        // Measure indentation
        int indentLevel = 0;
        int startPos = _pos;

        while (_pos < _source.Length && (_source[_pos] == ' ' || _source[_pos] == '\t'))
        {
            // Python spec: tabs are 8 spaces for indent purposes
            indentLevel += _source[_pos] == '\t' ? 8 : 1;
            Advance();
        }

        // Skip blank lines and comment-only lines — don't emit NEWLINE for them
        if (_pos < _source.Length && (_source[_pos] == '\n' || _source[_pos] == '\r'))
        {
            SkipNewline();
            _atLineStart = true;
            return;
        }
        if (_pos < _source.Length && _source[_pos] == '#')
        {
            SkipLineComment();
            _atLineStart = true;
            return;
        }
        if (_pos >= _source.Length)
            return;

        // Inside brackets — indentation is irrelevant
        if (_bracketDepth > 0)
            return;

        // Process indent change
        int result = _indent.Process(indentLevel, _line, _col);

        // Flush any pending DEDENTs first
        while (_indent.HasPending)
            _tokens.Add(_indent.DequeuePending());

        if (result > 0)
            _tokens.Add(MakeToken(TokenType.Indent, "", startPos, 1));
    }

    // ── Main scan loop ───────────────────────────────────────────────────────

    private void ScanToken()
    {
        // Flush any queued DEDENTs
        while (_indent.HasPending)
            _tokens.Add(_indent.DequeuePending());

        SkipWhitespace();

        if (_pos >= _source.Length)
            return;

        char c = Current();
        int start = _pos;
        int startCol = _col;

        // Comment
        if (c == '#')
        {
            SkipLineComment();
            return;
        }

        // Newline
        if (c == '\n' || c == '\r')
        {
            if (_bracketDepth == 0)
                _tokens.Add(MakeToken(TokenType.Newline, "\\n", _pos, startCol));
            SkipNewline();
            _atLineStart = true;
            return;
        }

        // Explicit line continuation
        if (c == '\\' && Peek(1) is '\n' or '\r')
        {
            Advance(); // skip backslash
            SkipNewline();
            return;
        }

        // Numbers
        if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(1))))
        {
            _tokens.Add(ScanNumber(start, startCol));
            return;
        }

        // Strings and f-strings
        if (c is '"' or '\'' ||
            (c is 'f' or 'F' or 'b' or 'B' or 'r' or 'R' or 'u' or 'U'
             && Peek(1) is '"' or '\''))
        {
            _tokens.Add(ScanString(start, startCol));
            return;
        }

        // Identifiers and keywords
        if (char.IsLetter(c) || c == '_')
        {
            _tokens.Add(ScanIdentifierOrKeyword(start, startCol));
            return;
        }

        // Operators and punctuation
        var opToken = ScanOperatorOrPunctuation(start, startCol);
        if (opToken is not null)
        {
            _tokens.Add(opToken);
            return;
        }

        // Unknown character
        _tokens.Add(MakeToken(TokenType.Unknown, c.ToString(), _pos, startCol));
        Advance();
    }

    // ── Number scanning ──────────────────────────────────────────────────────

    private Token ScanNumber(int start, int startCol)
    {
        var sb = new StringBuilder();
        bool isFloat = false;

        // Hex / Octal / Binary
        if (Current() == '0' && _pos + 1 < _source.Length)
        {
            char next = char.ToLower(_source[_pos + 1]);
            if (next == 'x') return ScanBaseLiteral(start, startCol, "0123456789abcdefABCDEF");
            if (next == 'o') return ScanBaseLiteral(start, startCol, "01234567");
            if (next == 'b') return ScanBaseLiteral(start, startCol, "01");
        }

        // Decimal integer or float
        while (_pos < _source.Length && (char.IsDigit(Current()) || Current() == '_'))
            sb.Append(Advance());

        if (_pos < _source.Length && Current() == '.')
        {
            if (Peek(1) != '.')
            {
                isFloat = true;
                sb.Append(Advance());
                while (_pos < _source.Length && (char.IsDigit(Current()) || Current() == '_'))
                    sb.Append(Advance());
            }
        }

        // Exponent
        if (_pos < _source.Length && Current() is 'e' or 'E')
        {
            isFloat = true;
            sb.Append(Advance());
            if (_pos < _source.Length && Current() is '+' or '-')
                sb.Append(Advance());
            while (_pos < _source.Length && char.IsDigit(Current()))
                sb.Append(Advance());
        }

        return MakeToken(isFloat ? TokenType.Float : TokenType.Integer, sb.ToString(), start, startCol);
    }

    private Token ScanBaseLiteral(int start, int startCol, string validChars)
    {
        var sb = new StringBuilder();
        sb.Append(_source[_pos]); Advance(); // 0
        sb.Append(_source[_pos]); Advance(); // x/o/b

        while (_pos < _source.Length && (validChars.Contains(Current()) || Current() == '_'))
            sb.Append(Advance());

        return MakeToken(TokenType.Integer, sb.ToString(), start, startCol);
    }

    // ── String scanning ──────────────────────────────────────────────────────

    private Token ScanString(int start, int startCol)
    {
        var prefix = new StringBuilder();

        // Collect prefix characters: f, b, r, u, F, B, R, U (and combos)
        while (_pos < _source.Length && "fFbBrRuU".Contains(Current()))
            prefix.Append(Advance());

        string pre = prefix.ToString().ToLower();
        bool isFStr = pre.Contains('f');
        bool isBStr = pre.Contains('b');
        bool isRaw = pre.Contains('r');

        char quote = Advance(); // ' or "
        bool triple = false;

        // Check for triple quote
        if (_pos + 1 < _source.Length && Current() == quote && Peek(1) == quote)
        {
            Advance(); Advance();
            triple = true;
        }

        var content = new StringBuilder();

        while (_pos < _source.Length)
        {
            char c = Current();

            if (c == '\\' && !isRaw)
            {
                // Check if this is a backslash-newline line continuation
                if (Peek(1) == '\n')
                {
                    // Skip the backslash and newline (line continuation)
                    Advance(); // skip backslash
                    Advance(); // skip \n
                    continue;
                }
                else if (Peek(1) == '\r')
                {
                    // Handle \r or \r\n
                    Advance(); // skip backslash
                    Advance(); // skip \r
                    if (Current() == '\n')
                        Advance(); // skip \n if present
                    continue;
                }
                // Regular escape sequence
                content.Append(Advance()); // backslash
                if (_pos < _source.Length)
                    content.Append(Advance()); // escaped char
                continue;
            }

            if (triple)
            {
                if (c == quote && Peek(1) == quote && Peek(2) == quote)
                {
                    Advance(); Advance(); Advance();
                    break;
                }
                content.Append(Advance());
            }
            else
            {
                if (c == quote) { Advance(); break; }
                if (c is '\n' or '\r')
                    throw new LexerException("Unterminated string literal", _line, _col);
                content.Append(Advance());
            }
        }

        var type = isFStr ? TokenType.FString
                 : isBStr ? TokenType.Bytes
                 : TokenType.String;

        return MakeToken(type, content.ToString(), start, startCol);
    }

    // ── Identifier / keyword scanning ───────────────────────────────────────

    private Token ScanIdentifierOrKeyword(int start, int startCol)
    {
        var sb = new StringBuilder();

        while (_pos < _source.Length && (char.IsLetterOrDigit(Current()) || Current() == '_'))
            sb.Append(Advance());

        string word = sb.ToString();

        if (Keywords.TryGetValue(word, out var kwType))
            return MakeToken(kwType, word, start, startCol);

        return MakeToken(TokenType.Identifier, word, start, startCol);
    }

    // ── Operator / punctuation scanning ─────────────────────────────────────

    private Token? ScanOperatorOrPunctuation(int start, int startCol)
    {
        char c = Current();
        char n = Peek(1);
        char n2 = Peek(2);

        // Three-char operators
        if (c == '*' && n == '*' && n2 == '=') return Eat(3, TokenType.DoubleStarEqual, "**=", start, startCol);
        if (c == '/' && n == '/' && n2 == '=') return Eat(3, TokenType.DoubleSlashEqual, "//=", start, startCol);
        if (c == '<' && n == '<' && n2 == '=') return Eat(3, TokenType.LeftShiftEqual, "<<=", start, startCol);
        if (c == '>' && n == '>' && n2 == '=') return Eat(3, TokenType.RightShiftEqual, ">>=", start, startCol);
        if (c == '.' && n == '.' && n2 == '.') return Eat(3, TokenType.Ellipsis, "...", start, startCol);

        // Two-char operators
        if (c == '*' && n == '*') return Eat(2, TokenType.DoubleStar, "**", start, startCol);
        if (c == '/' && n == '/') return Eat(2, TokenType.DoubleSlash, "//", start, startCol);
        if (c == '<' && n == '<') return Eat(2, TokenType.LeftShift, "<<", start, startCol);
        if (c == '>' && n == '>') return Eat(2, TokenType.RightShift, ">>", start, startCol);
        if (c == '<' && n == '=') return Eat(2, TokenType.LessEqual, "<=", start, startCol);
        if (c == '>' && n == '=') return Eat(2, TokenType.GreaterEqual, ">=", start, startCol);
        if (c == '=' && n == '=') return Eat(2, TokenType.Equal, "==", start, startCol);
        if (c == '!' && n == '=') return Eat(2, TokenType.NotEqual, "!=", start, startCol);
        if (c == '+' && n == '=') return Eat(2, TokenType.PlusEqual, "+=", start, startCol);
        if (c == '-' && n == '=') return Eat(2, TokenType.MinusEqual, "-=", start, startCol);
        if (c == '*' && n == '=') return Eat(2, TokenType.StarEqual, "*=", start, startCol);
        if (c == '/' && n == '=') return Eat(2, TokenType.SlashEqual, "/=", start, startCol);
        if (c == '%' && n == '=') return Eat(2, TokenType.PercentEqual, "%=", start, startCol);
        if (c == '@' && n == '=') return Eat(2, TokenType.AtEqual, "@=", start, startCol);
        if (c == '&' && n == '=') return Eat(2, TokenType.AmpersandEqual, "&=", start, startCol);
        if (c == '|' && n == '=') return Eat(2, TokenType.PipeEqual, "|=", start, startCol);
        if (c == '^' && n == '=') return Eat(2, TokenType.CaretEqual, "^=", start, startCol);
        if (c == ':' && n == '=') return Eat(2, TokenType.Walrus, ":=", start, startCol);
        if (c == '-' && n == '>') return Eat(2, TokenType.Arrow, "->", start, startCol);

        // Single-char
        return c switch
        {
            '+' => Eat(1, TokenType.Plus, "+", start, startCol),
            '-' => Eat(1, TokenType.Minus, "-", start, startCol),
            '*' => Eat(1, TokenType.Star, "*", start, startCol),
            '/' => Eat(1, TokenType.Slash, "/", start, startCol),
            '%' => Eat(1, TokenType.Percent, "%", start, startCol),
            '@' => Eat(1, TokenType.At, "@", start, startCol),
            '&' => Eat(1, TokenType.Ampersand, "&", start, startCol),
            '|' => Eat(1, TokenType.Pipe, "|", start, startCol),
            '^' => Eat(1, TokenType.Caret, "^", start, startCol),
            '~' => Eat(1, TokenType.Tilde, "~", start, startCol),
            '<' => Eat(1, TokenType.Less, "<", start, startCol),
            '>' => Eat(1, TokenType.Greater, ">", start, startCol),
            '=' => Eat(1, TokenType.Assign, "=", start, startCol),
            '(' => BracketOpen(TokenType.LeftParen, "(", start, startCol),
            ')' => BracketClose(TokenType.RightParen, ")", start, startCol),
            '[' => BracketOpen(TokenType.LeftBracket, "[", start, startCol),
            ']' => BracketClose(TokenType.RightBracket, "]", start, startCol),
            '{' => BracketOpen(TokenType.LeftBrace, "{", start, startCol),
            '}' => BracketClose(TokenType.RightBrace, "}", start, startCol),
            ',' => Eat(1, TokenType.Comma, ",", start, startCol),
            ':' => Eat(1, TokenType.Colon, ":", start, startCol),
            ';' => Eat(1, TokenType.Semicolon, ";", start, startCol),
            '.' => Eat(1, TokenType.Dot, ".", start, startCol),
            _ => null
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Token Eat(int count, TokenType type, string value, int start, int startCol)
    {
        for (int i = 0; i < count; i++) Advance();
        return MakeToken(type, value, start, startCol);
    }

    private Token BracketOpen(TokenType type, string value, int start, int startCol)
    {
        _bracketDepth++;
        return Eat(1, type, value, start, startCol);
    }

    private Token BracketClose(TokenType type, string value, int start, int startCol)
    {
        if (_bracketDepth > 0) _bracketDepth--;
        return Eat(1, type, value, start, startCol);
    }

    private Token MakeToken(TokenType type, string value, int start, int startCol) =>
        new(type, value, _line, startCol, start, _pos);

    private char Advance()
    {
        char c = _source[_pos++];
        if (c == '\n') { _line++; _col = 1; }
        else _col++;
        return c;
    }

    private char Current() =>
        _pos < _source.Length ? _source[_pos] : '\0';

    private char Peek(int offset)
    {
        int i = _pos + offset;
        return i < _source.Length ? _source[i] : '\0';
    }

    private void SkipWhitespace()
    {
        while (_pos < _source.Length && _source[_pos] is ' ' or '\t')
            Advance();
    }

    private void SkipLineComment()
    {
        while (_pos < _source.Length && _source[_pos] is not '\n' and not '\r')
            Advance();
    }

    private void SkipNewline()
    {
        // Advance() already increments _line when it consumes '\n'
        // so we must NOT manually increment here — that was the double-count bug
        if (_pos < _source.Length && _source[_pos] == '\r') Advance();
        if (_pos < _source.Length && _source[_pos] == '\n') Advance();
    }
}
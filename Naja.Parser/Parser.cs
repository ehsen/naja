using Naja.Lexer;

namespace Naja.Parser;

/// <summary>
/// Naja Parser — converts a flat token stream into a Python AST.
/// </summary>
public sealed partial class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _pos;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    public Module ParseModule()
    {
        int line = Current().Line, col = Current().Column;
        var body = ParseStatementList();
        Expect(TokenType.Eof);
        return new Module(body, line, col);
    }

    // ── Expression parsing methods extracted to Parser.ExpressionParser.cs

    private static long ParseInt(string raw)
    {
        raw = raw.Replace("_", "");
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(raw[2..], 16);
        if (raw.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(raw[2..], 8);
        if (raw.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(raw[2..], 2);
        return long.Parse(raw);
    }

    private static Expression ParseIntExpr(string raw, int line, int col)
    {
        raw = raw.Replace("_", "");
        // Always compute the unsigned value via BigInteger first.
        // Convert.ToInt64 for hex interprets the high bit as sign (two's complement)
        // which gives wrong results for Python literals like 0x8000000000000000.
        System.Numerics.BigInteger big;
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            // Prepend "0" so BigInteger.Parse treats the value as positive
            big = System.Numerics.BigInteger.Parse("0" + raw[2..],
                System.Globalization.NumberStyles.HexNumber);
        }
        else if (raw.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            big = System.Numerics.BigInteger.Zero;
            foreach (char c in raw[2..])
                big = big * 8 + (c - '0');
        }
        else if (raw.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            big = System.Numerics.BigInteger.Zero;
            foreach (char c in raw[2..])
                big = big * 2 + (c - '0');
        }
        else
        {
            big = System.Numerics.BigInteger.Parse(raw);
        }

        if (big <= long.MaxValue)
            return new IntLiteral((long)big, line, col);
        return new BigIntLiteral(big, line, col);
    }

    private Token Current() =>
        _pos < _tokens.Count ? _tokens[_pos] : Token.Eof;

    private Token Peek(int offset = 1) =>
        (_pos + offset) < _tokens.Count ? _tokens[_pos + offset] : Token.Eof;

    private TokenType PeekType(int offset = 1) => Peek(offset).Type;

    private Token Advance()
    {
        var t = Current();
        _pos++;
        return t;
    }

    private bool Check(TokenType type) => Current().Type == type;

    private bool Match(TokenType type)
    {
        if (!Check(type)) return false;
        Advance();
        return true;
    }

    private Token Expect(TokenType type)
    {
        if (!Check(type))
            throw Error($"Expected {type} but got '{Current().Value}' ({Current().Type})");
        return Advance();
    }

    private string ExpectIdentifier()
    {
        var t = Current();
        if (t.Type is TokenType.Identifier or TokenType.Match
                   or TokenType.Case or TokenType.Type)
            return Advance().Value;
        throw Error($"Expected identifier but got '{t.Value}' ({t.Type})");
    }

    private bool IsAtEnd() => Current().Type == TokenType.Eof;

    private bool CheckNewlineOrEof() =>
        Current().Type is TokenType.Newline or TokenType.Eof
                       or TokenType.Semicolon;

    private void SkipNewlines()
    {
        while (Current().Type is TokenType.Newline or TokenType.Semicolon)
            Advance();
    }

    private void SkipNewline()
    {
        if (Current().Type is TokenType.Newline or TokenType.Semicolon)
            Advance();
    }

    private ParseException Error(string msg) =>
        new(msg, Current().Line, Current().Column);
}
namespace Naja.Lexer;

/// <summary>
/// Immutable token produced by the Naja lexer.
/// Position info is preserved for error messages and source maps.
/// </summary>
public sealed record Token(
    TokenType Type,
    string    Value,       // raw text from source
    int       Line,        // 1-based
    int       Column,      // 1-based, character offset from line start
    int       StartPos,    // absolute offset in source string
    int       EndPos       // exclusive end offset
)
{
    public bool Is(TokenType type) => Type == type;

    public bool IsAny(params TokenType[] types)
    {
        foreach (var t in types)
            if (Type == t) return true;
        return false;
    }

    public bool IsKeyword() => Type is
        TokenType.And or TokenType.As or TokenType.Assert or
        TokenType.Async or TokenType.Await or TokenType.Break or
        TokenType.Class or TokenType.Continue or TokenType.Def or
        TokenType.Del or TokenType.Elif or TokenType.Else or
        TokenType.Except or TokenType.Finally or TokenType.For or
        TokenType.From or TokenType.Global or TokenType.If or
        TokenType.Import or TokenType.In or TokenType.Is or
        TokenType.Lambda or
        TokenType.Nonlocal or TokenType.Not or TokenType.Or or
        TokenType.Pass or TokenType.Raise or TokenType.Return or
        TokenType.Try or TokenType.While or
        TokenType.With or TokenType.Yield or
        TokenType.True or TokenType.False or TokenType.None;

    public bool IsLiteral() => Type is
        TokenType.Integer or TokenType.Float or
        TokenType.String or TokenType.FString or TokenType.Bytes or
        TokenType.True or TokenType.False or TokenType.None;

    public override string ToString() =>
        $"[{Type,-20} {Value,15}  L{Line}:C{Column}]";

    /// <summary>Sentinel EOF token.</summary>
    public static readonly Token Eof = new(TokenType.Eof, "", 0, 0, 0, 0);
}

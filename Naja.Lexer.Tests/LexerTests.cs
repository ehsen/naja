using Naja.Lexer;
using Xunit;

namespace Naja.Lexer.Tests;

public class LexerTests
{
    private static IReadOnlyList<Token> Lex(string source) =>
        new Lexer(source).Tokenize();

    private static TokenType[] Types(string source) =>
        Lex(source).Select(t => t.Type).ToArray();

    // ── Basic literals ────────────────────────────────────────────────────────

    [Fact]
    public void Integer_Decimal()
    {
        var tokens = Lex("42");
        Assert.Equal(TokenType.Integer, tokens[0].Type);
        Assert.Equal("42", tokens[0].Value);
    }

    [Fact]
    public void Integer_Hex() => Assert.Equal(TokenType.Integer, Lex("0xFF")[0].Type);

    [Fact]
    public void Integer_Binary() => Assert.Equal(TokenType.Integer, Lex("0b1010")[0].Type);

    [Fact]
    public void Integer_Octal() => Assert.Equal(TokenType.Integer, Lex("0o77")[0].Type);

    [Fact]
    public void Integer_Underscore_Separator()
    {
        var tokens = Lex("1_000_000");
        Assert.Equal(TokenType.Integer, tokens[0].Type);
        Assert.Equal("1_000_000", tokens[0].Value);
    }

    [Fact]
    public void Float_Basic() => Assert.Equal(TokenType.Float, Lex("3.14")[0].Type);

    [Fact]
    public void Float_Exponent() => Assert.Equal(TokenType.Float, Lex("1.5e10")[0].Type);

    [Fact]
    public void Float_Leading_Dot() => Assert.Equal(TokenType.Float, Lex(".5")[0].Type);

    [Fact]
    public void String_DoubleQuote()
    {
        var tokens = Lex("\"hello\"");
        Assert.Equal(TokenType.String, tokens[0].Type);
        Assert.Equal("hello", tokens[0].Value);
    }

    [Fact]
    public void String_SingleQuote()
    {
        var tokens = Lex("'world'");
        Assert.Equal(TokenType.String, tokens[0].Type);
    }

    [Fact]
    public void String_Triple_Double()
    {
        var tokens = Lex("\"\"\"multi\nline\"\"\"");
        Assert.Equal(TokenType.String, tokens[0].Type);
    }

    [Fact]
    public void FString_Prefix()
    {
        var tokens = Lex("f\"hello {name}\"");
        Assert.Equal(TokenType.FString, tokens[0].Type);
    }

    [Fact]
    public void Bytes_Prefix()
    {
        var tokens = Lex("b\"bytes\"");
        Assert.Equal(TokenType.Bytes, tokens[0].Type);
    }

    // ── Keywords ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("if",     TokenType.If)]
    [InlineData("else",   TokenType.Else)]
    [InlineData("elif",   TokenType.Elif)]
    [InlineData("def",    TokenType.Def)]
    [InlineData("class",  TokenType.Class)]
    [InlineData("return", TokenType.Return)]
    [InlineData("for",    TokenType.For)]
    [InlineData("while",  TokenType.While)]
    [InlineData("import", TokenType.Import)]
    [InlineData("match",  TokenType.Match)]
    [InlineData("case",   TokenType.Case)]
    [InlineData("type",   TokenType.Type)]
    [InlineData("async",  TokenType.Async)]
    [InlineData("await",  TokenType.Await)]
    [InlineData("True",   TokenType.True)]
    [InlineData("False",  TokenType.False)]
    [InlineData("None",   TokenType.None)]
    public void Keyword_Recognition(string word, TokenType expected)
    {
        var tokens = Lex(word);
        Assert.Equal(expected, tokens[0].Type);
    }

    // ── Operators ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(":=", TokenType.Walrus)]
    [InlineData("->", TokenType.Arrow)]
    [InlineData("**", TokenType.DoubleStar)]
    [InlineData("//", TokenType.DoubleSlash)]
    [InlineData("<<", TokenType.LeftShift)]
    [InlineData(">>", TokenType.RightShift)]
    [InlineData("...",TokenType.Ellipsis)]
    [InlineData("+=", TokenType.PlusEqual)]
    [InlineData("-=", TokenType.MinusEqual)]
    [InlineData("**=",TokenType.DoubleStarEqual)]
    [InlineData("//=",TokenType.DoubleSlashEqual)]
    [InlineData("<<=",TokenType.LeftShiftEqual)]
    [InlineData(">>=",TokenType.RightShiftEqual)]
    public void Operator_Recognition(string op, TokenType expected)
    {
        var tokens = Lex(op);
        Assert.Equal(expected, tokens[0].Type);
    }

    // ── Indentation ───────────────────────────────────────────────────────────

    [Fact]
    public void Indent_And_Dedent()
    {
        var source = "if True:\n    pass\n";
        var types  = Types(source);
        Assert.Contains(TokenType.Indent,  types);
        Assert.Contains(TokenType.Dedent,  types);
        Assert.Contains(TokenType.Newline, types);
    }

    [Fact]
    public void Nested_Indent()
    {
        var source = "if True:\n    if True:\n        pass\n";
        var types  = Types(source);
        int indents = types.Count(t => t == TokenType.Indent);
        int dedents = types.Count(t => t == TokenType.Dedent);
        Assert.Equal(2, indents);
        Assert.Equal(2, dedents); // balanced
    }

    [Fact]
    public void Implicit_Continuation_In_Parens()
    {
        // Inside (), newlines should NOT emit NEWLINE tokens
        var source = "x = (\n    1 +\n    2\n)\n";
        var types  = Types(source);
        // Should have only one NEWLINE at the end, not inside the parens
        int newlines = types.Count(t => t == TokenType.Newline);
        Assert.Equal(1, newlines);
    }

    [Fact]
    public void Inconsistent_Indent_Throws()
    {
        // Dedenting to a level that was never on the indent stack
        var source = "if True:\n    pass\n  bad\n";
        Assert.Throws<LexerException>(() => Lex(source));
    }

    // ── Full function ─────────────────────────────────────────────────────────

    [Fact]
    public void Full_Function_Definition()
    {
        var source = """
            def add(a: int, b: int) -> int:
                return a + b
            """;

        var tokens = Lex(source);
        var types  = tokens.Select(t => t.Type).ToList();

        Assert.Contains(TokenType.Def,        types);
        Assert.Contains(TokenType.Identifier, types);
        Assert.Contains(TokenType.Colon,      types);
        Assert.Contains(TokenType.Arrow,      types);
        Assert.Contains(TokenType.Return,     types);
        Assert.Contains(TokenType.Indent,     types);
        Assert.Contains(TokenType.Dedent,     types);
    }

    [Fact]
    public void Walrus_Operator_In_While()
    {
        var source = "while chunk := f.read(8192):\n    process(chunk)\n";
        var types  = Types(source);
        Assert.Contains(TokenType.Walrus, types);
    }

    // ── Position tracking ─────────────────────────────────────────────────────

    [Fact]
    public void Token_Line_Tracking()
    {
        var source = "x = 1\ny = 2\n";
        var tokens = Lex(source);
        var yToken = tokens.First(t => t.Value == "y");
        Assert.Equal(2, yToken.Line);
    }

    [Fact]
    public void Token_Column_Tracking()
    {
        var source = "x = 42";
        var tokens = Lex(source);
        var numToken = tokens.First(t => t.Type == TokenType.Integer);
        Assert.Equal(5, numToken.Column);
    }
}

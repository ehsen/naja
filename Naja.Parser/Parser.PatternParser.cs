using Naja.Lexer;
using System.Collections.Generic;
using System.Linq;

namespace Naja.Parser;

/// <summary>
/// Pattern parsing logic for Naja Parser.
/// Handles parsing of match patterns for pattern matching statements.
/// </summary>
public sealed partial class Parser
{
    private Pattern ParsePattern()
    {
        var pattern = ParsePatternSequenceElement();

        if (Check(TokenType.Comma))
        {
            var patterns = new List<Pattern> { pattern };
            while (Match(TokenType.Comma))
            {
                if (Check(TokenType.Colon) || Check(TokenType.If) || Check(TokenType.RightBracket) || Check(TokenType.RightParen)) break;
                patterns.Add(ParsePatternSequenceElement());
            }
            pattern = new SequencePattern(patterns, pattern.Line, pattern.Column);
        }

        return pattern;
    }

    private Pattern ParsePatternSequenceElement()
    {
        var pattern = ParseClosedPattern();

        if (Check(TokenType.Pipe))
        {
            var patterns = new List<Pattern> { pattern };
            while (Match(TokenType.Pipe))
                patterns.Add(ParseClosedPattern());
            pattern = new OrPattern(patterns, pattern.Line, pattern.Column);
        }

        if (Check(TokenType.As) || (Check(TokenType.Identifier) && Current().Value == "as"))
        {
            Advance();
            var name = ExpectIdentifier();
            pattern = new AsPattern(pattern, name, pattern.Line, pattern.Column);
        }

        return pattern;
    }

    private Pattern ParseClosedPattern()
    {
        var t = Current();

        if (Match(TokenType.LeftBracket))
        {
            var patterns = new List<Pattern>();
            if (!Check(TokenType.RightBracket))
            {
                do
                {
                    if (Match(TokenType.Star))
                    {
                        string? starName = Check(TokenType.Identifier) ? Advance().Value : null;
                        patterns.Add(new StarPattern(starName, t.Line, t.Column));
                    }
                    else
                    {
                        patterns.Add(ParsePatternSequenceElement());
                    }
                } while (Match(TokenType.Comma) && !Check(TokenType.RightBracket));
            }
            Expect(TokenType.RightBracket);
            return new SequencePattern(patterns, t.Line, t.Column);
        }

        if (Match(TokenType.LeftBrace))
        {
            var pairs = new List<(Expression, Pattern)>();
            string? rest = null;
            if (!Check(TokenType.RightBrace))
            {
                do
                {
                    if (Match(TokenType.DoubleStar))
                    {
                        if (Check(TokenType.Identifier) && Current().Value == "_") Advance();
                        else rest = ExpectIdentifier();
                        break;
                    }
                    var key = ParseExpression();
                    Expect(TokenType.Colon);
                    var val = ParsePatternSequenceElement();
                    pairs.Add((key, val));
                } while (Match(TokenType.Comma) && !Check(TokenType.RightBrace));
            }
            Expect(TokenType.RightBrace);
            return new MappingPattern(pairs, rest, t.Line, t.Column);
        }

        if (Match(TokenType.LeftParen))
        {
            var pattern = ParsePatternSequenceElement();
            if (Match(TokenType.Comma))
            {
                var patterns = new List<Pattern> { pattern };
                while (!Check(TokenType.RightParen))
                {
                    patterns.Add(ParsePatternSequenceElement());
                    if (!Match(TokenType.Comma)) break;
                }
                Expect(TokenType.RightParen);
                return new SequencePattern(patterns, t.Line, t.Column);
            }
            Expect(TokenType.RightParen);
            return pattern;
        }

        if (Check(TokenType.Identifier) && Current().Value == "_")
        {
            Advance();
            return new WildcardPattern(t.Line, t.Column);
        }

        if (Match(TokenType.Star))
        {
            if (Check(TokenType.Identifier) && Current().Value == "_")
            {
                Advance();
                return new CapturePattern("*", t.Line, t.Column);
            }
            var name = ExpectIdentifier();
            return new CapturePattern(name, t.Line, t.Column);
        }

        if (Check(TokenType.Identifier))
        {
            var nameToken = Advance();
            var sb = new System.Text.StringBuilder(nameToken.Value);
            Expression clsexpr = new NameExpr(nameToken.Value, nameToken.Line, nameToken.Column);

            while (Match(TokenType.Dot))
            {
                var attr = ExpectIdentifier();
                sb.Append('.').Append(attr);
                clsexpr = new AttributeExpr(clsexpr, attr, clsexpr.Line, clsexpr.Column);
            }

            if (Match(TokenType.LeftParen))
            {
                var pos = new List<Pattern>();
                var kw = new List<(string, Pattern)>();
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        if (Check(TokenType.Identifier) && Peek(1).Type == TokenType.Assign)
                        {
                            var kwname = Advance().Value;
                            Advance();
                            var p = ParsePatternSequenceElement();
                            kw.Add((kwname, p));
                        }
                        else
                        {
                            pos.Add(ParsePatternSequenceElement());
                        }
                    } while (Match(TokenType.Comma) && !Check(TokenType.RightParen));
                }
                Expect(TokenType.RightParen);
                return new ClassPattern(clsexpr, pos, kw, t.Line, t.Column);
            }
            else
            {
                if (clsexpr is AttributeExpr)
                    return new LiteralPattern(clsexpr, t.Line, t.Column);
                return new CapturePattern(nameToken.Value, t.Line, t.Column);
            }
        }

        var expr = ParsePostfix();
        return new LiteralPattern(expr, t.Line, t.Column);
    }
}

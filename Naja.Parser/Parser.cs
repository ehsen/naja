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

    private IReadOnlyList<Statement> ParseStatementList()
    {
        var stmts = new List<Statement>();
        while (!IsAtEnd() && !Check(TokenType.Dedent))
        {
            SkipNewlines();
            if (IsAtEnd() || Check(TokenType.Dedent)) break;
            stmts.Add(ParseStatement());
        }
        return stmts;
    }

    private IReadOnlyList<Statement> ParseBlock()
    {
        Expect(TokenType.Colon);

        // Handle single-line blocks (e.g. `if True: pass` or `case 1: return`)
        if (!Check(TokenType.Newline) && !Check(TokenType.Eof))
        {
            var stmts = new List<Statement>();
            while (!CheckNewlineOrEof())
            {
                stmts.Add(ParseSimpleStatement());
                if (Match(TokenType.Semicolon))
                {
                    if (CheckNewlineOrEof()) break;
                }
                else
                {
                    break;
                }
            }
            SkipNewline();
            return stmts;
        }

        SkipNewlines();
        Expect(TokenType.Indent);
        var stmtsList = ParseStatementList();
        SkipNewlines();
        if (Check(TokenType.Dedent)) Advance();
        return stmtsList;
    }

    private Statement ParseStatement()
    {
        SkipNewlines();
        var tok = Current();

        var stmt = tok.Type switch
        {
            TokenType.If => ParseIf(),
            TokenType.While => ParseWhile(),
            TokenType.For => ParseFor(),
            TokenType.Try => ParseTry(),
            TokenType.With => ParseWith(),
            TokenType.Def => ParseFunctionDef(isAsync: false),
            TokenType.Class => ParseClassDef(),
            TokenType.Return => ParseReturn(),
            TokenType.Raise => ParseRaise(),
            TokenType.Del => ParseDelete(),
            TokenType.Assert => ParseAssert(),
            TokenType.Pass => ParsePass(),
            TokenType.Break => ParseBreak(),
            TokenType.Continue => ParseContinue(),
            TokenType.Global => ParseGlobal(),
            TokenType.Nonlocal => ParseNonlocal(),
            TokenType.Import => ParseImport(),
            TokenType.From => ParseFromImport(),
            TokenType.At => ParseDecorated(),
            TokenType.Match => ParseMatch(),
            TokenType.Type => ParseTypeAlias(),
            TokenType.Async => ParseAsync(),
            _ => ParseExpressionStatement(),
        };

        SkipNewlines();
        return stmt;
    }

    private Statement ParsePass()
    {
        var t = Expect(TokenType.Pass);
        SkipNewline();
        return new PassStatement(t.Line, t.Column);
    }

    private Statement ParseBreak()
    {
        var t = Expect(TokenType.Break);
        SkipNewline();
        return new BreakStatement(t.Line, t.Column);
    }

    private Statement ParseContinue()
    {
        var t = Expect(TokenType.Continue);
        SkipNewline();
        return new ContinueStatement(t.Line, t.Column);
    }

    private Statement ParseReturn()
    {
        var t = Expect(TokenType.Return);
        Expression? value = null;
        if (!CheckNewlineOrEof())
            value = ParseTupleOrExpression();
        SkipNewline();
        return new ReturnStatement(value, t.Line, t.Column);
    }

    private Statement ParseRaise()
    {
        var t = Expect(TokenType.Raise);
        Expression? exc = null, cause = null;
        if (!CheckNewlineOrEof())
        {
            exc = ParseExpression();
            if (Match(TokenType.From))
                cause = ParseExpression();
        }
        SkipNewline();
        return new RaiseStatement(exc, cause, t.Line, t.Column);
    }

    private Statement ParseDelete()
    {
        var t = Expect(TokenType.Del);
        var targets = ParseExpressionList();
        SkipNewline();
        return new DeleteStatement(targets, t.Line, t.Column);
    }

    private Statement ParseAssert()
    {
        var t = Expect(TokenType.Assert);
        var test = ParseExpression();
        Expression? msg = null;
        if (Match(TokenType.Comma))
            msg = ParseExpression();
        SkipNewline();
        return new AssertStatement(test, msg, t.Line, t.Column);
    }

    private Statement ParseGlobal()
    {
        var t = Expect(TokenType.Global);
        var names = ParseNameList();
        SkipNewline();
        return new GlobalStatement(names, t.Line, t.Column);
    }

    private Statement ParseNonlocal()
    {
        var t = Expect(TokenType.Nonlocal);
        var names = ParseNameList();
        SkipNewline();
        return new NonlocalStatement(names, t.Line, t.Column);
    }

    private IReadOnlyList<string> ParseNameList()
    {
        var names = new List<string> { ExpectIdentifier() };
        while (Match(TokenType.Comma))
            names.Add(ExpectIdentifier());
        return names;
    }

    private Statement ParseImport()
    {
        var t = Expect(TokenType.Import);
        var aliases = new List<ImportAlias>();
        do
        {
            var name = ParseDottedName();
            string? alias = null;
            if (Match(TokenType.As))
                alias = ExpectIdentifier();
            aliases.Add(new ImportAlias(name, alias));
        } while (Match(TokenType.Comma));
        SkipNewline();
        return new ImportStatement(aliases, t.Line, t.Column);
    }

    private Statement ParseFromImport()
    {
        var t = Expect(TokenType.From);
        int level = 0;
        while (Match(TokenType.Dot)) level++;

        string module = "";
        if (Check(TokenType.Identifier))
            module = ParseDottedName();

        Expect(TokenType.Import);

        var aliases = new List<ImportAlias>();
        if (Match(TokenType.Star))
        {
            // from x import *
        }
        else if (Match(TokenType.LeftParen))
        {
            do
            {
                var name = ExpectIdentifier();
                string? alias = null;
                if (Match(TokenType.As)) alias = ExpectIdentifier();
                aliases.Add(new ImportAlias(name, alias));
            } while (Match(TokenType.Comma) && !Check(TokenType.RightParen));
            Expect(TokenType.RightParen);
        }
        else
        {
            do
            {
                var name = ExpectIdentifier();
                string? alias = null;
                if (Match(TokenType.As)) alias = ExpectIdentifier();
                aliases.Add(new ImportAlias(name, alias));
            } while (Match(TokenType.Comma));
        }

        SkipNewline();
        return new FromImportStatement(module, aliases, level, t.Line, t.Column);
    }

    private string ParseDottedName()
    {
        var sb = new System.Text.StringBuilder(ExpectIdentifier());
        while (Check(TokenType.Dot) && PeekType(1) == TokenType.Identifier)
        {
            Advance();
            sb.Append('.'); sb.Append(ExpectIdentifier());
        }
        return sb.ToString();
    }

    private Statement ParseIf()
    {
        var t = Expect(TokenType.If);
        var cond = ParseExpression();
        var then = ParseBlock();

        var elifs = new List<(Expression, IReadOnlyList<Statement>)>();
        var els = new List<Statement>();

        while (Check(TokenType.Elif))
        {
            Advance();
            var elifCond = ParseExpression();
            var elifBody = ParseBlock();
            elifs.Add((elifCond, elifBody));
        }

        if (Match(TokenType.Else))
            els = (List<Statement>)ParseBlock();

        return new IfStatement(cond, then, elifs, els, t.Line, t.Column);
    }

    private Statement ParseWhile()
    {
        var t = Expect(TokenType.While);
        var cond = ParseExpression();
        var body = ParseBlock();
        var els = new List<Statement>();
        if (Match(TokenType.Else))
            els = (List<Statement>)ParseBlock();
        return new WhileStatement(cond, body, els, t.Line, t.Column);
    }

    private Statement ParseFor(bool isAsync = false)
    {
        var t = Expect(TokenType.For);
        var target = ParseTargetList();
        Expect(TokenType.In);
        var iter = ParseExpression();
        var body = ParseBlock();
        var els = new List<Statement>();
        if (Match(TokenType.Else))
            els = (List<Statement>)ParseBlock();
        return new ForStatement(target, iter, body, els, isAsync, t.Line, t.Column);
    }

    private Statement ParseTry()
    {
        var t = Expect(TokenType.Try);
        var body = ParseBlock();
        var handlers = new List<ExceptHandler>();
        var els = new List<Statement>();
        var finally_ = new List<Statement>();

        while (Check(TokenType.Except))
        {
            var et = Advance();  // consume 'except'

            // PEP 654: except* catches exception groups
            bool isStar = Match(TokenType.Star);

            Expression? excType = null;
            string? excName = null;
            if (!Check(TokenType.Colon))
            {
                excType = ParseExpression();
                if (Match(TokenType.As))
                    excName = ExpectIdentifier();
            }

            // except* MUST have a type — bare 'except*:' is a syntax error in Python
            if (isStar && excType is null)
                throw Error("except* requires an exception type");

            var hBody = ParseBlock();
            handlers.Add(new ExceptHandler(excType, excName, hBody, isStar, et.Line, et.Column));
        }

        // Mixing except and except* is also a syntax error in Python
        bool hasStar = handlers.Any(h => h.isStar);
        bool hasPlain = handlers.Any(h => !h.isStar);
        if (hasStar && hasPlain)
            throw Error("cannot mix 'except' and 'except*' in the same try statement");

        if (Match(TokenType.Else))
            els = (List<Statement>)ParseBlock();
        if (Match(TokenType.Finally))
            finally_ = (List<Statement>)ParseBlock();

        return new TryStatement(body, handlers, els, finally_, t.Line, t.Column);
    }

    private Statement ParseWith(bool isAsync = false)
    {
        var t = Expect(TokenType.With);
        var items = new List<WithItem>();
        do
        {
            var ctx = ParseExpression();
            Expression? asTarget = null;
            if (Match(TokenType.As))
                asTarget = ParseExpression();
            items.Add(new WithItem(ctx, asTarget));
        } while (Match(TokenType.Comma));
        var body = ParseBlock();
        return new WithStatement(items, body, isAsync, t.Line, t.Column);
    }

    private Statement ParseFunctionDef(bool isAsync, IReadOnlyList<Expression>? decorators = null)
    {
        var t = Expect(TokenType.Def);
        var name = ExpectIdentifier();
        var parameters = ParseParameterList();
        Expression? returnAnn = null;
        if (Match(TokenType.Arrow))
            returnAnn = ParseExpression();

        var body = ParseBlock();
        return new FunctionDef(name, parameters, body, returnAnn,
            decorators ?? [], isAsync, t.Line, t.Column);
    }

    private Statement ParseSimpleStatement()
    {
        var t = Current();
        switch (t.Type)
        {
            case TokenType.Pass:
                Advance();
                return new PassStatement(t.Line, t.Column);
            case TokenType.Return: return ParseReturn();
            case TokenType.Raise: return ParseRaise();
            case TokenType.Assert: return ParseAssert();
            case TokenType.Del: return ParseDelete();
            case TokenType.Break: return ParseBreak();
            case TokenType.Continue: return ParseContinue();
            case TokenType.Global: return ParseGlobal();
            case TokenType.Nonlocal: return ParseNonlocal();
            default: return ParseExpressionStatement();
        }
    }

    private IReadOnlyList<Parameter> ParseParameterList()
    {
        Expect(TokenType.LeftParen);
        var @params = new List<Parameter>();
        bool keywordOnly = false;
        bool positionalOnly = true;  // PEP 570: before / all params are positional-only

        while (!Check(TokenType.RightParen) && !IsAtEnd())
        {
            // PEP 570: positional-only separator
            if (Match(TokenType.Slash))
            {
                positionalOnly = false;  // params after / are not positional-only
                Match(TokenType.Comma);  // consume optional trailing comma
                continue;
            }

            if (Match(TokenType.DoubleStar))
            {
                var name = ExpectIdentifier();
                @params.Add(new Parameter(name, null, null, false, true, false, false));
                break;
            }
            if (Match(TokenType.Star))
            {
                if (Check(TokenType.Comma) || Check(TokenType.RightParen))
                {
                    keywordOnly = true;
                    Match(TokenType.Comma);
                    continue;
                }
                var name = ExpectIdentifier();
                @params.Add(new Parameter(name, null, null, true, false, false, false));
                keywordOnly = true;
            }
            else
            {
                var name = ExpectIdentifier();
                Expression? ann = null, def = null;
                if (Match(TokenType.Colon)) ann = ParseExpression();
                if (Match(TokenType.Assign)) def = ParseExpression();
                @params.Add(new Parameter(name, ann, def, false, false, keywordOnly, positionalOnly));
            }
            if (!Match(TokenType.Comma)) break;
        }

        Expect(TokenType.RightParen);
        return @params;
    }

    private Statement ParseClassDef(IReadOnlyList<Expression>? decorators = null)
    {
        var t = Expect(TokenType.Class);
        var name = ExpectIdentifier();
        var bases = new List<Expression>();
        var kws = new List<Argument>();

        if (Match(TokenType.LeftParen))
        {
            while (!Check(TokenType.RightParen) && !IsAtEnd())
            {
                if (Check(TokenType.DoubleStar))
                {
                    Advance();
                    kws.Add(new Argument(null, ParseExpression(), false, true));
                }
                else if (Check(TokenType.Identifier) && PeekType(1) == TokenType.Assign)
                {
                    var kw = ExpectIdentifier();
                    Expect(TokenType.Assign);
                    kws.Add(new Argument(kw, ParseExpression(), false, false));
                }
                else
                    bases.Add(ParseExpression());
                if (!Match(TokenType.Comma)) break;
            }
            Expect(TokenType.RightParen);
        }

        var body = ParseBlock();
        return new ClassDef(name, bases, kws, body, decorators ?? [], t.Line, t.Column);
    }

    private Statement ParseDecorated()
    {
        var decorators = new List<Expression>();
        while (Match(TokenType.At))
        {
            decorators.Add(ParseExpression());
            SkipNewline();
        }
        if (Check(TokenType.Def)) return ParseFunctionDef(false, decorators);
        if (Check(TokenType.Async)) { Advance(); return ParseFunctionDef(true, decorators); }
        if (Check(TokenType.Class)) return ParseClassDef(decorators);
        throw Error("Expected def or class after decorator");
    }

    private Statement ParseAsync()
    {
        Expect(TokenType.Async);
        if (Check(TokenType.Def)) return ParseFunctionDef(isAsync: true);
        if (Check(TokenType.For)) return ParseFor(isAsync: true);
        if (Check(TokenType.With)) return ParseWith(isAsync: true);
        throw Error("Expected def, for, or with after async");
    }

    private Statement ParseMatch()
    {
        var t = Expect(TokenType.Match);
        var subject = ParseExpression();
        Expect(TokenType.Colon);
        SkipNewlines();
        Expect(TokenType.Indent);

        var cases = new List<MatchCase>();
        while (Check(TokenType.Case))
        {
            Advance();
            var pattern = ParsePattern();
            Expression? guard = null;
            if (Match(TokenType.If))
                guard = ParseExpression();
            var body = ParseCaseBody();
            cases.Add(new MatchCase(pattern, guard, body));
            SkipNewlines();
        }

        if (Check(TokenType.Dedent)) Advance();
        return new MatchStatement(subject, cases, t.Line, t.Column);
    }

    /// <summary>
    /// Parses the body of a case clause, handling both single-line and block bodies.
    /// Similar to ParseBlock but optimized for case statements.
    /// </summary>
    private IReadOnlyList<Statement> ParseCaseBody()
    {
        Expect(TokenType.Colon);

        // Handle single-line case body (e.g., `case 1: return 42`)
        if (!Check(TokenType.Newline) && !Check(TokenType.Eof))
        {
            var stmts = new List<Statement>();
            stmts.Add(ParseSimpleStatement());
            SkipNewline();
            return stmts;
        }

        // Handle multi-line indented block
        SkipNewlines();
        Expect(TokenType.Indent);
        var stmtsList = ParseStatementList();
        SkipNewlines();
        if (Check(TokenType.Dedent)) Advance();
        return stmtsList;
    }

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

    private Statement ParseTypeAlias()
    {
        var t = Expect(TokenType.Type);
        var name = ExpectIdentifier();
        Expect(TokenType.Assign);
        var value = ParseExpression();
        SkipNewline();
        return new TypeAliasStatement(name, value, t.Line, t.Column);
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
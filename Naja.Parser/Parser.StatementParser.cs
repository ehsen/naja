using Naja.Lexer;
using System.Collections.Generic;
using System.Linq;

namespace Naja.Parser;

/// <summary>
/// Statement parsing logic for Naja Parser.
/// Handles parsing of statements, blocks, parameters, and case bodies.
/// </summary>
public sealed partial class Parser
{
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

    private Statement ParseTypeAlias()
    {
        var t = Expect(TokenType.Type);
        var name = ExpectIdentifier();
        Expect(TokenType.Assign);
        var value = ParseExpression();
        SkipNewline();
        return new TypeAliasStatement(name, value, t.Line, t.Column);
    }
}

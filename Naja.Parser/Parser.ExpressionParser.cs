using Naja.Lexer;

namespace Naja.Parser;

/// <summary>
/// Partial class: Expression parsing logic.
/// Handles operator precedence, postfix operations, atoms, collections, and comprehensions.
/// </summary>
public sealed partial class Parser
{
    private Expression ParseTupleOrExpression()
    {
        var first = ParseExpression();
        if (!Check(TokenType.Comma)) return first;

        var elements = new List<Expression> { first };
        while (Match(TokenType.Comma))
        {
            if (CheckNewlineOrEof() || Check(TokenType.Assign) || Check(TokenType.Colon)) break;
            elements.Add(ParseExpression());
        }
        return new TupleExpr(elements, first.Line, first.Column);
    }

    private Statement ParseExpressionStatement()
    {
        var t = Current();
        var expr = ParseTupleOrExpression();

        var augOp = TryGetAugOp();
        if (augOp.HasValue)
        {
            Advance();
            var val = ParseTupleOrExpression();
            SkipNewline();
            return new AugAssignStatement(expr, augOp.Value, val, t.Line, t.Column);
        }

        if (Match(TokenType.Colon))
        {
            var ann = ParseExpression();
            Expression? val = null;
            if (Match(TokenType.Assign))
                val = ParseTupleOrExpression();
            SkipNewline();
            return new AnnAssignStatement(expr, ann, val, t.Line, t.Column);
        }

        if (Check(TokenType.Assign))
        {
            var targets = new List<Expression> { expr };
            while (Match(TokenType.Assign))
            {
                if (CheckNewlineOrEof()) break;
                targets.Add(ParseTupleOrExpression());
            }
            var value = targets[^1];
            targets.RemoveAt(targets.Count - 1);
            SkipNewline();
            return new AssignStatement(targets, value, t.Line, t.Column);
        }

        SkipNewline();
        return new ExprStatement(expr, t.Line, t.Column);
    }

    private BinaryOp? TryGetAugOp() => Current().Type switch
    {
        TokenType.PlusEqual => BinaryOp.Add,
        TokenType.MinusEqual => BinaryOp.Sub,
        TokenType.StarEqual => BinaryOp.Mul,
        TokenType.SlashEqual => BinaryOp.Div,
        TokenType.DoubleSlashEqual => BinaryOp.FloorDiv,
        TokenType.PercentEqual => BinaryOp.Mod,
        TokenType.DoubleStarEqual => BinaryOp.Pow,
        TokenType.AtEqual => BinaryOp.MatMul,
        TokenType.AmpersandEqual => BinaryOp.BitAnd,
        TokenType.PipeEqual => BinaryOp.BitOr,
        TokenType.CaretEqual => BinaryOp.BitXor,
        TokenType.LeftShiftEqual => BinaryOp.LShift,
        TokenType.RightShiftEqual => BinaryOp.RShift,
        _ => null
    };

    public Expression ParseExpression()
    {
        return ParseTernary();
    }

    private Expression ParseTernary()
    {
        var left = ParseOr();
        if (Match(TokenType.If))
        {
            var cond = ParseOr();
            Expect(TokenType.Else);
            var els = ParseTernary();
            return new IfExpr(cond, left, els, left.Line, left.Column);
        }
        return left;
    }

    private Expression ParseOr()
    {
        var left = ParseAnd();
        while (Check(TokenType.Or))
        {
            Advance();
            var right = ParseAnd();
            left = new BoolOpExpr(BoolOp.Or, [left, right], left.Line, left.Column);
        }
        return left;
    }

    private Expression ParseAnd()
    {
        var left = ParseNot();
        while (Check(TokenType.And))
        {
            Advance();
            var right = ParseNot();
            left = new BoolOpExpr(BoolOp.And, [left, right], left.Line, left.Column);
        }
        return left;
    }

    private Expression ParseNot()
    {
        if (Check(TokenType.Not))
        {
            var t = Advance();
            return new UnaryExpr(UnaryOp.Not, ParseNot(), t.Line, t.Column);
        }
        return ParseComparison();
    }

    private Expression ParseComparison()
    {
        var left = ParseBitOr();
        var comparators = new List<(CompareOp, Expression)>();

        while (true)
        {
            var op = TryGetCompareOp();
            if (op is null) break;
            var right = ParseBitOr();
            comparators.Add((op.Value, right));
        }

        if (comparators.Count == 0) return left;
        return new CompareExpr(left, comparators, left.Line, left.Column);
    }

    private CompareOp? TryGetCompareOp()
    {
        var t = Current();
        switch (t.Type)
        {
            case TokenType.Equal: Advance(); return CompareOp.Eq;
            case TokenType.NotEqual: Advance(); return CompareOp.NotEq;
            case TokenType.Less: Advance(); return CompareOp.Lt;
            case TokenType.LessEqual: Advance(); return CompareOp.LtEq;
            case TokenType.Greater: Advance(); return CompareOp.Gt;
            case TokenType.GreaterEqual: Advance(); return CompareOp.GtEq;
            case TokenType.In: Advance(); return CompareOp.In;
            case TokenType.Is:
                Advance();
                if (Match(TokenType.Not)) return CompareOp.IsNot;
                return CompareOp.Is;
            case TokenType.Not:
                if (PeekType(1) == TokenType.In) { Advance(); Advance(); return CompareOp.NotIn; }
                return null;
            default: return null;
        }
    }

    private Expression ParseBitOr()
    {
        var left = ParseBitXor();
        while (Match(TokenType.Pipe))
            left = new BinaryExpr(left, BinaryOp.BitOr, ParseBitXor(), left.Line, left.Column);
        return left;
    }

    private Expression ParseBitXor()
    {
        var left = ParseBitAnd();
        while (Match(TokenType.Caret))
            left = new BinaryExpr(left, BinaryOp.BitXor, ParseBitAnd(), left.Line, left.Column);
        return left;
    }

    private Expression ParseBitAnd()
    {
        var left = ParseShift();
        while (Match(TokenType.Ampersand))
            left = new BinaryExpr(left, BinaryOp.BitAnd, ParseShift(), left.Line, left.Column);
        return left;
    }

    private Expression ParseShift()
    {
        var left = ParseAddSub();
        while (true)
        {
            if (Match(TokenType.LeftShift))
                left = new BinaryExpr(left, BinaryOp.LShift, ParseAddSub(), left.Line, left.Column);
            else if (Match(TokenType.RightShift))
                left = new BinaryExpr(left, BinaryOp.RShift, ParseAddSub(), left.Line, left.Column);
            else break;
        }
        return left;
    }

    private Expression ParseAddSub()
    {
        var left = ParseMulDiv();
        while (true)
        {
            if (Match(TokenType.Plus))
                left = new BinaryExpr(left, BinaryOp.Add, ParseMulDiv(), left.Line, left.Column);
            else if (Match(TokenType.Minus))
                left = new BinaryExpr(left, BinaryOp.Sub, ParseMulDiv(), left.Line, left.Column);
            else break;
        }
        return left;
    }

    private Expression ParseMulDiv()
    {
        var left = ParseUnary();
        while (true)
        {
            if (Match(TokenType.Star)) left = new BinaryExpr(left, BinaryOp.Mul, ParseUnary(), left.Line, left.Column);
            else if (Match(TokenType.Slash)) left = new BinaryExpr(left, BinaryOp.Div, ParseUnary(), left.Line, left.Column);
            else if (Match(TokenType.DoubleSlash)) left = new BinaryExpr(left, BinaryOp.FloorDiv, ParseUnary(), left.Line, left.Column);
            else if (Match(TokenType.Percent)) left = new BinaryExpr(left, BinaryOp.Mod, ParseUnary(), left.Line, left.Column);
            else if (Match(TokenType.At)) left = new BinaryExpr(left, BinaryOp.MatMul, ParseUnary(), left.Line, left.Column);
            else break;
        }
        return left;
    }

    private Expression ParseUnary()
    {
        var t = Current();
        if (Match(TokenType.Plus)) return new UnaryExpr(UnaryOp.Pos, ParseUnary(), t.Line, t.Column);
        if (Match(TokenType.Minus)) return new UnaryExpr(UnaryOp.Neg, ParseUnary(), t.Line, t.Column);
        if (Match(TokenType.Tilde)) return new UnaryExpr(UnaryOp.Invert, ParseUnary(), t.Line, t.Column);
        return ParsePower();
    }

    private Expression ParsePower()
    {
        var left = ParseAwait();
        if (Match(TokenType.DoubleStar))
            return new BinaryExpr(left, BinaryOp.Pow, ParseUnary(), left.Line, left.Column);
        return left;
    }

    private Expression ParseAwait()
    {
        if (Check(TokenType.Await))
        {
            var t = Advance();
            return new AwaitExpr(ParsePostfix(), t.Line, t.Column);
        }
        return ParsePostfix();
    }

    private Expression ParsePostfix()
    {
        var expr = ParseAtom();

        while (true)
        {
            if (Match(TokenType.Dot))
            {
                var name = ExpectIdentifier();
                expr = new AttributeExpr(expr, name, expr.Line, expr.Column);
            }
            else if (Check(TokenType.LeftBracket))
            {
                var t = Advance();
                var index = ParseSliceOrIndex();
                // Handle multi-arg subscripts: dict[str, int], Tuple[int, str], etc.
                if (index is not SliceExpr && Check(TokenType.Comma))
                {
                    var items = new List<Expression> { index };
                    while (Match(TokenType.Comma) && !Check(TokenType.RightBracket))
                        items.Add(ParseSliceOrIndex());
                    index = new TupleExpr(items, t.Line, t.Column);
                }
                Expect(TokenType.RightBracket);
                expr = new SubscriptExpr(expr, index, t.Line, t.Column);
            }
            else if (Check(TokenType.LeftParen))
            {
                var t = Advance();
                var args = ParseArgumentList();
                Expect(TokenType.RightParen);
                expr = new CallExpr(expr, args, t.Line, t.Column);
            }
            else break;
        }

        return expr;
    }

    private Expression ParseSliceOrIndex()
    {
        Expression? lower = null, upper = null, step = null;
        int line = Current().Line, col = Current().Column;

        if (!Check(TokenType.Colon))
            lower = ParseExpression();

        if (!Match(TokenType.Colon))
            return lower!;

        if (!Check(TokenType.Colon) && !Check(TokenType.RightBracket))
            upper = ParseExpression();

        if (Match(TokenType.Colon))
            if (!Check(TokenType.RightBracket))
                step = ParseExpression();

        return new SliceExpr(lower, upper, step, line, col);
    }

    private IReadOnlyList<Argument> ParseArgumentList()
    {
        var args = new List<Argument>();
        while (!Check(TokenType.RightParen) && !IsAtEnd())
        {
            if (Match(TokenType.DoubleStar))
            {
                args.Add(new Argument(null, ParseExpression(), false, true));
            }
            else if (Match(TokenType.Star))
            {
                args.Add(new Argument(null, ParseExpression(), true, false));
            }
            else if (Check(TokenType.Identifier) && PeekType(1) == TokenType.Assign)
            {
                var kw = ExpectIdentifier();
                Expect(TokenType.Assign);
                args.Add(new Argument(kw, ParseExpression(), false, false));
            }
            else
            {
                var expr = ParseExpression();

                // Allow bare generator expressions if they are the ONLY argument
                if (Check(TokenType.For) && args.Count == 0)
                {
                    var gens = ParseComprehensions();
                    expr = new GeneratorExpr(expr, gens, expr.Line, expr.Column);
                }
                args.Add(new Argument(null, expr, false, false));
            }
            if (!Match(TokenType.Comma)) break;
        }
        return args;
    }

    private Expression ParseAtom()
    {
        var t = Current();

        switch (t.Type)
        {
            case TokenType.Integer:
                Advance();
                return new IntLiteral(ParseInt(t.Value), t.Line, t.Column);

            case TokenType.Float:
                Advance();
                return new FloatLiteral(double.Parse(t.Value,
                    System.Globalization.CultureInfo.InvariantCulture), t.Line, t.Column);

            case TokenType.String:
                Advance();
                {
                    var sb = new System.Text.StringBuilder(t.Value);
                    while (Check(TokenType.String))
                        sb.Append(Advance().Value);
                    return new StringLiteral(sb.ToString(), t.Line, t.Column);
                }

            case TokenType.FString:
                Advance();
                {
                    // Support implicit concatenation of adjacent f-strings:
                    // f"First {first}, " f"Last {last}"  -> single template
                    var sb = new System.Text.StringBuilder(t.Value);
                    while (Check(TokenType.FString))
                        sb.Append(Advance().Value);
                    return new FStringExpr(sb.ToString(), t.Line, t.Column);
                }

            case TokenType.True:
                Advance();
                return new BoolLiteral(true, t.Line, t.Column);

            case TokenType.False:
                Advance();
                return new BoolLiteral(false, t.Line, t.Column);

            case TokenType.None:
                Advance();
                return new NoneLiteral(t.Line, t.Column);

            case TokenType.Ellipsis:
                Advance();
                return new EllipsisLiteral(t.Line, t.Column);

            case TokenType.Identifier:
            case TokenType.Type:
            case TokenType.Match:
            case TokenType.Case:
                {
                    Advance();
                    if (Match(TokenType.Walrus))
                        return new WalrusExpr(t.Value, ParseExpression(), t.Line, t.Column);
                    return new NameExpr(t.Value, t.Line, t.Column);
                }

            case TokenType.LeftParen:
                {
                    Advance();
                    if (Match(TokenType.RightParen))
                        return new TupleExpr([], t.Line, t.Column);

                    var expr = ParseExpression();

                    if (Check(TokenType.For))
                    {
                        var gens = ParseComprehensions();
                        Expect(TokenType.RightParen);
                        return new GeneratorExpr(expr, gens, t.Line, t.Column);
                    }

                    if (Check(TokenType.Comma))
                    {
                        var elems = new List<Expression> { expr };
                        while (Match(TokenType.Comma) && !Check(TokenType.RightParen))
                            elems.Add(ParseExpression());
                        Expect(TokenType.RightParen);
                        return new TupleExpr(elems, t.Line, t.Column);
                    }

                    Expect(TokenType.RightParen);
                    return expr;
                }

            case TokenType.LeftBracket:
                {
                    Advance();
                    if (Match(TokenType.RightBracket))
                        return new ListExpr([], t.Line, t.Column);

                    var first = ParseExpression();

                    if (Check(TokenType.For))
                    {
                        var gens = ParseComprehensions();
                        Expect(TokenType.RightBracket);
                        return new ListCompExpr(first, gens, t.Line, t.Column);
                    }

                    var elems = new List<Expression> { first };
                    while (Match(TokenType.Comma) && !Check(TokenType.RightBracket))
                        elems.Add(ParseExpression());
                    Expect(TokenType.RightBracket);
                    return new ListExpr(elems, t.Line, t.Column);
                }

            case TokenType.LeftBrace:
                {
                    Advance();
                    if (Match(TokenType.RightBrace))
                        return new DictExpr([], t.Line, t.Column);

                    if (Check(TokenType.DoubleStar))
                        return ParseDictOrSet(t, null);

                    var first = ParseExpression();

                    if (Check(TokenType.Colon))
                        return ParseDictOrSet(t, first);

                    if (Check(TokenType.For))
                    {
                        var gens = ParseComprehensions();
                        Expect(TokenType.RightBrace);
                        return new SetCompExpr(first, gens, t.Line, t.Column);
                    }

                    var setElems = new List<Expression> { first };
                    while (Match(TokenType.Comma) && !Check(TokenType.RightBrace))
                        setElems.Add(ParseExpression());
                    Expect(TokenType.RightBrace);
                    return new SetExpr(setElems, t.Line, t.Column);
                }

            case TokenType.Star:
                {
                    Advance();
                    return new StarredExpr(ParseExpression(), t.Line, t.Column);
                }

            case TokenType.Yield:
                {
                    Advance();
                    bool isFrom = Match(TokenType.From);
                    Expression? val = null;
                    if (!CheckNewlineOrEof())
                        val = ParseExpression();
                    return new YieldExpr(val, isFrom, t.Line, t.Column);
                }

            case TokenType.Lambda:
                {
                    Advance();
                    var lparams = new List<Parameter>();
                    while (!Check(TokenType.Colon) && !IsAtEnd())
                    {
                        var pname = ExpectIdentifier();
                        Expression? def = null;
                        if (Match(TokenType.Assign)) def = ParseExpression();
                        lparams.Add(new Parameter(pname, null, def, false, false, false, false));
                        if (!Match(TokenType.Comma)) break;
                    }
                    Expect(TokenType.Colon);
                    var lbody = ParseExpression();
                    return new LambdaExpr(lparams, lbody, t.Line, t.Column);
                }

            default:
                throw Error($"Unexpected token '{t.Value}' ({t.Type})");
        }
    }

    private Expression ParseDictOrSet(Token t, Expression? firstKey)
    {
        var pairs = new List<(Expression?, Expression)>();

        if (firstKey is not null)
        {
            Expect(TokenType.Colon);
            var val = ParseExpression();

            if (Check(TokenType.For))
            {
                var gens = ParseComprehensions();
                Expect(TokenType.RightBrace);
                return new DictCompExpr(firstKey, val, gens, t.Line, t.Column);
            }

            pairs.Add((firstKey, val));
        }

        while (Match(TokenType.Comma) && !Check(TokenType.RightBrace))
        {
            if (Match(TokenType.DoubleStar))
                pairs.Add((null, ParseExpression()));
            else
            {
                var k = ParseExpression();
                Expect(TokenType.Colon);
                var v = ParseExpression();
                pairs.Add((k, v));
            }
        }

        Expect(TokenType.RightBrace);
        return new DictExpr(pairs, t.Line, t.Column);
    }

    private IReadOnlyList<Comprehension> ParseComprehensions()
    {
        var comps = new List<Comprehension>();
        while (Check(TokenType.For) || Check(TokenType.Async))
        {
            bool isAsync = Match(TokenType.Async);
            Expect(TokenType.For);
            var target = ParseTargetList();
            Expect(TokenType.In);
            var iter = ParseOr();
            var ifs = new List<Expression>();
            while (Match(TokenType.If))
                ifs.Add(ParseOr());
            comps.Add(new Comprehension(target, iter, ifs, isAsync));
        }
        return comps;
    }

    private Expression ParseTargetList()
    {
        var first = ParseTarget();
        if (!Check(TokenType.Comma)) return first;
        var elems = new List<Expression> { first };
        while (Match(TokenType.Comma) && !Check(TokenType.In) && !Check(TokenType.Colon) && !CheckNewlineOrEof())
            elems.Add(ParseTarget());
        return new TupleExpr(elems, first.Line, first.Column);
    }

    private Expression ParseTarget()
    {
        var t = Current();

        if (Match(TokenType.Star))
            return new StarredExpr(ParseTarget(), t.Line, t.Column);

        if (Check(TokenType.LeftParen))
        {
            Advance();
            var inner = ParseTargetList();
            Expect(TokenType.RightParen);
            return inner;
        }

        if (Check(TokenType.LeftBracket))
        {
            Advance();
            var inner = ParseTargetList();
            Expect(TokenType.RightBracket);
            return new ListExpr(inner is TupleExpr te ? te.Elements : [inner], t.Line, t.Column);
        }

        var name = ExpectIdentifier();
        Expression expr = new NameExpr(name, t.Line, t.Column);

        while (true)
        {
            if (Match(TokenType.Dot))
            {
                var attr = ExpectIdentifier();
                expr = new AttributeExpr(expr, attr, expr.Line, expr.Column);
            }
            else if (Check(TokenType.LeftBracket))
            {
                Advance();
                var idx = ParseSliceOrIndex();
                Expect(TokenType.RightBracket);
                expr = new SubscriptExpr(expr, idx, expr.Line, expr.Column);
            }
            else break;
        }

        return expr;
    }

    private IReadOnlyList<Expression> ParseExpressionList()
    {
        var exprs = new List<Expression>();
        exprs.Add(ParseExpression());
        while (Match(TokenType.Comma) && !CheckNewlineOrEof())
            exprs.Add(ParseExpression());
        return exprs;
    }
}

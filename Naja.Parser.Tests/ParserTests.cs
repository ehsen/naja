using Naja.Lexer;
using Naja.Parser;
using Xunit;

namespace Naja.Parser.Tests;

public class ParserTests
{
    private static Module Parse(string source)
    {
        // Fully-qualify the Lexer type to avoid the CS0118 "namespace used like a type"
        var tokens = new Naja.Lexer.Lexer(source).Tokenize();
        return new Parser(tokens).ParseModule();
    }

    private static Statement FirstStmt(string source) =>
        Parse(source).Body[0];

    private static Expression FirstExpr(string source) =>
        ((ExprStatement)FirstStmt(source)).Expr;

    // ── Literals ──────────────────────────────────────────────────────────────

    [Fact]
    public void Integer_Literal()
    {
        var expr = (IntLiteral)FirstExpr("42");
        Assert.Equal(42L, expr.Value);
    }

    [Fact]
    public void Float_Literal()
    {
        var expr = (FloatLiteral)FirstExpr("3.14");
        Assert.Equal(3.14, expr.Value, precision: 5);
    }

    [Fact]
    public void String_Literal()
    {
        var expr = (StringLiteral)FirstExpr("\"hello\"");
        Assert.Equal("hello", expr.Value);
    }

    [Fact]
    public void Bool_True() => Assert.True(((BoolLiteral)FirstExpr("True")).Value);
    [Fact]
    public void Bool_False() => Assert.False(((BoolLiteral)FirstExpr("False")).Value);
    [Fact]
    public void None_Literal() => Assert.IsType<NoneLiteral>(FirstExpr("None"));

    // ── Binary expressions ────────────────────────────────────────────────────

    [Fact]
    public void Addition()
    {
        var expr = (BinaryExpr)FirstExpr("1 + 2");
        Assert.Equal(BinaryOp.Add, expr.Op);
        Assert.Equal(1L, ((IntLiteral)expr.Left).Value);
        Assert.Equal(2L, ((IntLiteral)expr.Right).Value);
    }

    [Fact]
    public void Operator_Precedence_MulBeforeAdd()
    {
        // 1 + 2 * 3  should parse as  1 + (2 * 3)
        var expr = (BinaryExpr)FirstExpr("1 + 2 * 3");
        Assert.Equal(BinaryOp.Add, expr.Op);
        Assert.Equal(BinaryOp.Mul, ((BinaryExpr)expr.Right).Op);
    }

    [Fact]
    public void Power_RightAssociative()
    {
        // 2 ** 3 ** 4  should parse as  2 ** (3 ** 4)
        var expr = (BinaryExpr)FirstExpr("2 ** 3 ** 4");
        Assert.Equal(BinaryOp.Pow, expr.Op);
        Assert.Equal(BinaryOp.Pow, ((BinaryExpr)expr.Right).Op);
    }

    [Fact]
    public void Unary_Negation()
    {
        var expr = (UnaryExpr)FirstExpr("-x");
        Assert.Equal(UnaryOp.Neg, expr.Op);
    }

    [Fact]
    public void Unary_Not()
    {
        var expr = (UnaryExpr)FirstExpr("not True");
        Assert.Equal(UnaryOp.Not, expr.Op);
    }

    // ── Comparisons ───────────────────────────────────────────────────────────

    [Fact]
    public void Simple_Comparison()
    {
        var expr = (CompareExpr)FirstExpr("a < b");
        Assert.Equal(CompareOp.Lt, expr.Comparators[0].Op);
    }

    [Fact]
    public void Chained_Comparison()
    {
        var expr = (CompareExpr)FirstExpr("a < b < c");
        Assert.Equal(2, expr.Comparators.Count);
        Assert.Equal(CompareOp.Lt, expr.Comparators[0].Op);
        Assert.Equal(CompareOp.Lt, expr.Comparators[1].Op);
    }

    [Fact]
    public void Is_Not_Comparison()
    {
        var expr = (CompareExpr)FirstExpr("x is not None");
        Assert.Equal(CompareOp.IsNot, expr.Comparators[0].Op);
    }

    [Fact]
    public void Not_In_Comparison()
    {
        var expr = (CompareExpr)FirstExpr("x not in [1, 2]");
        Assert.Equal(CompareOp.NotIn, expr.Comparators[0].Op);
    }

    // ── Boolean ops ───────────────────────────────────────────────────────────

    [Fact]
    public void And_Expression()
    {
        var expr = (BoolOpExpr)FirstExpr("a and b");
        Assert.Equal(BoolOp.And, expr.Op);
    }

    [Fact]
    public void Or_Expression()
    {
        var expr = (BoolOpExpr)FirstExpr("a or b");
        Assert.Equal(BoolOp.Or, expr.Op);
    }

    // ── Ternary ───────────────────────────────────────────────────────────────

    [Fact]
    public void Ternary_IfElse()
    {
        var expr = (IfExpr)FirstExpr("x if cond else y");
        Assert.IsType<NameExpr>(expr.Then);
        Assert.IsType<NameExpr>(expr.Condition);
        Assert.IsType<NameExpr>(expr.Else);
    }

    // ── Collections ───────────────────────────────────────────────────────────

    [Fact]
    public void List_Literal()
    {
        var expr = (ListExpr)FirstExpr("[1, 2, 3]");
        Assert.Equal(3, expr.Elements.Count);
    }

    [Fact]
    public void Empty_List() => Assert.IsType<ListExpr>(FirstExpr("[]"));

    [Fact]
    public void Dict_Literal()
    {
        var expr = (DictExpr)FirstExpr("{\"a\": 1, \"b\": 2}");
        Assert.Equal(2, expr.Pairs.Count);
    }

    [Fact]
    public void Tuple_Literal()
    {
        var expr = (TupleExpr)FirstExpr("(1, 2, 3)");
        Assert.Equal(3, expr.Elements.Count);
    }

    [Fact]
    public void Empty_Tuple() => Assert.IsType<TupleExpr>(FirstExpr("()"));

    // ── Calls ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Simple_Call()
    {
        var expr = (CallExpr)FirstExpr("f(1, 2)");
        Assert.Equal(2, expr.Args.Count);
    }

    [Fact]
    public void Keyword_Call()
    {
        var expr = (CallExpr)FirstExpr("f(x=1, y=2)");
        Assert.Equal("x", expr.Args[0].Keyword);
        Assert.Equal("y", expr.Args[1].Keyword);
    }

    [Fact]
    public void Star_Args_Call()
    {
        var expr = (CallExpr)FirstExpr("f(*args, **kwargs)");
        Assert.True(expr.Args[0].IsStar);
        Assert.True(expr.Args[1].IsDoubleStar);
    }

    // ── Attribute / subscript ─────────────────────────────────────────────────

    [Fact]
    public void Attribute_Access()
    {
        var expr = (AttributeExpr)FirstExpr("obj.name");
        Assert.Equal("name", expr.Attribute);
    }

    [Fact]
    public void Subscript()
    {
        var expr = (SubscriptExpr)FirstExpr("arr[0]");
        Assert.Equal(0L, ((IntLiteral)expr.Index).Value);
    }

    [Fact]
    public void Slice()
    {
        var expr = (SubscriptExpr)FirstExpr("arr[1:10]");
        Assert.IsType<SliceExpr>(expr.Index);
    }

    // ── Statements ────────────────────────────────────────────────────────────

    [Fact]
    public void Assignment()
    {
        var stmt = (AssignStatement)FirstStmt("x = 42");
        Assert.Single(stmt.Targets);
        Assert.Equal("x", ((NameExpr)stmt.Targets[0]).Name);
        Assert.Equal(42L, ((IntLiteral)stmt.Value).Value);
    }

    [Fact]
    public void Augmented_Assignment()
    {
        var stmt = (AugAssignStatement)FirstStmt("x += 1");
        Assert.Equal(BinaryOp.Add, stmt.Op);
    }

    [Fact]
    public void Annotated_Assignment()
    {
        var stmt = (AnnAssignStatement)FirstStmt("x: int = 5");
        Assert.Equal("x", ((NameExpr)stmt.Target).Name);
        Assert.Equal("int", ((NameExpr)stmt.Annotation).Name);
    }

    [Fact]
    public void Return_With_Value()
    {
        var fn = (FunctionDef)FirstStmt("def f():\n    return 42\n");
        var ret = (ReturnStatement)fn.Body[0];
        Assert.Equal(42L, ((IntLiteral)ret.Value!).Value);
    }

    [Fact]
    public void Return_Bare()
    {
        var fn = (FunctionDef)FirstStmt("def f():\n    return\n");
        var ret = (ReturnStatement)fn.Body[0];
        Assert.Null(ret.Value);
    }

    // ── Control flow ──────────────────────────────────────────────────────────

    [Fact]
    public void If_Statement()
    {
        var stmt = (IfStatement)FirstStmt("if True:\n    pass\n");
        Assert.IsType<BoolLiteral>(stmt.Condition);
        Assert.Single(stmt.Then);
    }

    [Fact]
    public void If_Else()
    {
        var stmt = (IfStatement)FirstStmt("if x:\n    pass\nelse:\n    pass\n");
        Assert.NotEmpty(stmt.Else);
    }

    [Fact]
    public void If_Elif_Else()
    {
        var src = "if a:\n    pass\nelif b:\n    pass\nelse:\n    pass\n";
        var stmt = (IfStatement)FirstStmt(src);
        Assert.Single(stmt.Elifs);
        Assert.NotEmpty(stmt.Else);
    }

    [Fact]
    public void While_Statement()
    {
        var stmt = (WhileStatement)FirstStmt("while True:\n    pass\n");
        Assert.IsType<BoolLiteral>(stmt.Condition);
    }

    [Fact]
    public void For_Statement()
    {
        var stmt = (ForStatement)FirstStmt("for x in items:\n    pass\n");
        Assert.Equal("x", ((NameExpr)stmt.Target).Name);
        Assert.Equal("items", ((NameExpr)stmt.Iter).Name);
    }

    // ── Functions ─────────────────────────────────────────────────────────────

    [Fact]
    public void Function_Definition()
    {
        var fn = (FunctionDef)FirstStmt("def add(a: int, b: int) -> int:\n    return a + b\n");
        Assert.Equal("add", fn.Name);
        Assert.Equal(2, fn.Params.Count);
        Assert.NotNull(fn.ReturnAnnotation);
    }

    [Fact]
    public void Async_Function()
    {
        var fn = (FunctionDef)FirstStmt("async def f():\n    pass\n");
        Assert.True(fn.IsAsync);
    }

    [Fact]
    public void Function_Default_Params()
    {
        var fn = (FunctionDef)FirstStmt("def f(x=1, y=2):\n    pass\n");
        Assert.NotNull(fn.Params[0].Default);
        Assert.NotNull(fn.Params[1].Default);
    }

    // ── Classes ───────────────────────────────────────────────────────────────

    [Fact]
    public void Class_Definition()
    {
        var cls = (ClassDef)FirstStmt("class Foo:\n    pass\n");
        Assert.Equal("Foo", cls.Name);
    }

    [Fact]
    public void Class_With_Base()
    {
        var cls = (ClassDef)FirstStmt("class Foo(Bar):\n    pass\n");
        Assert.Single(cls.Bases);
        Assert.Equal("Bar", ((NameExpr)cls.Bases[0]).Name);
    }

    // ── Import ────────────────────────────────────────────────────────────────

    [Fact]
    public void Import_Statement()
    {
        var stmt = (ImportStatement)FirstStmt("import os");
        Assert.Equal("os", stmt.Names[0].Name);
    }

    [Fact]
    public void From_Import()
    {
        var stmt = (FromImportStatement)FirstStmt("from os import path");
        Assert.Equal("os", stmt.Module);
        Assert.Equal("path", stmt.Names[0].Name);
    }

    [Fact]
    public void Import_As()
    {
        var stmt = (ImportStatement)FirstStmt("import numpy as np");
        Assert.Equal("np", stmt.Names[0].Alias);
    }

    // ── Comprehensions ────────────────────────────────────────────────────────

    [Fact]
    public void List_Comprehension()
    {
        var expr = (ListCompExpr)FirstExpr("[x for x in items]");
        Assert.Single(expr.Generators);
    }

    [Fact]
    public void Dict_Comprehension()
    {
        var expr = (DictCompExpr)FirstExpr("{k: v for k, v in items.items()}");
        Assert.Single(expr.Generators);
    }

    // ── Lambda ────────────────────────────────────────────────────────────────

    [Fact]
    public void Lambda_Expression()
    {
        var expr = (LambdaExpr)FirstExpr("lambda x, y: x + y");
        Assert.Equal(2, expr.Params.Count);
        Assert.IsType<BinaryExpr>(expr.Body);
    }

    // ── Full program ──────────────────────────────────────────────────────────

    [Fact]
    public void Full_FizzBuzz()
    {
        var source = """
            for i in range(1, 101):
                if i % 15 == 0:
                    print("FizzBuzz")
                elif i % 3 == 0:
                    print("Fizz")
                elif i % 5 == 0:
                    print("Buzz")
                else:
                    print(i)
            """;
        var module = Parse(source);
        Assert.Single(module.Body);
        Assert.IsType<ForStatement>(module.Body[0]);
    }
}
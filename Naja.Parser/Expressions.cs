namespace Naja.Parser;

// ── Base ─────────────────────────────────────────────────────────────────────

/// <summary>Every AST node carries source position for error reporting.</summary>
public abstract record AstNode(int Line, int Column);

/// <summary>A complete Python module (the root of the tree).</summary>
public sealed record Module(
    IReadOnlyList<Statement> Body,
    int Line, int Column
) : AstNode(Line, Column);

// ── Expressions ───────────────────────────────────────────────────────────────

public abstract record Expression(int Line, int Column) : AstNode(Line, Column);

// Literals
public sealed record IntLiteral   (long Value,    int Line, int Column) : Expression(Line, Column);
public sealed record FloatLiteral (double Value,  int Line, int Column) : Expression(Line, Column);
public sealed record StringLiteral(string Value,  int Line, int Column) : Expression(Line, Column);
public sealed record BoolLiteral  (bool Value,    int Line, int Column) : Expression(Line, Column);
public sealed record NoneLiteral  (               int Line, int Column) : Expression(Line, Column);
public sealed record EllipsisLiteral(             int Line, int Column) : Expression(Line, Column);

// Name / identifier reference
public sealed record NameExpr(string Name, int Line, int Column) : Expression(Line, Column);

// Unary operations:  -x  +x  ~x  not x
public sealed record UnaryExpr(
    UnaryOp Op,
    Expression Operand,
    int Line, int Column
) : Expression(Line, Column);

// Binary operations:  a + b  a and b  etc.
public sealed record BinaryExpr(
    Expression Left,
    BinaryOp Op,
    Expression Right,
    int Line, int Column
) : Expression(Line, Column);

// Comparison chain:  a < b <= c  (Python allows chaining)
public sealed record CompareExpr(
    Expression Left,
    IReadOnlyList<(CompareOp Op, Expression Right)> Comparators,
    int Line, int Column
) : Expression(Line, Column);

// Boolean ops:  a and b  /  a or b
public sealed record BoolOpExpr(
    BoolOp Op,
    IReadOnlyList<Expression> Values,
    int Line, int Column
) : Expression(Line, Column);

// Walrus operator:  (x := expr)
public sealed record WalrusExpr(
    string Target,
    Expression Value,
    int Line, int Column
) : Expression(Line, Column);

// Ternary:  x if cond else y
public sealed record IfExpr(
    Expression Condition,
    Expression Then,
    Expression Else,
    int Line, int Column
) : Expression(Line, Column);

// Function call:  f(a, b, key=val, *args, **kwargs)
public sealed record CallExpr(
    Expression Func,
    IReadOnlyList<Argument> Args,
    int Line, int Column
) : Expression(Line, Column);

// Attribute access:  obj.name
public sealed record AttributeExpr(
    Expression Object,
    string Attribute,
    int Line, int Column
) : Expression(Line, Column);

// Subscript:  obj[key]
public sealed record SubscriptExpr(
    Expression Object,
    Expression Index,
    int Line, int Column
) : Expression(Line, Column);

// Slice:  a[1:10:2]
public sealed record SliceExpr(
    Expression? Lower,
    Expression? Upper,
    Expression? Step,
    int Line, int Column
) : Expression(Line, Column);

// Lambda:  lambda x, y: x + y
public sealed record LambdaExpr(
    IReadOnlyList<Parameter> Params,
    Expression Body,
    int Line, int Column
) : Expression(Line, Column);

// List literal:  [1, 2, 3]
public sealed record ListExpr(
    IReadOnlyList<Expression> Elements,
    int Line, int Column
) : Expression(Line, Column);

// Tuple literal:  (1, 2, 3)  or  1, 2, 3
public sealed record TupleExpr(
    IReadOnlyList<Expression> Elements,
    int Line, int Column
) : Expression(Line, Column);

// Set literal:  {1, 2, 3}
public sealed record SetExpr(
    IReadOnlyList<Expression> Elements,
    int Line, int Column
) : Expression(Line, Column);

// Dict literal:  {a: b, c: d}
public sealed record DictExpr(
    IReadOnlyList<(Expression? Key, Expression Value)> Pairs,  // Key=null means **unpack
    int Line, int Column
) : Expression(Line, Column);

// List comprehension:  [x for x in xs if pred]
public sealed record ListCompExpr(
    Expression Element,
    IReadOnlyList<Comprehension> Generators,
    int Line, int Column
) : Expression(Line, Column);

// Set comprehension:  {x for x in xs}
public sealed record SetCompExpr(
    Expression Element,
    IReadOnlyList<Comprehension> Generators,
    int Line, int Column
) : Expression(Line, Column);

// Dict comprehension:  {k: v for k, v in items}
public sealed record DictCompExpr(
    Expression Key,
    Expression Value,
    IReadOnlyList<Comprehension> Generators,
    int Line, int Column
) : Expression(Line, Column);

// Generator expression:  (x for x in xs)
public sealed record GeneratorExpr(
    Expression Element,
    IReadOnlyList<Comprehension> Generators,
    int Line, int Column
) : Expression(Line, Column);

// Starred expression:  *args  (in calls and assignments)
public sealed record StarredExpr(
    Expression Value,
    int Line, int Column
) : Expression(Line, Column);

// Await expression:  await coro
public sealed record AwaitExpr(
    Expression Value,
    int Line, int Column
) : Expression(Line, Column);

// Yield expression:  yield x  /  yield from x
public sealed record YieldExpr(
    Expression? Value,
    bool IsFrom,
    int Line, int Column
) : Expression(Line, Column);

// F-string (simplified — full template body stored as raw string for Phase 1)
public sealed record FStringExpr(
    string RawTemplate,
    int Line, int Column
) : Expression(Line, Column);

// ── Supporting types ──────────────────────────────────────────────────────────

/// <summary>A single argument in a function call.</summary>
public sealed record Argument(
    string? Keyword,        // null = positional
    Expression Value,
    bool IsStar,            // *args
    bool IsDoubleStar       // **kwargs
);

/// <summary>A parameter in a function/lambda definition.</summary>
public sealed record Parameter(
    string Name,
    Expression? Annotation,
    Expression? Default,
    bool IsStar,            // *args
    bool IsDoubleStar,      // **kwargs
    bool IsKeywordOnly,     // after bare *
    bool IsPositionalOnly   // before / (PEP 570)
);

/// <summary>A for-clause in a comprehension.</summary>
public sealed record Comprehension(
    Expression Target,
    Expression Iter,
    IReadOnlyList<Expression> Conditions,
    bool IsAsync
);



// ── Operator enums ────────────────────────────────────────────────────────────

public enum UnaryOp  { Pos, Neg, Invert, Not }

public enum BinaryOp
{
    Add, Sub, Mul, Div, FloorDiv, Mod, Pow, MatMul,
    BitAnd, BitOr, BitXor, LShift, RShift
}

public enum CompareOp
{
    Eq, NotEq, Lt, LtEq, Gt, GtEq,
    Is, IsNot, In, NotIn
}

public enum BoolOp { And, Or }

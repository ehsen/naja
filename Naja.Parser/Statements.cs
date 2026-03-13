namespace Naja.Parser;

// ── Statements ────────────────────────────────────────────────────────────────

public abstract record Statement(int Line, int Column) : AstNode(Line, Column);

// Expression statement:  func()  /  x + 1
public sealed record ExprStatement(
    Expression Expr,
    int Line, int Column
) : Statement(Line, Column);

// Assignment:  x = 1  /  a = b = 0
public sealed record AssignStatement(
    IReadOnlyList<Expression> Targets,
    Expression Value,
    int Line, int Column
) : Statement(Line, Column);

// Annotated assignment:  x: int = 5
public sealed record AnnAssignStatement(
    Expression Target,
    Expression Annotation,
    Expression? Value,
    int Line, int Column
) : Statement(Line, Column);

// Augmented assignment:  x += 1
public sealed record AugAssignStatement(
    Expression Target,
    BinaryOp Op,
    Expression Value,
    int Line, int Column
) : Statement(Line, Column);

// Return:  return x
public sealed record ReturnStatement(
    Expression? Value,
    int Line, int Column
) : Statement(Line, Column);

// Delete:  del x, y
public sealed record DeleteStatement(
    IReadOnlyList<Expression> Targets,
    int Line, int Column
) : Statement(Line, Column);

// Pass
public sealed record PassStatement(int Line, int Column) : Statement(Line, Column);

// Break
public sealed record BreakStatement(int Line, int Column) : Statement(Line, Column);

// Continue
public sealed record ContinueStatement(int Line, int Column) : Statement(Line, Column);

// Raise:  raise  /  raise Ex  /  raise Ex from cause
public sealed record RaiseStatement(
    Expression? Exception,
    Expression? Cause,
    int Line, int Column
) : Statement(Line, Column);

// Assert:  assert cond  /  assert cond, msg
public sealed record AssertStatement(
    Expression Test,
    Expression? Message,
    int Line, int Column
) : Statement(Line, Column);

// Global:  global x, y
public sealed record GlobalStatement(
    IReadOnlyList<string> Names,
    int Line, int Column
) : Statement(Line, Column);

// Nonlocal:  nonlocal x, y
public sealed record NonlocalStatement(
    IReadOnlyList<string> Names,
    int Line, int Column
) : Statement(Line, Column);

// Import:  import os  /  import os.path as p
public sealed record ImportStatement(
    IReadOnlyList<ImportAlias> Names,
    int Line, int Column
) : Statement(Line, Column);

// From import:  from os import path, getcwd
public sealed record FromImportStatement(
    string Module,
    IReadOnlyList<ImportAlias> Names,  // empty = *
    int Level,                          // dots for relative import
    int Line, int Column
) : Statement(Line, Column);

// If / elif / else
public sealed record IfStatement(
    Expression Condition,
    IReadOnlyList<Statement> Then,
    IReadOnlyList<(Expression Cond, IReadOnlyList<Statement> Body)> Elifs,
    IReadOnlyList<Statement> Else,
    int Line, int Column
) : Statement(Line, Column);

// While loop
public sealed record WhileStatement(
    Expression Condition,
    IReadOnlyList<Statement> Body,
    IReadOnlyList<Statement> Else,
    int Line, int Column
) : Statement(Line, Column);

// For loop
public sealed record ForStatement(
    Expression Target,
    Expression Iter,
    IReadOnlyList<Statement> Body,
    IReadOnlyList<Statement> Else,
    bool IsAsync,
    int Line, int Column
) : Statement(Line, Column);

// Try / except / else / finally
public sealed record TryStatement(
    IReadOnlyList<Statement> Body,
    IReadOnlyList<ExceptHandler> Handlers,
    IReadOnlyList<Statement> Else,
    IReadOnlyList<Statement> Finally,
    int Line, int Column
) : Statement(Line, Column);

// With statement:  with open(f) as x:
public sealed record WithStatement(
    IReadOnlyList<WithItem> Items,
    IReadOnlyList<Statement> Body,
    bool IsAsync,
    int Line, int Column
) : Statement(Line, Column);

// Function definition
public sealed record FunctionDef(
    string Name,
    IReadOnlyList<Parameter> Params,
    IReadOnlyList<Statement> Body,
    Expression? ReturnAnnotation,
    IReadOnlyList<Expression> Decorators,
    bool IsAsync,
    int Line, int Column
) : Statement(Line, Column);

// Class definition
public sealed record ClassDef(
    string Name,
    IReadOnlyList<Expression> Bases,
    IReadOnlyList<Argument> Keywords,      // metaclass=Meta etc.
    IReadOnlyList<Statement> Body,
    IReadOnlyList<Expression> Decorators,
    int Line, int Column
) : Statement(Line, Column);

// Match statement (Python 3.10+)
public sealed record MatchStatement(
    Expression Subject,
    IReadOnlyList<MatchCase> Cases,
    int Line, int Column
) : Statement(Line, Column);

// Type alias (Python 3.12+):  type Point = tuple[int, int]
public sealed record TypeAliasStatement(
    string Name,
    Expression Value,
    int Line, int Column
) : Statement(Line, Column);

// ── Supporting types ──────────────────────────────────────────────────────────

public sealed record ImportAlias(string Name, string? Alias);

public sealed record ExceptHandler(
    Expression? ExceptionType,
    string? Name,
    
    IReadOnlyList<Statement> Body,
    bool isStar,  // except* for exception groups (Python 3.11+) PEP 564
    int Line, int Column
);

public sealed record WithItem(Expression Context, Expression? Target);

public sealed record MatchCase(
    Pattern Pattern,
    Expression? Guard,
    IReadOnlyList<Statement> Body
);

// ── Patterns (for match/case) ─────────────────────────────────────────────────

public abstract record Pattern(int Line, int Column) : AstNode(Line, Column);

public sealed record WildcardPattern(int Line, int Column)                          : Pattern(Line, Column);
public sealed record CapturePattern(string Name, int Line, int Column)              : Pattern(Line, Column);
public sealed record LiteralPattern(Expression Value, int Line, int Column)         : Pattern(Line, Column);
public sealed record OrPattern(IReadOnlyList<Pattern> Patterns, int Line, int Column) : Pattern(Line, Column);
public sealed record SequencePattern(IReadOnlyList<Pattern> Patterns, int Line, int Column) : Pattern(Line, Column);
public sealed record MappingPattern(
    IReadOnlyList<(Expression Key, Pattern Value)> Pairs,
    string? Rest,
    int Line, int Column
) : Pattern(Line, Column);
public sealed record ClassPattern(
    Expression Cls,
    IReadOnlyList<Pattern> Positional,
    IReadOnlyList<(string Name, Pattern Value)> Keyword,
    int Line, int Column
) : Pattern(Line, Column);

/// <summary>Pattern: inner_pattern as name</summary>
public sealed record AsPattern(
    Pattern Inner,
    string Name,
    int Line, int Column
) : Pattern(Line, Column);

/// <summary>Pattern: *name or *_ in a sequence pattern</summary>
public sealed record StarPattern(
    string? Name,  // Name == null or "_" means discard (*_)
    int Line, int Column
) : Pattern(Line, Column);

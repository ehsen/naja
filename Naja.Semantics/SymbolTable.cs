namespace Naja.Semantics;

// ── Symbol kinds ──────────────────────────────────────────────────────────────

public enum SymbolKind
{
    Variable,       // x = 1
    Parameter,      // def f(x):
    Function,       // def f():
    Class,          // class Foo:
    Import,         // import os  /  from os import path
    Builtin,        // print, range, len, ...
}

// ── Symbol ────────────────────────────────────────────────────────────────────

/// <summary>
/// A single declared name in a scope.
/// Tracks the type, declaration location, and whether it has been assigned.
/// </summary>
public sealed class Symbol
{
    public string     Name         { get; }
    public SymbolKind Kind         { get; }
    public NajaType   Type         { get; set; }
    public int        Line         { get; }
    public int        Column       { get; }
    public bool       IsAssigned   { get; set; }
    public bool       IsGlobal     { get; set; }   // declared with 'global'
    public bool       IsNonlocal   { get; set; }   // declared with 'nonlocal'

    public Symbol(string name, SymbolKind kind, NajaType type, int line, int col)
    {
        Name       = name;
        Kind       = kind;
        Type       = type;
        Line       = line;
        Column     = col;
        IsAssigned = false;
    }

    public override string ToString() =>
        $"{Kind} '{Name}': {Type}  @ L{Line}:C{Column}";
}

// ── Scope kinds ───────────────────────────────────────────────────────────────

public enum ScopeKind
{
    Module,         // top-level
    Function,       // def / async def
    Class,          // class
    Comprehension,  // [x for x in ...]  — has its own scope in Python 3
    Lambda,         // lambda
}

// ── SymbolTable ───────────────────────────────────────────────────────────────

/// <summary>
/// A single lexical scope — holds all symbols declared in that scope
/// and a reference to the enclosing parent scope.
///
/// Lookup walks up the chain:  local → enclosing functions → module → builtins
/// </summary>
public sealed class SymbolTable
{
    private readonly Dictionary<string, Symbol> _symbols = new();

    public SymbolTable?  Parent    { get; }
    public ScopeKind     Kind      { get; }
    public string        Name      { get; }   // function/class name or "<module>"
    public List<SymbolTable> Children { get; } = new();

    public SymbolTable(ScopeKind kind, string name, SymbolTable? parent)
    {
        Kind   = kind;
        Name   = name;
        Parent = parent;
        parent?.Children.Add(this);
    }

    // ── Define ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Declare a new symbol in THIS scope.
    /// If already declared, updates the type (re-assignment).
    /// </summary>
    public Symbol Define(string name, SymbolKind kind, NajaType type, int line, int col)
    {
        if (_symbols.TryGetValue(name, out var existing))
        {
            // Re-assignment — widen the type
            existing.Type       = NajaTypes.Widen(existing.Type, type);
            existing.IsAssigned = true;
            return existing;
        }

        var sym = new Symbol(name, kind, type, line, col) { IsAssigned = true };
        _symbols[name] = sym;
        return sym;
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Look up a name starting from this scope, walking up the chain.
    /// Returns null if not found anywhere.
    /// </summary>
    public Symbol? Lookup(string name)
    {
        if (_symbols.TryGetValue(name, out var sym))
            return sym;
        return Parent?.Lookup(name);
    }

    /// <summary>
    /// Look up only in THIS scope — does not walk up.
    /// </summary>
    public Symbol? LookupLocal(string name) =>
        _symbols.TryGetValue(name, out var sym) ? sym : null;

    // ── Enumerate ─────────────────────────────────────────────────────────────

    public IEnumerable<Symbol> LocalSymbols => _symbols.Values;

    public bool Contains(string name) => _symbols.ContainsKey(name);

    // ── Debug ─────────────────────────────────────────────────────────────────

    public void Dump(int indent = 0)
    {
        var pad = new string(' ', indent * 2);
        Console.WriteLine($"{pad}[{Kind}] {Name}");
        foreach (var s in _symbols.Values)
            Console.WriteLine($"{pad}  {s}");
        foreach (var child in Children)
            child.Dump(indent + 1);
    }

    public override string ToString() => $"SymbolTable({Kind}: {Name})";
}

// ── Builtin scope ─────────────────────────────────────────────────────────────

/// <summary>
/// Pre-populated scope containing Python's built-in names.
/// This is the root of every lookup chain.
/// </summary>
public static class BuiltinScope
{
    public static SymbolTable Create()
    {
        var scope = new SymbolTable(ScopeKind.Module, "<builtins>", null);

        // Built-in functions
        void Fn(string name, NajaType ret, params NajaType[] args) =>
            scope.Define(name, SymbolKind.Builtin,
                new FunctionType(args, ret), 0, 0);

        Fn("print",     NajaTypes.None,    NajaTypes.Unknown);
        Fn("input",     NajaTypes.Str,     NajaTypes.Str);
        Fn("len",       NajaTypes.Int,     NajaTypes.Unknown);
        Fn("range",     new ListType(NajaTypes.Int), NajaTypes.Int);
        Fn("int",       NajaTypes.Int,     NajaTypes.Unknown);
        Fn("float",     NajaTypes.Float,   NajaTypes.Unknown);
        Fn("str",       NajaTypes.Str,     NajaTypes.Unknown);
        Fn("bool",      NajaTypes.Bool,    NajaTypes.Unknown);
        Fn("list",      new ListType(NajaTypes.Unknown));
        Fn("dict",      new DictType(NajaTypes.Unknown, NajaTypes.Unknown));
        Fn("set",       new SetType(NajaTypes.Unknown));
        Fn("frozenset", new FrozenSetType(NajaTypes.Unknown));
        Fn("tuple",     new TupleType([]));
        Fn("abs",       NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("max",       NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("min",       NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("sum",       NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("sorted",    new ListType(NajaTypes.Unknown), NajaTypes.Unknown);
        Fn("reversed",  NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("enumerate", NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("zip",       NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("map",       NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("filter",    NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("open",      NajaTypes.Unknown, NajaTypes.Str);
        Fn("isinstance",NajaTypes.Bool,    NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("issubclass",NajaTypes.Bool,    NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("hasattr",   NajaTypes.Bool,    NajaTypes.Unknown, NajaTypes.Str);
        Fn("getattr",   NajaTypes.Unknown, NajaTypes.Unknown, NajaTypes.Str);
        Fn("setattr",   NajaTypes.None,    NajaTypes.Unknown, NajaTypes.Str, NajaTypes.Unknown);
        Fn("type",      NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("repr",      NajaTypes.Str,     NajaTypes.Unknown);
        Fn("id",        NajaTypes.Int,     NajaTypes.Unknown);
        Fn("hash",      NajaTypes.Int,     NajaTypes.Unknown);
        Fn("iter",      NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("next",      NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("super",     NajaTypes.Unknown);
        Fn("vars",      NajaTypes.Unknown, NajaTypes.Unknown);
        Fn("dir",       new ListType(NajaTypes.Str), NajaTypes.Unknown);

        // Built-in exceptions (as class types)
        foreach (var ex in new[]
        {
            "Exception", "BaseException", "ValueError", "TypeError",
            "KeyError", "IndexError", "AttributeError", "RuntimeError",
            "StopIteration", "NotImplementedError", "OSError", "IOError",
            "FileNotFoundError", "PermissionError", "KeyboardInterrupt",
            "SystemExit", "OverflowError", "ZeroDivisionError",
            "MemoryError", "RecursionError", "ImportError", "NameError",
            "GeneratorExit", "ArithmeticError", "LookupError",
            "UnicodeError", "UnicodeDecodeError", "UnicodeEncodeError",
            "BufferError", "EOFError", "ConnectionError", "TimeoutError",
            "StopAsyncIteration", "AssertionError", "FloatingPointError",
            "UnboundLocalError", "ModuleNotFoundError", "IsADirectoryError",
            "NotADirectoryError", "InterruptedError", "ProcessLookupError",
            "ChildProcessError", "BrokenPipeError", "ConnectionAbortedError",
            "ConnectionRefusedError", "ConnectionResetError", "BlockingIOError",
            "SyntaxError", "IndentationError", "TabError",
        })
            scope.Define(ex, SymbolKind.Builtin, new ClassType(ex), 0, 0);

        // Built-in types exposed as class names
        scope.Define("object",  SymbolKind.Builtin, new ClassType("object"),  0, 0);
        scope.Define("bytes",   SymbolKind.Builtin, new ClassType("bytes"),   0, 0);
        scope.Define("complex", SymbolKind.Builtin, new ClassType("complex"), 0, 0);
        scope.Define("bytearray", SymbolKind.Builtin, new ClassType("bytearray"), 0, 0);
        scope.Define("memoryview", SymbolKind.Builtin, new ClassType("memoryview"), 0, 0);

        // Built-in constants
        scope.Define("True",     SymbolKind.Builtin, NajaTypes.Bool,    0, 0);
        scope.Define("False",    SymbolKind.Builtin, NajaTypes.Bool,    0, 0);
        scope.Define("None",     SymbolKind.Builtin, NajaTypes.None,    0, 0);
        scope.Define("NotImplemented", SymbolKind.Builtin, NajaTypes.Unknown, 0, 0);
        scope.Define("Ellipsis", SymbolKind.Builtin, NajaTypes.Unknown, 0, 0);
        scope.Define("__name__", SymbolKind.Builtin, NajaTypes.Str,     0, 0);
        scope.Define("__file__", SymbolKind.Builtin, NajaTypes.Str,     0, 0);

        return scope;
    }
}

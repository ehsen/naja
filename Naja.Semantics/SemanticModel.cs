using Naja.Parser;

namespace Naja.Semantics;

/// <summary>
/// The result of semantic analysis.
/// The IL emitter queries this to get the type of any expression
/// and the symbol for any name reference.
/// </summary>
public sealed class SemanticModel
{
    private readonly Dictionary<AstNode, NajaType> _types;
    private readonly Dictionary<AstNode, Symbol>   _symbols;

    public SymbolTable    ModuleScope  { get; }
    public DiagnosticBag  Diagnostics  { get; }
    public bool           IsValid      => !Diagnostics.HasErrors;

    internal SemanticModel(
        SymbolTable scope,
        DiagnosticBag diagnostics,
        Dictionary<AstNode, NajaType> types,
        Dictionary<AstNode, Symbol> symbols)
    {
        ModuleScope  = scope;
        Diagnostics  = diagnostics;
        _types       = types;
        _symbols     = symbols;
    }

    /// <summary>Get the inferred type of any expression node.</summary>
    public NajaType GetType(AstNode node) =>
        _types.TryGetValue(node, out var t) ? t : NajaTypes.Unknown;

    /// <summary>Get the symbol a name expression resolves to.</summary>
    public Symbol? GetSymbol(AstNode node) =>
        _symbols.TryGetValue(node, out var s) ? s : null;

    /// <summary>Get all symbols in the module scope.</summary>
    public IEnumerable<Symbol> ModuleSymbols =>
        ModuleScope.LocalSymbols;

    /// <summary>Print a summary of the analysis results.</summary>
    public void Dump()
    {
        Console.WriteLine("=== Semantic Model ===");
        Console.WriteLine($"Errors:   {Diagnostics.ErrorCount}");
        Console.WriteLine($"Warnings: {Diagnostics.WarningCount}");
        Console.WriteLine();
        Console.WriteLine("=== Symbol Table ===");
        ModuleScope.Dump();
        Console.WriteLine();
        if (Diagnostics.All.Count > 0)
        {
            Console.WriteLine("=== Diagnostics ===");
            foreach (var d in Diagnostics.All)
                Console.WriteLine(d);
        }
    }
}

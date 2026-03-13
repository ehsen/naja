namespace Naja.Inference;

/// <summary>
/// Accumulates compiler diagnostics during the analysis pass and enforces
/// mode-specific rules.
///
/// One sink per compilation unit (one source file).
/// Created by AssemblyEmitter from <see cref="CompilationOptions"/>, passed
/// into <see cref="DynamicSiteAnalyser"/>, and forwarded into EmitContext
/// so the expression emitter can record dynamic() and cast() sites.
///
/// Lifecycle
/// ─────────
///   1. DynamicSiteAnalyser walks the AST and calls Report* methods.
///   2. Caller calls ThrowIfErrors() — aborts in Strict, no-op in Balanced/Permissive.
///   3. CLI reads All / Errors / Warnings to format output.
///   4. EmitContext holds a reference so the expression emitter can append
///      Info diagnostics for dynamic() and cast() sites encountered during emission.
/// </summary>
public sealed class DiagnosticSink
{
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly CompilationMode  _mode;
    private readonly string           _filePath;

    public DiagnosticSink(CompilationMode mode, string filePath = "<source>")
    {
        _mode     = mode;
        _filePath = filePath;
    }

    // ── Accessors ─────────────────────────────────────────────────────────────

    public CompilationMode Mode => _mode;

    public IReadOnlyList<Diagnostic> All      => _diagnostics;
    public IReadOnlyList<Diagnostic> Errors   => _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
    public IReadOnlyList<Diagnostic> Warnings => _diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning).ToList();
    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    // ── Reporting ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Report an expression whose type the inference engine could not resolve.
    ///
    ///  Strict      → Error   (compilation will abort after analysis)
    ///  Balanced    → Warning (compilation continues, DynamicCall emitted)
    ///  Permissive  → no-op   (silent — existing behaviour unchanged)
    /// </summary>
    public void ReportDynamicSite(
        string  expressionDescription,
        int     line,
        int     column,
        string? fixHint = null)
    {
        switch (_mode)
        {
            case CompilationMode.Strict:
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Type of '{expressionDescription}' cannot be resolved statically. " +
                    $"In --strict mode every expression must have a known type.",
                    _filePath, line, column,
                    fixHint ?? GenerateHint(expressionDescription)));
                break;

            case CompilationMode.Balanced:
                _diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Dynamic site: '{expressionDescription}' has unknown type. " +
                    $"DynamicCall will be emitted. Output is not AOT-safe.",
                    _filePath, line, column,
                    fixHint));
                break;

            case CompilationMode.Permissive:
                break; // silent — matches pre-mode-system behaviour exactly
        }
    }

    /// <summary>
    /// Report an explicit <c>dynamic(expr)</c> call.
    ///
    /// dynamic() is always allowed in all modes — it is the developer's explicit
    /// opt-in to dynamic dispatch.  In Strict mode we record an Info so the
    /// developer has a full inventory of every dynamic site in their codebase.
    /// In Balanced and Permissive we stay silent (the dynamic dispatch is already
    /// implied by the mode).
    /// </summary>
    public void ReportExplicitDynamic(
        string expressionDescription,
        int    line,
        int    column)
    {
        if (_mode == CompilationMode.Strict)
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Info,
                $"Explicit dynamic() at '{expressionDescription}' — DynamicCall will be emitted.",
                _filePath, line, column));
        }
    }

    /// <summary>
    /// Report a <c>cast(T, expr)</c> type assertion.
    ///
    /// cast() is always allowed and always informational — it means the developer
    /// is asserting a type the compiler cannot prove.  Recorded in Strict and
    /// Balanced modes so the developer can audit all assertion points.
    /// Silent in Permissive mode (no point adding noise when everything is silent).
    /// </summary>
    public void ReportCast(
        string targetTypeName,
        string expressionDescription,
        int    line,
        int    column)
    {
        if (_mode is CompilationMode.Strict or CompilationMode.Balanced)
        {
            _diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Info,
                $"cast({targetTypeName}, {expressionDescription}) — " +
                $"type assertion, verify correctness at this site.",
                _filePath, line, column));
        }
    }

    // ── Enforcement ───────────────────────────────────────────────────────────

    /// <summary>
    /// Throw <see cref="CompilationModeException"/> if any errors were recorded.
    /// Call this after <see cref="DynamicSiteAnalyser.Analyse"/> completes and
    /// before IL emission begins.
    ///
    /// In Balanced and Permissive modes this is always a no-op.
    /// </summary>
    public void ThrowIfErrors()
    {
        if (!HasErrors) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"\nNaja compilation failed — {Errors.Count} error(s):\n");
        foreach (var err in Errors)
            sb.AppendLine(err.ToString());
        sb.AppendLine();
        sb.AppendLine("To allow dynamic dispatch at a specific site:");
        sb.AppendLine("  dynamic(expr)        — explicit opt-in for one expression");
        sb.AppendLine("  cast(type, expr)     — assert a known type on an expression");
        sb.AppendLine();
        sb.AppendLine("To change the compilation mode globally:");
        sb.AppendLine("  --balanced           — warn instead of error (not AOT-safe)");
        sb.AppendLine("  --permissive         — silent fallback (current default behaviour)");

        throw new CompilationModeException(sb.ToString(), _diagnostics.AsReadOnly());
    }

    // ── Output ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Write all diagnostics to <see cref="Console.Error"/> in CLI format.
    /// Called by the CLI after compilation to display warnings.
    /// </summary>
    public void PrintAll()
    {
        foreach (var d in _diagnostics)
            Console.Error.WriteLine(d);
    }

    // ── Hint generation ───────────────────────────────────────────────────────

    private static string GenerateHint(string exprDesc)
    {
        if (exprDesc.Contains('.'))
            return $"Annotate the object type, or use: cast(int, {exprDesc})";
        if (exprDesc.StartsWith("call:"))
            return "Add a return type annotation to the function: def f(...) -> int:";
        return "Add a type annotation, or wrap in dynamic() to opt in explicitly.";
    }
}

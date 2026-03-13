namespace Naja.Semantics;

public enum DiagnosticSeverity { Error, Warning, Info }

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string             Message,
    int                Line,
    int                Column
)
{
    public override string ToString() =>
        $"[{Severity}] L{Line}:C{Column} — {Message}";
}

/// <summary>
/// Collects diagnostics during semantic analysis.
/// Analysis continues even after errors — we collect all of them.
/// </summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<Diagnostic> All         => _diagnostics;
    public bool                      HasErrors    => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    public int                       ErrorCount   => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    public int                       WarningCount => _diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

    public void Error  (string msg, int line, int col) =>
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error,   msg, line, col));

    public void Warning(string msg, int line, int col) =>
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, msg, line, col));

    public void Info   (string msg, int line, int col) =>
        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,    msg, line, col));

    public void Clear() => _diagnostics.Clear();

    public override string ToString() =>
        string.Join(Environment.NewLine, _diagnostics);
}

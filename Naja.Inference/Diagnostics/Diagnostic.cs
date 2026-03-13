namespace Naja.Inference;

/// <summary>Severity level of a compiler diagnostic.</summary>
public enum DiagnosticSeverity
{
    /// <summary>
    /// Informational — compilation continues normally.
    /// Used for explicit dynamic() and cast() sites so the developer has
    /// a full inventory of every dynamic dispatch in their codebase.
    /// </summary>
    Info,

    /// <summary>
    /// Dynamic site warning (Balanced mode).
    /// Compilation succeeds but output is not AOT-safe.
    /// </summary>
    Warning,

    /// <summary>
    /// Hard compile error (Strict mode).
    /// Compilation is aborted after the analysis pass via
    /// <see cref="DiagnosticSink.ThrowIfErrors"/>.
    /// </summary>
    Error
}

/// <summary>
/// A single compiler diagnostic message with source location and an optional fix hint.
/// Immutable — produced by <see cref="DiagnosticSink"/> and read by the CLI formatter.
/// </summary>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string             Message,
    string             FilePath,
    int                Line,
    int                Column,
    string?            FixHint = null)
{
    /// <summary>
    /// Formats the diagnostic as a single human-readable string suitable for
    /// printing to the console.  Format mirrors clang/rustc conventions:
    ///   ERROR  path/to/file.py:10:5  message
    ///          Hint: suggested fix
    /// </summary>
    public override string ToString()
    {
        var tag = Severity switch
        {
            DiagnosticSeverity.Error   => "ERROR",
            DiagnosticSeverity.Warning => "WARN ",
            _                          => "INFO "
        };

        var hint = FixHint is not null
            ? $"\n         Hint: {FixHint}"
            : string.Empty;

        return $"  {tag}  {FilePath}:{Line}:{Column}  {Message}{hint}";
    }
}

namespace Naja.Inference;

/// <summary>
/// Thrown by <see cref="DiagnosticSink.ThrowIfErrors"/> when the analysis pass
/// found one or more unresolvable type sites in Strict mode.
///
/// The CLI catches this at the top level, formats the diagnostics, and exits
/// with a non-zero status code.  AssemblyEmitter never sees it — emission is
/// aborted before it starts.
///
/// Carries the full diagnostic list so the CLI can format all errors at once
/// rather than stopping at the first one.
/// </summary>
public sealed class CompilationModeException : Exception
{
    /// <summary>All diagnostics recorded before the exception was thrown.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public CompilationModeException(
        string message,
        IReadOnlyList<Diagnostic> diagnostics)
        : base(message)
    {
        Diagnostics = diagnostics;
    }
}

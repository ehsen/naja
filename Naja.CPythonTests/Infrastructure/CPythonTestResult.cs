namespace Naja.CPythonTests.Infrastructure;

/// <summary>
/// Parsed result of a CPython test run — produced by <see cref="UnittestOutputParser"/>.
/// Wraps <see cref="ExecutionResult"/> with structured pass/fail counts and a readable
/// failure summary for xUnit assertion messages.
/// </summary>
public sealed class CPythonTestResult
{
    public bool    Passed          { get; init; }
    public int     ExitCode        { get; init; }
    public int     PassCount       { get; init; }
    public int     FailCount       { get; init; }
    public int     ErrorCount      { get; init; }
    public string  Stdout          { get; init; } = "";
    public string  Stderr          { get; init; } = "";
    public string? FailureSummary  { get; init; }

    /// <summary>Represents a compilation failure before any tests ran.</summary>
    public static CPythonTestResult CompilationFailure(IEnumerable<string> errors)
    {
        var msg = string.Join("\n", errors);
        return new CPythonTestResult
        {
            Passed        = false,
            ExitCode      = -1,
            FailureSummary = $"COMPILATION FAILED:\n{msg}",
            Stderr         = msg,
        };
    }

    /// <summary>Represents a compilation failure before any tests ran.</summary>
    public static CPythonTestResult CompilationFailure(string error) =>
        CompilationFailure([error]);

    /// <summary>Inline snippet that ran successfully (no unittest runner involved).</summary>
    public static CPythonTestResult SnippetPassed() =>
        new() { Passed = true, ExitCode = 0 };

    /// <summary>Inline snippet that threw an exception.</summary>
    public static CPythonTestResult SnippetFailed(string errorMessage) =>
        new() { Passed = false, ExitCode = 1, FailureSummary = errorMessage };
}

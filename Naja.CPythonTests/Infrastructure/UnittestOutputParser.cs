using System.Text.RegularExpressions;

namespace Naja.CPythonTests.Infrastructure;

/// <summary>
/// Parses the stderr output produced by <see cref="Naja.StdLib.NajaUnittest.main()"/>.
///
/// NajaUnittest writes to Console.Error in Python's standard unittest format:
///   .....F.E             ← one character per test (dot=pass, F=fail, E=error, s=skip)
///   Ran N tests
///
///   FAILURES:
///   FAIL: ClassName.method   ← optional section, only when failures exist
///     AssertionError: message
///   ERROR: ClassName.method
///     SomeException: message
///   FAILED (failures=X, errors=Y)   ← or "OK"
/// </summary>
public static class UnittestOutputParser
{
    private static readonly Regex RanPattern     = new(@"Ran (\d+) test",                            RegexOptions.Compiled);
    private static readonly Regex FailedPattern  = new(@"FAILED \(failures=(\d+),\s*errors=(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex OkPattern      = new(@"(?m)^OK\s*$",                               RegexOptions.Compiled);

    /// <summary>
    /// Parse an <see cref="ExecutionResult"/> (from NajaTestExecutor) into a structured
    /// <see cref="CPythonTestResult"/>.
    /// </summary>
    public static CPythonTestResult Parse(ExecutionResult execution)
    {
        // Compilation or file-not-found failure — no unittest output at all.
        if (execution.ExitCode == -1 && string.IsNullOrWhiteSpace(execution.Stderr))
            return CPythonTestResult.CompilationFailure(execution.Stdout.Trim());

        var stderr = execution.Stderr;

        var ranMatch    = RanPattern.Match(stderr);
        var failedMatch = FailedPattern.Match(stderr);
        bool ok         = OkPattern.IsMatch(stderr) && !failedMatch.Success;

        int totalRan  = ranMatch.Success ? int.Parse(ranMatch.Groups[1].Value) : 0;
        int failures  = 0, errors = 0;
        if (failedMatch.Success)
        {
            failures = int.Parse(failedMatch.Groups[1].Value);
            errors   = int.Parse(failedMatch.Groups[2].Value);
        }

        // If there's no unittest output at all but exit code is non-zero, the script
        // threw before reaching unittest.main() (e.g. import error, top-level exception).
        if (!ranMatch.Success && execution.ExitCode != 0)
        {
            ok = false;
        }

        string? failureSummary = null;
        if (!ok)
        {
            var failIdx = stderr.IndexOf("FAILURES:", StringComparison.Ordinal);
            if (failIdx >= 0)
                failureSummary = stderr[failIdx..];
            else if (!string.IsNullOrWhiteSpace(stderr))
                failureSummary = stderr.Trim();
            else if (!string.IsNullOrWhiteSpace(execution.Stdout))
                failureSummary = execution.Stdout.Trim();
        }

        int passCount = Math.Max(0, totalRan - failures - errors);

        return new CPythonTestResult
        {
            Passed        = ok,
            ExitCode      = execution.ExitCode,
            PassCount     = passCount,
            FailCount     = failures,
            ErrorCount    = errors,
            Stdout        = execution.Stdout,
            Stderr        = stderr,
            FailureSummary = failureSummary,
        };
    }
}

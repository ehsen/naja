namespace Naja.CPythonTests.Infrastructure;

/// <summary>
/// Raw stdout / stderr captured from a single Python test execution.
/// Produced by <see cref="NajaTestExecutor.Run"/>.
/// </summary>
public sealed class ExecutionResult
{
    public int    ExitCode  { get; init; }
    public string Stdout    { get; init; } = "";
    public string Stderr    { get; init; } = "";
    public bool   TimedOut  { get; init; }

    public static ExecutionResult Timeout(string stdout, string stderr) =>
        new() { ExitCode = -1, Stdout = stdout, Stderr = stderr + "\n[TIMEOUT]", TimedOut = true };
}

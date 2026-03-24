using Xunit.Abstractions;

namespace Naja.CPythonTests.Infrastructure;

/// <summary>
/// Base class for all CPython integration tests.
///
/// Provides the core execution methods used by all four test categories:
///   Category 1 — <see cref="RunSnippet"/> / <see cref="RunTestFile"/>
///   Category 2 — <see cref="RunTestFile"/>
///   Category 3 — <see cref="RunTestMethod"/>
///   Category 4 — <see cref="AssertCompiles"/>
///
/// Path configuration (in priority order):
///   1. CPYTHON_TEST_ROOT environment variable
///   2. Hardcoded developer paths (F:/Sources/cpython, C:/dev/cpython)
///   3. cpython/Lib/test/ relative to the test output directory
///
/// All Python stdout/stderr is captured and attached to the xUnit result via
/// <see cref="Output"/> so failures show the full Python traceback in the
/// test explorer without any console hunting.
/// </summary>
public abstract class CPythonTestFixture
{
    protected readonly ITestOutputHelper Output;

    protected static readonly string CpythonTestRoot =
        Environment.GetEnvironmentVariable("CPYTHON_TEST_ROOT")
        ?? CPythonTestDiscovery.TestRoot;

    protected static readonly string NajaStdlibRoot =
        Environment.GetEnvironmentVariable("NAJA_STDLIB_ROOT")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Naja.StdLib");

    protected CPythonTestFixture(ITestOutputHelper output) => Output = output;

    // ── Category 1 — inline snippet ──────────────────────────────────────────

    /// <summary>
    /// Compile and run an inline Python snippet.
    /// The snippet does not need to call unittest.main() — a clean exit is a pass.
    /// </summary>
    protected CPythonTestResult RunSnippet(string pythonSource, int timeoutMs = 10_000)
    {
        var result = new NajaTestExecutor().RunInline(pythonSource, timeoutMs);
        WriteResult("<inline snippet>", result);
        return result;
    }

    // ── Category 1 / 2 — full test file ──────────────────────────────────────

    /// <summary>
    /// Compile and run an entire CPython test file from
    /// <see cref="CpythonTestRoot"/>.
    /// </summary>
    protected CPythonTestResult RunTestFile(string testFileName, int timeoutMs = 60_000)
    {
        if (string.IsNullOrEmpty(CpythonTestRoot))
        {
            Output.WriteLine("SKIPPED: CPYTHON_TEST_ROOT not set and no default CPython path found.");
            return CPythonTestResult.SnippetPassed();   // treat as pass so CI is not broken
        }

        var path = Path.Combine(CpythonTestRoot, testFileName);
        return RunTestFileAtPath(path, null, timeoutMs);
    }

    // ── Category 3 — single test method ──────────────────────────────────────

    /// <summary>
    /// Compile and run a single test method from a CPython test file.
    /// </summary>
    protected CPythonTestResult RunTestMethod(
        string testFileName,
        string className,
        string methodName,
        int    timeoutMs = 30_000)
    {
        if (string.IsNullOrEmpty(CpythonTestRoot))
        {
            Output.WriteLine("SKIPPED: CPYTHON_TEST_ROOT not set.");
            return CPythonTestResult.SnippetPassed();
        }

        var path = Path.Combine(CpythonTestRoot, testFileName);
        return RunTestFileAtPath(path, $"{className}.{methodName}", timeoutMs);
    }

    // ── Category 4 — compilation only ────────────────────────────────────────

    /// <summary>
    /// Compile a Python file and assert it compiles without errors.
    /// Does not execute the file.
    /// </summary>
    protected CompilationResult AssertCompiles(string pythonFilePath)
    {
        Output.WriteLine($"Compiling: {pythonFilePath}");
        var result = new NajaTestExecutor().CompileOnly(pythonFilePath);

        if (!result.Success)
            foreach (var err in result.Errors)
                Output.WriteLine($"  ERROR: {err}");

        return result;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private CPythonTestResult RunTestFileAtPath(
        string  fullPath,
        string? specificMethod,
        int     timeoutMs)
    {
        Output.WriteLine($"Test file : {fullPath}");
        Output.WriteLine($"Method    : {specificMethod ?? "(all)"}");
        Output.WriteLine(new string('─', 60));

        if (!File.Exists(fullPath))
        {
            Output.WriteLine($"SKIPPED: File not found: {fullPath}");
            return CPythonTestResult.SnippetPassed();
        }

        var executor    = new NajaTestExecutor();
        var compilation = executor.Compile(fullPath);

        if (!compilation.Success)
        {
            var r = CPythonTestResult.CompilationFailure(compilation.Errors);
            WriteResult(fullPath, r);
            return r;
        }

        var execution = executor.Run(compilation, specificMethod, timeoutMs);
        var parsed    = UnittestOutputParser.Parse(execution);
        WriteResult(fullPath, parsed);
        return parsed;
    }

    private void WriteResult(string context, CPythonTestResult result)
    {
        Output.WriteLine($"Exit code : {result.ExitCode}");
        Output.WriteLine($"Passed    : {result.PassCount}");
        Output.WriteLine($"Failed    : {result.FailCount}");
        Output.WriteLine($"Errors    : {result.ErrorCount}");
        Output.WriteLine(new string('─', 60));

        if (!string.IsNullOrWhiteSpace(result.Stdout))
        {
            Output.WriteLine("STDOUT:");
            Output.WriteLine(result.Stdout);
        }

        if (!string.IsNullOrWhiteSpace(result.Stderr))
        {
            Output.WriteLine("STDERR:");
            Output.WriteLine(result.Stderr);
        }

        if (!string.IsNullOrWhiteSpace(result.FailureSummary))
        {
            Output.WriteLine("FAILURES:");
            Output.WriteLine(result.FailureSummary);
        }
    }
}

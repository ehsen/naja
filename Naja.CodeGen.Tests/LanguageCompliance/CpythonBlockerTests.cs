using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// CPython Blocker Tests — Features that cannot even be attempted yet.
///
/// These tests are INTENTIONALLY SKIPPED (not xfail) because they require
/// language-level features the compiler cannot parse or emit at all:
/// async/await, pattern-matching, except*, complex f-strings, metaclasses, etc.
///
/// NOT COUNTED TOWARD COMPLIANCE METRICS.
///
/// Tracking policy:
///   • Pure-unittest tests with known run-time blockers → CpythonSuiteRunner.cs Phase 2/3 (xfail).
///   • Tests whose very syntax cannot be parsed/emitted   → here (Skip).
///
/// When a blocker is fixed, move the test to CpythonSuiteRunner.cs as xfail,
/// then promote to Phase 1 once it passes.
/// </summary>
[Collection("SerialConsole")]
public sealed class CpythonBlockerTests
{
    private readonly ITestOutputHelper _out;
    private static readonly NajaEngine Engine = new();

    private static readonly string? CpythonRoot =
        Environment.GetEnvironmentVariable("CPYTHON_REPO")
        ?? TryDefault("F:/Sources/cpython")
        ?? TryDefault("C:/dev/cpython")
        ?? TryDefault("~/dev/cpython");

    private static string? TryDefault(string path)
    {
        var expanded = path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return Directory.Exists(expanded) ? expanded : null;
    }

    public CpythonBlockerTests(ITestOutputHelper output) => _out = output;

    private void RunCpythonTest(string testFile, string reason)
    {
        if (CpythonRoot is null)
        {
            _out.WriteLine("SKIPPED: set CPYTHON_REPO env var to your CPython clone path.");
            return;
        }

        var testPath = Path.Combine(CpythonRoot, "Lib", "test", testFile);
        if (!File.Exists(testPath))
        {
            _out.WriteLine($"SKIPPED: {reason} — file not found: {testFile}");
            return;
        }

        _out.WriteLine($"SKIPPED: {reason}");
        _out.WriteLine($"File: {testPath}");
        _out.WriteLine("This test requires a language feature the compiler cannot yet parse or emit.");
        _out.WriteLine("It will be promoted to CpythonSuiteRunner Phase 2 when the blocker is fixed.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SYNTAX / EMIT BLOCKERS — parser or emitter cannot handle these at all
    // ═════════════════════════════════════════════════════════════════════════

    [Fact(Skip = "BLOCKER: Coroutine async/await syntax not yet parsed or emitted.")]
    public void Blocked_Coroutines_AsyncAwait()
        => RunCpythonTest("test_coroutines.py", "async/await coroutine syntax (PEP 492)");

    [Fact(Skip = "BLOCKER: Complex f-string expressions (nested braces, format specs, conversions) not fully supported.")]
    public void Blocked_FString_ComplexExpressions()
        => RunCpythonTest("test_fstring.py", "F-string nested expressions, format specs, !r/!s/!a conversions");

    [Fact(Skip = "BLOCKER: match/case pattern-matching syntax not yet parsed (PEP 634).")]
    public void Blocked_PatternMatching_MatchCase()
        => RunCpythonTest("test_patternmatching.py", "Structural pattern matching (PEP 634)");

    [Fact(Skip = "BLOCKER: except* exception-group syntax not yet emitted (PEP 654).")]
    public void Blocked_ExceptionGroup_ExceptStar()
        => RunCpythonTest("test_exceptiongroup.py", "Exception groups (PEP 654) — except* emit");

    [Fact(Skip = "BLOCKER: PEP 563/649 type annotation lazy evaluation not processed; annotations emitter is no-op.")]
    public void Blocked_TypeAnnotations_Processing()
        => RunCpythonTest("test_typeannotations.py", "Type annotation evaluation (PEP 563 / PEP 649)");

    [Fact(Skip = "BLOCKER: Grammar test uses compile()/eval()/exec() in exec/single/eval modes heavily; full multi-mode compile not implemented.")]
    public void Blocked_Grammar_MultiMode()
        => RunCpythonTest("test_grammar.py", "Grammar in exec/single/eval compile modes");

    [Fact(Skip = "BLOCKER: contextlib imports (contextmanager, suppress, redirect_stdout …) not in NajaStdLib; 'with' edge cases around __exit__ return value.")]
    public void Blocked_Contextlib_WithEdgeCases()
        => RunCpythonTest("test_contextlib.py", "contextlib module — context manager edge cases");

    [Fact(Skip = "BLOCKER: itertools module not in NajaStdLib; iterator protocol (send/throw/close) incomplete.")]
    public void Blocked_Itertools_Protocol()
        => RunCpythonTest("test_itertools.py", "itertools module + full iterator protocol");

    [Fact(Skip = "BLOCKER: Generator expression tracking (yield, send, throw, close, StopIteration) incomplete.")]
    public void Blocked_Generators_Full()
        => RunCpythonTest("test_generators.py", "Full generator protocol (yield / send / throw / close)");
}

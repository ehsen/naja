using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Pillar 1.1 — CPython Test Suite Matrix.
///
/// This runner executes selected .py files from the CPython test suite
/// against the Naja compiler. Because the CPython tests are pure Python
/// (assert-based), they work directly with NajaEngine.Eval().
///
/// SETUP:
///   1. Clone CPython: git clone https://github.com/python/cpython.git
///   2. Set env var: CPYTHON_REPO=C:/dev/cpython
///      OR add to your .runsettings:
///        <Parameter name="CpythonRepoPath" value="C:/dev/cpython" />
///
/// If CPYTHON_REPO is not set, all CPython suite tests are SKIPPED
/// (not failed) so CI can run without the clone when needed.
///
/// IMPORTANT: Not all CPython tests will pass — many test stdlib modules
/// or features not yet implemented in Naja. Use [Trait("xfail", "true")]
/// for known failures rather than deleting the test. The goal is to
/// track progress toward the >95% pass rate target.
/// </summary>
public sealed class CpythonSuiteRunner
{
    private readonly ITestOutputHelper _out;
    private static readonly NajaEngine Engine = new();

    // ── Locate CPython repo ───────────────────────────────────────────────────

    private static readonly string? CpythonRoot =
        // 1. runsettings parameter (set via dotnet test --settings Naja.runsettings)
        // TestContext is not available in xUnit — use env var instead
        Environment.GetEnvironmentVariable("CPYTHON_REPO")
        // 2. Default developer path conventions
        ?? TryDefault("/cpython")
        ?? TryDefault("C:/dev/cpython")
        ?? TryDefault("~/dev/cpython");

    private static string? TryDefault(string path)
    {
        var expanded = path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return Directory.Exists(expanded) ? expanded : null;
    }

    public CpythonSuiteRunner(ITestOutputHelper output) => _out = output;

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Run a file from CPython's Lib/test/ directory.
    /// Skips the test if the CPython repo is not configured.
    /// </summary>
    private void RunCpythonTest(string testFile, bool expectedToFail = false)
    {
        if (CpythonRoot is null)
        {
            _out.WriteLine("SKIPPED: CPYTHON_REPO env var not set. Set it to the path of your CPython clone.");
            return; // Soft skip — don't fail CI without the repo
        }

        var testPath = Path.Combine(CpythonRoot, "Lib", "test", testFile);
        if (!File.Exists(testPath))
            throw new FileNotFoundException(
                $"CPython test file not found: {testPath}\n" +
                $"Check that CPYTHON_REPO={CpythonRoot} points to a valid CPython source tree.");

        _out.WriteLine($"Running: {testPath}");

        if (expectedToFail)
        {
            // Known failure — we expect an exception. Track it for progress metrics.
            var ex = Record.Exception(() => Engine.Eval(testPath));
            if (ex is null)
            {
                _out.WriteLine($"XPASS (unexpected pass) — update xfail status: {testFile}");
                // Don't fail — an unexpected pass is good news.
            }
            else
            {
                _out.WriteLine($"XFAIL (expected): {ex.Message[..Math.Min(200, ex.Message.Length)]}");
            }
        }
        else
        {
            Engine.Eval(testPath); // Throws on failure — xUnit will report it
        }
    }

    // ── CRITICAL priority tests ───────────────────────────────────────────────

    /// <summary>test_grammar.py — core syntax for all constructs. 100% required.</summary>
    [Fact, Trait("cpython", "critical")]
    public void CPython_Grammar() => RunCpythonTest("test_grammar.py");

    /// <summary>test_fstring.py — PEP 701 nested f-strings (Python 3.12+).</summary>
    [Fact, Trait("cpython", "critical")]
    public void CPython_FString() => RunCpythonTest("test_fstring.py");

    /// <summary>test_pattern_matching.py — PEP 634 match/case all pattern types.</summary>
    [Fact, Trait("cpython", "critical")]
    public void CPython_PatternMatching() => RunCpythonTest("test_pattern_matching.py");

    /// <summary>test_exception_group.py — PEP 654 except*.</summary>
    [Fact, Trait("cpython", "high")]
    public void CPython_ExceptionGroup() => RunCpythonTest("test_exception_group.py");

    // ── HIGH priority tests ───────────────────────────────────────────────────

    [Fact, Trait("cpython", "high")]
    public void CPython_TypeAnnotations() => RunCpythonTest("test_type_annotations.py");

    [Fact, Trait("cpython", "high")]
    public void CPython_Generators() => RunCpythonTest("test_generators.py");

    [Fact, Trait("cpython", "high")]
    public void CPython_Coroutines() => RunCpythonTest("test_coroutines.py");

    [Fact, Trait("cpython", "high")]
    public void CPython_Typing() => RunCpythonTest("test_typing.py");

    // ── MEDIUM priority (expected to fail initially — tracked as xfail) ───────

    [Fact, Trait("cpython", "medium"), Trait("xfail", "true")]
    public void CPython_Dataclasses_XFail() => RunCpythonTest("test_dataclasses.py", expectedToFail: true);

    [Fact, Trait("cpython", "medium"), Trait("xfail", "true")]
    public void CPython_Asyncio_XFail() => RunCpythonTest("test_asyncio.py", expectedToFail: true);

    [Fact, Trait("cpython", "medium"), Trait("xfail", "true")]
    public void CPython_Contextlib_XFail() => RunCpythonTest("test_contextlib.py", expectedToFail: true);

    [Fact, Trait("cpython", "medium"), Trait("xfail", "true")]
    public void CPython_Itertools_XFail() => RunCpythonTest("test_itertools.py", expectedToFail: true);

    // ── Python 3.14 exclusive features ───────────────────────────────────────

    /// <summary>PEP 750 — Template strings. New in 3.14.</summary>
    [Fact, Trait("cpython", "critical"), Trait("python_version", "3.14")]
    public void CPython_TString_Pep750()
    {
        if (CpythonRoot is null)
        {
            _out.WriteLine("SKIPPED: CPYTHON_REPO not set");
            return;
        }
        // test_t_string.py only exists in Python 3.14+
        var path = Path.Combine(CpythonRoot, "Lib", "test", "test_t_string.py");
        if (!File.Exists(path))
        {
            _out.WriteLine("SKIPPED: test_t_string.py not found — ensure CPython 3.14+ clone");
            return;
        }
        Engine.Eval(path);
    }

    // ── Bulk runner — enumerate all test_*.py and report pass rate ────────────

    /// <summary>
    /// Run ALL test_*.py files in CPython's Lib/test/ and report the overall
    /// pass rate. The strategy requires >95%. This test fails if the rate drops
    /// below the threshold.
    /// </summary>
    [Fact, Trait("cpython", "bulk"), Trait("slow", "true")]
    public void CPython_BulkSuite_PassRate()
    {
        if (CpythonRoot is null)
        {
            _out.WriteLine("SKIPPED: CPYTHON_REPO not set");
            return;
        }

        const double RequiredPassRate = 0.95;
        var testDir = Path.Combine(CpythonRoot, "Lib", "test");
        var allTests = Directory.GetFiles(testDir, "test_*.py");

        int passed = 0, failed = 0, errored = 0;
        var failures = new List<(string File, string Error)>();

        foreach (var testFile in allTests)
        {
            try
            {
                Engine.Eval(testFile);
                passed++;
            }
            catch (CodeGenException ex)
            {
                failed++;
                failures.Add((Path.GetFileName(testFile), ex.Message[..Math.Min(120, ex.Message.Length)]));
            }
            catch (Exception ex)
            {
                errored++;
                failures.Add((Path.GetFileName(testFile), $"[runtime] {ex.GetType().Name}: {ex.Message[..Math.Min(120, ex.Message.Length)]}"));
            }
        }

        int total = passed + failed + errored;
        double rate = total == 0 ? 0 : (double)passed / total;

        _out.WriteLine($"\n{'=',-60}");
        _out.WriteLine($"CPython Suite Results: {passed}/{total} passed ({rate:P1})");
        _out.WriteLine($"Failed: {failed}  Errored: {errored}");
        _out.WriteLine($"{'=',-60}");

        if (failures.Count > 0)
        {
            _out.WriteLine("\nTop failures:");
            foreach (var (file, err) in failures.Take(20))
                _out.WriteLine($"  FAIL {file}: {err}");
        }

        Assert.True(rate >= RequiredPassRate,
            $"CPython suite pass rate {rate:P1} is below required {RequiredPassRate:P0}.\n" +
            $"Passed: {passed}/{total}. See test output for failure list.");
    }
}

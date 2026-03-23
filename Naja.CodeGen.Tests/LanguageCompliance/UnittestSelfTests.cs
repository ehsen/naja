using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Phase 0 — NajaUnittest Self-Tests.
///
/// Validates our NajaTestCase / NajaUnittest implementation by compiling and
/// executing small .py files from testdata/unittest_self/ through NajaEngine.
///
/// These tests must all pass BEFORE running any CPython test suite tests.
/// If any self-test fails it means NajaTestCase has a bug; fix the stdlib
/// implementation rather than the test file.
///
/// Test data: testdata/unittest_self/
/// </summary>
[Collection("SerialConsole")]
public sealed class UnittestSelfTests
{
    private readonly ITestOutputHelper _out;
    private static readonly NajaEngine Engine = new();

    public UnittestSelfTests(ITestOutputHelper output) => _out = output;

    private void Run(string fileName)
    {
        var path = Path.Combine("testdata", "unittest_self", fileName);
        _out.WriteLine($"Running: {path}");
        Engine.Eval(path);
    }

    // ── Phase 0 self-tests ────────────────────────────────────────────────────

    /// <summary>assertEqual, assertNotEqual, assertIs, assertIsNone.</summary>
    [Fact, Trait("phase", "0"), Trait("category", "unittest-self")]
    public void Self_Equality() => Run("test_equality.py");

    /// <summary>assertTrue, assertFalse, assertIn, assertNotIn.</summary>
    [Fact, Trait("phase", "0"), Trait("category", "unittest-self")]
    public void Self_Boolean() => Run("test_boolean.py");

    /// <summary>assertGreater, assertLess, assertAlmostEqual.</summary>
    [Fact, Trait("phase", "0"), Trait("category", "unittest-self")]
    public void Self_Comparison() => Run("test_comparison.py");

    /// <summary>assertRaises (callable form), failureException.</summary>
    [Fact, Trait("phase", "0"), Trait("category", "unittest-self")]
    public void Self_Raises() => Run("test_raises.py");

    /// <summary>skipTest, fail().</summary>
    [Fact, Trait("phase", "0"), Trait("category", "unittest-self")]
    public void Self_SkipAndFail() => Run("test_skip_fail.py");
}

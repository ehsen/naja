using Xunit.Abstractions;
using Naja.CPythonTests.Infrastructure;

namespace Naja.CPythonTests.CompilerCorrectness;

/// <summary>
/// Hard CI gate — CPython test files confirmed to pass end-to-end through Naja.
///
/// A failure here means a working feature regressed. Every test asserts directly.
///
/// To add a new test: verify the file passes locally, then add a [Fact] here
/// and add the filename to <see cref="CpythonFullSuiteTests.KnownPassingFiles"/>.
///
/// CI filter:  dotnet test --filter "Category=CompilerCorrectness&amp;Confirmed=true"
/// </summary>
[Collection("SerialConsole")]
[Trait("Category", "CompilerCorrectness")]
[Trait("Confirmed", "true")]
public sealed class ConfirmedPassingTests : CPythonTestFixture
{
    public ConfirmedPassingTests(ITestOutputHelper output) : base(output) { }

    // ── test_exception_variations.py ─────────────────────────────────────────

    /// <summary>
    /// try/except/else/finally with proper exception chaining.
    /// 30 test methods. Only import: unittest.
    /// </summary>
    [Fact]
    [Trait("StdlibModule", "exceptions")]
    public void CPython_ExceptionVariations_AllPass()
    {
        var result = RunTestFile("test_exception_variations.py");
        Assert.True(result.Passed, result.FailureSummary);
    }

    // ── test_int_literal.py ──────────────────────────────────────────────────

    /// <summary>
    /// Hex/oct/bin integer literals, PEP 237 treatment.
    /// 6 test methods. Only import: unittest.
    /// </summary>
    [Fact]
    [Trait("StdlibModule", "int")]
    public void CPython_IntLiteral_AllPass()
    {
        var result = RunTestFile("test_int_literal.py");
        Assert.True(result.Passed, result.FailureSummary);
    }

    // ── test_generator_stop.py ───────────────────────────────────────────────

    /// <summary>
    /// PEP 479: StopIteration-to-RuntimeError wrapping inside generators.
    /// 2 test methods. Imports: __future__ (generator_stop), unittest.
    /// </summary>
    [Fact]
    [Trait("StdlibModule", "generators")]
    public void CPython_GeneratorStop_AllPass()
    {
        var result = RunTestFile("test_generator_stop.py");
        Assert.True(result.Passed, result.FailureSummary);
    }
}

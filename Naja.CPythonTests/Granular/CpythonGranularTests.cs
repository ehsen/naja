using Xunit.Abstractions;
using Naja.CPythonTests.Infrastructure;

namespace Naja.CPythonTests.Granular;

/// <summary>
/// Per-method granular CPython tests.
///
/// One xUnit Theory row per (className, methodName) pair inside a CPython test
/// file. Useful when a file has many methods and you need to see exactly which
/// ones pass and which fail, rather than the whole file as one row.
///
/// Every row hard-asserts — if a Python test method fails, the xUnit row fails.
///
/// CI filter:  dotnet test --filter "Category=CPythonGranular"
/// </summary>
[Collection("SerialConsole")]
[Trait("Category", "CPythonGranular")]
public sealed class CpythonGranularTests : CPythonTestFixture
{
    public CpythonGranularTests(ITestOutputHelper output) : base(output) { }

    // ── MemberData sources per file ────────────────────────────────────────

    // Add one MemberData property per test file that has sufficient coverage
    // to warrant per-method tracking. Each returns
    //   IEnumerable<object[]> { fileName, className, methodName }

    public static IEnumerable<object[]> ExceptionVariationsMethods
        => CPythonTestDiscovery.GetTestMethods("test_exception_variations.py")
            .Select(row => new object[] { "test_exception_variations.py", row[0], row[1] });

    public static IEnumerable<object[]> GeneratorStopMethods
        => CPythonTestDiscovery.GetTestMethods("test_generator_stop.py")
            .Select(row => new object[] { "test_generator_stop.py", row[0], row[1] });

    public static IEnumerable<object[]> IntLiteralMethods
        => CPythonTestDiscovery.GetTestMethods("test_int_literal.py")
            .Select(row => new object[] { "test_int_literal.py", row[0], row[1] });

    public static IEnumerable<object[]> Utf8SourceMethods
        => CPythonTestDiscovery.GetTestMethods("test_utf8source.py")
            .Select(row => new object[] { "test_utf8source.py", row[0], row[1] });

    public static IEnumerable<object[]> UnaryMethods
        => CPythonTestDiscovery.GetTestMethods("test_unary.py")
            .Select(row => new object[] { "test_unary.py", row[0], row[1] });

    public static IEnumerable<object[]> DecoratorsMethods
        => CPythonTestDiscovery.GetTestMethods("test_decorators.py")
            .Select(row => new object[] { "test_decorators.py", row[0], row[1] });

    public static IEnumerable<object[]> ScopeMethods
        => CPythonTestDiscovery.GetTestMethods("test_scope.py")
            .Select(row => new object[] { "test_scope.py", row[0], row[1] });

    public static IEnumerable<object[]> SuperMethods
        => CPythonTestDiscovery.GetTestMethods("test_super.py")
            .Select(row => new object[] { "test_super.py", row[0], row[1] });

    public static IEnumerable<object[]> CompareMethods
        => CPythonTestDiscovery.GetTestMethods("test_compare.py")
            .Select(row => new object[] { "test_compare.py", row[0], row[1] });

    // ── Theories ──────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ExceptionVariationsMethods))]
    [Trait("File", "test_exception_variations")]
    public void cpython_exception_variations_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    [Theory]
    [MemberData(nameof(GeneratorStopMethods))]
    [Trait("File", "test_generator_stop")]
    public void cpython_generator_stop_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    [Theory]
    [MemberData(nameof(IntLiteralMethods))]
    [Trait("File", "test_int_literal")]
    public void cpython_int_literal_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    [Theory]
    [MemberData(nameof(Utf8SourceMethods))]
    [Trait("File", "test_utf8source")]
    public void cpython_utf8source_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    [Theory]
    [MemberData(nameof(UnaryMethods))]
    [Trait("File", "test_unary")]
    public void cpython_unary_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    [Theory]
    [MemberData(nameof(DecoratorsMethods))]
    [Trait("File", "test_decorators")]
    public void cpython_decorators_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    [Theory]
    [MemberData(nameof(ScopeMethods))]
    [Trait("File", "test_scope")]
    public void cpython_scope_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    [Theory]
    [MemberData(nameof(SuperMethods))]
    [Trait("File", "test_super")]
    public void cpython_super_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    [Theory]
    [MemberData(nameof(CompareMethods))]
    [Trait("File", "test_compare")]
    public void cpython_compare_method(
        string fileName, string className, string methodName)
        => RunGranularMethod(fileName, className, methodName);

    // ── Core runner ────────────────────────────────────────────────────────

    private void RunGranularMethod(string fileName, string className, string methodName)
    {
        var result = RunTestMethod(fileName, className, methodName, timeoutMs: 30_000);
        Assert.True(result.Passed,
            $"{className}.{methodName} in '{fileName}' failed.\n{result.FailureSummary}");
    }
}

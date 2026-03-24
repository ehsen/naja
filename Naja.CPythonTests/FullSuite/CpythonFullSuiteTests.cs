using Xunit.Abstractions;
using Naja.CPythonTests.Infrastructure;

namespace Naja.CPythonTests.FullSuite;

/// <summary>
/// Full CPython test suite — one xUnit Theory row per test_*.py file.
///
/// Every row hard-asserts: if the Python file fails the xUnit row fails.
/// Red = broken. Green = passing. No silent swallowing.
///
/// CI filter:  dotnet test --filter "Category=CPythonFullSuite"
/// </summary>
[Collection("SerialConsole")]
[Trait("Category", "CPythonFullSuite")]
public sealed class CpythonFullSuiteTests : CPythonTestFixture
{
    public CpythonFullSuiteTests(ITestOutputHelper output) : base(output) { }

    /// <summary>
    /// Enumerates every test_*.py filename from the CPython test root, sorted.
    /// </summary>
    public static IEnumerable<object[]> AllCpythonFiles
        => CPythonTestDiscovery.GetAllTestFiles();

    /// <summary>
    /// Compile and run one CPython test file. Fails if Python fails.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCpythonFiles))]
    public void cpython_file(string fileName)
    {
        var result = RunTestFile(fileName, timeoutMs: 60_000);
        Assert.True(result.Passed,
            $"'{fileName}' failed " +
            $"(pass={result.PassCount}, fail={result.FailCount}, err={result.ErrorCount}).\n" +
            result.FailureSummary);
    }
}

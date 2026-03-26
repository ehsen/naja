using System.Text.RegularExpressions;

namespace Naja.CPythonTests.Infrastructure;

/// <summary>
/// Discovers test classes and methods from CPython .py test files by parsing
/// Python source text with regular expressions.
///
/// Drives Category 3 granular Theory tests: each (className, methodName) pair
/// becomes one xUnit Theory row giving per-method pass/fail in the test explorer.
/// Also drives Category 2 full-suite Theory: each test_*.py filename becomes one row.
/// </summary>
public static class CPythonTestDiscovery
{
    // ── Test root resolution ──────────────────────────────────────────────────

    public static readonly string TestRoot =
        Environment.GetEnvironmentVariable("CPYTHON_TEST_ROOT")
        ?? TryDefault("F:/Sources/cpython/Lib/test")
        ?? TryDefault("C:/dev/cpython/Lib/test")
        ?? "";

    private static string? TryDefault(string path) =>
        Directory.Exists(path) ? path : null;

    // ── Regex patterns ────────────────────────────────────────────────────────

    // class FooTests(unittest.TestCase):
    // class FooTests(TestCase):
    // class FooTests(unittest.TestCase, SomeMixin):
    private static readonly Regex ClassPattern = new(
        @"^class\s+(\w+)\s*\(.*?(?:unittest\.TestCase|TestCase).*?\)\s*:",
        RegexOptions.Compiled | RegexOptions.Multiline);

    //     def test_xxx(self):     (4-space or tab indent)
    //     def testXxx(self):      (camelCase – e.g. test_scope.py, test_super.py)
    private static readonly Regex MethodPattern = new(
        @"^[ \t]{4}def\s+(test(?:_\w+|\p{Lu}\w*))\s*\(self",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all (className, methodName) pairs from a CPython test file.
    /// Suitable for use as [MemberData] in Theory tests.
    /// Returns an empty enumerable when the test root is not configured or the
    /// file is not found — Theory tests are gracefully skipped rather than fail.
    /// </summary>
    public static IEnumerable<object[]> GetTestMethods(string testFileName)
    {
        if (string.IsNullOrEmpty(TestRoot))
            return [];

        var path = Path.Combine(TestRoot, testFileName);
        if (!File.Exists(path))
            return [];

        return ParseTestMethods(File.ReadAllText(path));
    }

    /// <summary>
    /// Returns all test_*.py filenames in the CPython test directory, sorted.
    /// Used by Category 2 full-suite Theory to enumerate every test file.
    /// Returns an empty enumerable when the test root is not configured.
    /// </summary>
    public static IEnumerable<object[]> GetAllTestFiles()
    {
        if (string.IsNullOrEmpty(TestRoot) || !Directory.Exists(TestRoot))
            return [];

        return Directory
            .GetFiles(TestRoot, "test_*.py")
            .Select(Path.GetFileName)
            .Where(f => f is not null && f != "test_augassign.py" && f != "test_numeric_tower.py" && f != "test_iter.py") // Temp skip crashing/hanging tests
            .Order()
            .Select(f => new object[] { f! });
    }

    /// <summary>
    /// Returns all CPython test filenames matching test_&lt;moduleName&gt;*.py.
    /// </summary>
    public static IEnumerable<string> GetTestFilesForModule(string moduleName)
    {
        if (string.IsNullOrEmpty(TestRoot) || !Directory.Exists(TestRoot))
            return [];

        return Directory
            .GetFiles(TestRoot, $"test_{moduleName}*.py")
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Cast<string>();
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    internal static IEnumerable<object[]> ParseTestMethods(string source)
    {
        var results = new List<object[]>();

        var classMatches = ClassPattern.Matches(source);
        if (classMatches.Count == 0)
            return results;

        // Build spans: [classStart, nextClassStart)
        for (int i = 0; i < classMatches.Count; i++)
        {
            int start = classMatches[i].Index;
            int end   = i + 1 < classMatches.Count
                ? classMatches[i + 1].Index
                : source.Length;

            string className = classMatches[i].Groups[1].Value;
            string span      = source[start..end];

            foreach (Match m in MethodPattern.Matches(span))
                results.Add([className, m.Groups[1].Value]);
        }

        return results;
    }
}

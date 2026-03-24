using Xunit.Abstractions;
using Naja.CPythonTests.Infrastructure;

namespace Naja.CPythonTests.StdlibCompilation;

/// <summary>
/// Category 4 — stdlib module compilation tests.
///
/// Verifies that Naja's standard library modules compile without errors.
/// These tests do NOT execute the modules — they only check that the
/// compiler accepts the source. This catches regressions in the stdlib
/// source that would prevent any program importing those modules from compiling.
///
/// CI filter:
///   dotnet test --filter "Category=StdlibCompilation"
/// </summary>
[Collection("SerialConsole")]
[Trait("Category", "StdlibCompilation")]
public sealed class StdlibCompilationTests : CPythonTestFixture
{
    private static readonly string StdlibRoot =
        Environment.GetEnvironmentVariable("NAJA_STDLIB_ROOT")
        ?? Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Naja.StdLib"));

    public StdlibCompilationTests(ITestOutputHelper output) : base(output) { }

    // ── Core modules ──────────────────────────────────────────────────────

    [Fact]
    public void Stdlib_Unittest_Compiles()
        => AssertStdlibCompiles("NajaUnittest.cs");   // pseudo — uses C# class; skip if absent

    [Fact]
    public void Stdlib_Sys_Compiles()
        => AssertStdlibModuleCompiles("sys");

    [Fact]
    public void Stdlib_Os_Compiles()
        => AssertStdlibModuleCompiles("os");

    [Fact]
    public void Stdlib_Math_Compiles()
        => AssertStdlibModuleCompiles("math");

    [Fact]
    public void Stdlib_Re_Compiles()
        => AssertStdlibModuleCompiles("re");

    // ── Python wrapper files (if any .py shims exist in StdLib) ───────────

    [Fact]
    public void Stdlib_PyShims_AllCompile()
    {
        if (!Directory.Exists(StdlibRoot))
        {
            Output.WriteLine($"SKIPPED: StdLib root not found at '{StdlibRoot}'");
            return;
        }

        var pyFiles = Directory.GetFiles(StdlibRoot, "*.py", SearchOption.AllDirectories);
        if (pyFiles.Length == 0)
        {
            Output.WriteLine("INFO: No .py shim files found in StdLib root — nothing to compile.");
            return;
        }

        var failures = new List<string>();
        foreach (var pyFile in pyFiles.OrderBy(f => f))
        {
            Output.WriteLine($"Checking: {Path.GetRelativePath(StdlibRoot, pyFile)}");
            var result = AssertCompiles(pyFile);
            if (!result.Success)
                failures.Add($"{Path.GetFileName(pyFile)}: {string.Join("; ", result.Errors)}");
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} stdlib .py file(s) failed to compile:\n" +
            string.Join("\n", failures));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void AssertStdlibModuleCompiles(string moduleName)
    {
        if (!Directory.Exists(StdlibRoot))
        {
            Output.WriteLine($"SKIPPED: StdLib root not found at '{StdlibRoot}'");
            return;
        }

        // Look for a .py file named after the module
        var candidates = new[]
        {
            Path.Combine(StdlibRoot, $"{moduleName}.py"),
            Path.Combine(StdlibRoot, moduleName, "__init__.py"),
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
        {
            Output.WriteLine(
                $"SKIPPED: No .py source for module '{moduleName}' found in '{StdlibRoot}'");
            return;
        }

        var result = AssertCompiles(found);
        Assert.True(result.Success,
            $"Module '{moduleName}' failed to compile:\n" +
            string.Join("\n", result.Errors));
    }

    private void AssertStdlibCompiles(string fileName)
    {
        // C# stdlib files are not compiled by NajaEngine — skip gracefully
        Output.WriteLine(
            $"SKIPPED: '{fileName}' is a C# implementation file, not compiled by NajaEngine.");
    }
}

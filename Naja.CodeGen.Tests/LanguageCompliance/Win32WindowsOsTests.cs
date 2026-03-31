using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Windows OS Tests — replica of CPython test_os.py Windows test classes.
///
/// Compiles and runs testdata/windows_os/test_windows.py through NajaEngine.
/// Skips silently on non-Windows platforms (Linux, macOS CI agents).
///
/// Test classes and coverage:
///   Win32ListdirTests    (5) – os.listdir with normal and extended \\?\ paths
///   Win32ListdriveTests  (3) – os.listdrives / os.listvolumes / os.listmounts
///   Win32SymlinkTests    (4) – os.symlink / os.readlink / os.lstat / os.path.islink
///   Win32JunctionTests   (2) – _winapi.CreateJunction / os.readlink
///   Win32NtTests         (3) – nt module functions (stat, lstat, etc.)
///   Win32KillTests       (2) – os.kill (basic tests, ctypes-heavy tests skipped)
///
/// Total: 19 tests (some skipped on non-Windows or low-privilege systems)
///
/// Test data file: testdata/windows_os/test_windows.py
/// 
/// Note: This test runner will skip entirely on non-Windows platforms.
/// On Windows, some individual tests may skip if required privileges are missing
/// (e.g., symlink creation requires admin or developer mode on Windows 10+).
/// </summary>
[Collection("SerialConsole")]
public sealed class Win32WindowsOsTests
{
    private readonly ITestOutputHelper _output;
    private static readonly NajaEngine Engine = new();
    private const string TestFile = "testdata/windows_os/test_windows.py";

    public Win32WindowsOsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Runs the complete Windows OS test suite.
    /// Skips on non-Windows platforms (no error, just skipped).
    /// On Windows, executes all 6 test classes and reports results.
    /// </summary>
    [Fact(DisplayName = "Windows OS Tests"), 
     Trait("category", "os-windows"), 
     Trait("phase", "win32")]
    public void RunWindowsOsTests()
    {
        if (!OperatingSystem.IsWindows())
        {
            _output.WriteLine("SKIPPED: Windows-only tests (not running on Windows)");
            return;
        }

        _output.WriteLine($"Running: {TestFile}");
        _output.WriteLine("Test classes: 6");
        _output.WriteLine("  - Win32ListdirTests (5 tests)");
        _output.WriteLine("  - Win32ListdriveTests (3 tests)");
        _output.WriteLine("  - Win32SymlinkTests (4 tests)");
        _output.WriteLine("  - Win32JunctionTests (2 tests)");
        _output.WriteLine("  - Win32NtTests (3 tests)");
        _output.WriteLine("  - Win32KillTests (2 tests)");
        _output.WriteLine("Total tests: 19");
        _output.WriteLine("");
        _output.WriteLine("Note: Some tests may skip individually if requirements aren't met:");
        _output.WriteLine("  - Symlink tests skip without admin/developer mode privilege");
        _output.WriteLine("  - Junction tests skip if _winapi module unavailable");
        _output.WriteLine("  - External tool tests skip if fsutil.exe unavailable");
        _output.WriteLine("");

        try
        {
            Engine.Eval(TestFile);
            _output.WriteLine("");
            _output.WriteLine("✓ All Windows OS tests passed (or appropriately skipped)");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Test execution failed: {ex.Message}");
            _output.WriteLine("");
            _output.WriteLine("Stack trace:");
            _output.WriteLine(ex.StackTrace ?? "");
            throw;
        }
    }
}

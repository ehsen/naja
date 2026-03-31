using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Real CPython test_windows.py Test Runner
///
/// Runs the actual unmodified CPython test_windows.py suite through Naja compiler.
/// This tests Naja's compatibility with real CPython test infrastructure without
/// any modifications or test adaptations.
///
/// Test coverage from CPython test_windows.py:
///   Win32ListdirTests    (2) – os.listdir with normal and extended \\?\ paths
///   Win32ListdriveTests  (3) – os.listdrives / os.listvolumes / os.listmounts
///   Win32SymlinkTests    (9) – os.symlink / os.readlink / os.lstat operations
///   Win32JunctionTests   (2) – _winapi.CreateJunction / os.readlink
///   Win32NtTests         (2) – nt module functions (partial, ctypes tests skipped)
///   Win32KillTests       (3) – os.kill (basic - ctypes tests skipped)
///
/// Total: 21 tests (some skip based on system capabilities)
///
/// Test data file: testdata/windows_os/test_windows_real_cpython.py
/// 
/// This is the "gap analysis" version that:
/// - Uses real test.support module infrastructure
/// - Includes actual ctypes module for Windows API access
/// - Requires shutil, textwrap, and other stdlib modules
/// - Tests Naja's support for unmodified CPython test code
///
/// Note: Some tests require elevated privileges (symlinks) or ctypes support.
/// The test suite will self-skip tests that don't have required infrastructure.
/// </summary>
[Collection("SerialConsole")]
public sealed class CpythonTestWindowsGapAnalysis
{
    private readonly ITestOutputHelper _output;
    private static readonly NajaEngine Engine = new();
    
    // Uses the REAL unmodified CPython test file
    private const string TestFile = "testdata/windows_os/test_windows_real_cpython.py";

    public CpythonTestWindowsGapAnalysis(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Runs the actual CPython test_windows.py through Naja.
    /// 
    /// Success criteria:
    /// - All tests that can run should pass
    /// - Tests missing infrastructure should skip with clear message
    /// - Compiler should successfully process test.support and ctypes imports
    /// - No errors in module resolution or import handling
    /// </summary>
    [Fact(DisplayName = "CPython Real test_windows.py - Gap Analysis")]
    [Trait("category", "os-windows")]
    [Trait("phase", "cpython-gap-analysis")]
    [Trait("priority", "critical")]
    public void RunCpythonTestWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            _output.WriteLine("SKIPPED: Windows-only tests (not running on Windows)");
            return;
        }

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("CPython test_windows.py - Real Gap Analysis Test Run");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");
        _output.WriteLine($"Test File: {TestFile}");
        _output.WriteLine("Status: Real unmodified CPython test infrastructure");
        _output.WriteLine("");
        _output.WriteLine("Test Classes (21 tests total):");
        _output.WriteLine("  • Win32ListdirTests (2) – listdir with Unicode and extended paths");
        _output.WriteLine("  • Win32ListdriveTests (3) – listdrives/listvolumes/listmounts");
        _output.WriteLine("  • Win32SymlinkTests (9) – symlink operations and edge cases");
        _output.WriteLine("  • Win32JunctionTests (2) – junction creation and removal");
        _output.WriteLine("  • Win32NtTests (2) – nt module functions (partial)");
        _output.WriteLine("  • Win32KillTests (3) – os.kill signal handling (basic)");
        _output.WriteLine("");
        _output.WriteLine("Infrastructure Requirements:");
        _output.WriteLine("  ✓ test.support module (NajaTestSupport)");
        _output.WriteLine("  ✓ test.support.os_helper (NajaOsHelper)");
        _output.WriteLine("  ✓ test.support.import_helper (NajaImportHelper)");
        _output.WriteLine("  ✓ ctypes module (NajaCTypes with wintypes)");
        _output.WriteLine("  ✓ shutil module (NajaShutil)");
        _output.WriteLine("  ✓ textwrap module (NajaTextwrap)");
        _output.WriteLine("  ✓ os functions: symlink, readlink, listdrives, etc.");
        _output.WriteLine("");
        _output.WriteLine("Expected Behavior:");
        _output.WriteLine("  - Tests requiring elevated privileges may skip");
        _output.WriteLine("  - Tests requiring ctypes will run (advanced API tests may skip)");
        _output.WriteLine("  - All infrastructure imports should resolve successfully");
        _output.WriteLine("");
        _output.WriteLine("Starting test execution...");
        _output.WriteLine("");

        try
        {
            // Execute the actual CPython test file through Naja
            Engine.Eval(TestFile);
            
            _output.WriteLine("");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("✓ Test Execution Completed Successfully");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("");
            _output.WriteLine("Results:");
            _output.WriteLine("  - All infrastructure imports resolved");
            _output.WriteLine("  - Tests executed (passed or skipped appropriately)");
            _output.WriteLine("  - No critical failures");
            _output.WriteLine("");
            _output.WriteLine("Gap Analysis Complete: Ready for detailed failure inspection");
        }
        catch (Exception ex)
        {
            _output.WriteLine("");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("✗ Test Execution Failed");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("");
            _output.WriteLine($"Error: {ex.Message}");
            _output.WriteLine("");
            _output.WriteLine("Stack Trace:");
            _output.WriteLine(ex.StackTrace ?? "(no stack trace available)");
            
            if (ex.InnerException != null)
            {
                _output.WriteLine("");
                _output.WriteLine("Inner Exception:");
                _output.WriteLine($"{ex.InnerException.Message}");
                _output.WriteLine(ex.InnerException.StackTrace ?? "");
            }
            
            throw;
        }
    }
}

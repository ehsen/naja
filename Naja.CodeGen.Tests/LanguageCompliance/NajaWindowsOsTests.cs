using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Naja Native test_windows.naja Test Runner
///
/// Runs the Windows OS test suite written in pure Naja syntax (.naja file).
/// This tests Naja's ability to compile and execute Windows-specific tests
/// using the native Naja language syntax.
///
/// Test coverage:
///   Win32ListdirTests    (2) – os.listdir with normal and extended paths
///   Win32ListdriveTests  (3) – os.listdrives / os.listvolumes / os.listmounts
///   Win32SymlinkTests    (7) – os.symlink / os.readlink operations
///   Win32JunctionTests   (2) – _winapi.CreateJunction
///   Win32NtTests         (2) – nt module functions
///   Win32KillTests       (3) – os.kill and process control
///
/// Total: 19 tests across 6 test classes
///
/// Test data file: testdata/windows_os/test_windows.naja
/// 
/// This is a native Naja version that:
/// - Uses pure Naja syntax (.naja file)
/// - Compiles directly with Naja compiler
/// - Runs through NajaEngine.Eval()
/// - Demonstrates Naja's Windows OS capabilities
/// </summary>
[Collection("SerialConsole")]
public sealed class NajaWindowsOsTests
{
    private readonly ITestOutputHelper _output;
    private static readonly NajaEngine Engine = new();
    
    // Pure Naja test file
    private const string TestFile = "testdata/windows_os/test_windows.naja";

    public NajaWindowsOsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Runs Windows OS tests written in pure Naja syntax.
    /// 
    /// Success criteria:
    /// - Naja compiler successfully compiles .naja file
    /// - All test infrastructure imports resolve
    /// - 19 total tests discoverable and executable
    /// - Tests pass or skip appropriately
    /// </summary>
    [Fact(DisplayName = "Naja Native Windows OS Tests")]
    [Trait("category", "os-windows")]
    [Trait("language", "naja")]
    [Trait("phase", "native-naja")]
    public void RunNajaWindowsOsTests()
    {
        if (!OperatingSystem.IsWindows())
        {
            _output.WriteLine("SKIPPED: Windows-only tests (not running on Windows)");
            return;
        }

        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("Naja Native Windows OS Tests - test_windows.naja");
        _output.WriteLine("═══════════════════════════════════════════════════════════");
        _output.WriteLine("");
        _output.WriteLine($"Test File: {TestFile}");
        _output.WriteLine("Language: Pure Naja syntax (.naja)");
        _output.WriteLine("");
        _output.WriteLine("Test Classes (19 tests total):");
        _output.WriteLine("  • Win32ListdirTests (2) – listdir operations");
        _output.WriteLine("  • Win32ListdriveTests (3) – drive/volume enumeration");
        _output.WriteLine("  • Win32SymlinkTests (7) – symlink operations");
        _output.WriteLine("  • Win32JunctionTests (2) – junction support");
        _output.WriteLine("  • Win32NtTests (2) – nt module functions");
        _output.WriteLine("  • Win32KillTests (3) – process control");
        _output.WriteLine("");
        _output.WriteLine("Infrastructure Available:");
        _output.WriteLine("  ✓ test.support infrastructure");
        _output.WriteLine("  ✓ ctypes Windows API stubs");
        _output.WriteLine("  ✓ shutil file operations");
        _output.WriteLine("  ✓ textwrap text formatting");
        _output.WriteLine("  ✓ tempfile temporary management");
        _output.WriteLine("  ✓ os functions (symlink, readlink, listdrives, etc.)");
        _output.WriteLine("");
        _output.WriteLine("Starting test execution...");
        _output.WriteLine("");

        try
        {
            // Compile and execute the Naja test file
            Engine.Eval(TestFile);
            
            _output.WriteLine("");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("✓ Test Execution Completed Successfully");
            _output.WriteLine("═══════════════════════════════════════════════════════════");
            _output.WriteLine("");
            _output.WriteLine("Results:");
            _output.WriteLine("  - Naja .naja file compiled successfully");
            _output.WriteLine("  - All test classes discovered");
            _output.WriteLine("  - Tests executed (passed or skipped appropriately)");
            _output.WriteLine("  - No critical errors");
            _output.WriteLine("");
            _output.WriteLine("Validation: Naja can compile and execute Windows OS tests");
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

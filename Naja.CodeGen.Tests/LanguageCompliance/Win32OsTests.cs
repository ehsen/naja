using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Windows OS Tests — replica of CPython test_os.py Win32 test classes.
///
/// Compiles and runs testdata/os_windows/test_os_windows.py through NajaEngine.
/// Skips silently on non-Windows platforms (Linux, macOS CI agents).
///
/// ACTIVE (test our implementations):
///   Win32ListdirTests   – os.listdir with normal and extended \\?\ paths
///   Win32FileOpsTests   – mkdir/makedirs/rmdir/remove/unlink/rename/chdir
///   Win32StatTests      – os.stat st_size and st_mtime
///   Win32EnvTests       – os.environ, os.getenv, os.putenv
///   Win32PathTests      – os.path with Windows-style paths
///
/// DEFERRED (setUp calls self.skipTest — documented, not yet executable):
///   Win32ListdriveTests – os.listdrives/listvolumes/listmounts not yet impl
///   Win32SymlinkTests   – os.symlink/readlink/lstat/islink not yet impl
///   Win32JunctionTests  – _winapi.CreateJunction not yet impl
///
/// Test data: testdata/os_windows/test_os_windows.py
/// </summary>
[Collection("SerialConsole")]
public sealed class Win32OsTests
{
    private readonly ITestOutputHelper _out;
    private static readonly NajaEngine Engine = new();
    private const string TestFile = "testdata/os_windows/test_os_windows.py";

    public Win32OsTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Runs the full Windows OS test suite.
    /// Active test classes exercise the Naja os stdlib.
    /// Deferred test classes self-skip via setUp→skipTest.
    /// </summary>
    [Fact, Trait("category", "os-windows"), Trait("phase", "win32")]
    public void Win32_OsTests()
    {
        if (!OperatingSystem.IsWindows())
        {
            _out.WriteLine("SKIPPED: Windows-only tests.");
            return;
        }

        _out.WriteLine($"Running: {TestFile}");
        Engine.Eval(TestFile);
    }
}

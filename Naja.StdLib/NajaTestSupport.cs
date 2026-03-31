using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python test.support module - testing infrastructure and utilities.
/// Provides common support functionality for unit tests including:
/// - TESTFN: managed temporary directory
/// - skip decorators and conditions
/// - subprocess support detection
/// - test timing and retry utilities
/// - verbose output control
/// </summary>
public class NajaTestSupport
{
    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    // ── Test Configuration ─────────────────────────────────────────────────────

    /// <summary>
    /// Global TESTFN - a managed temporary directory path for test files.
    /// Created once per test session and cleaned up after.
    /// </summary>
    private static string? _testfn = null;

    public static string TESTFN
    {
        get
        {
            if (_testfn == null)
            {
                _testfn = Path.Combine(Path.GetTempPath(), 
                    $"naja_test_{Process.GetCurrentProcess().Id}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(_testfn);
            }
            return _testfn;
        }
    }

    /// <summary>
    /// Global verbose flag - controls test output verbosity.
    /// Can be set via VERBOSE environment variable.
    /// </summary>
    public static bool verbose
    {
        get => Environment.GetEnvironmentVariable("VERBOSE") == "1" ||
               Environment.GetEnvironmentVariable("VERBOSE") == "true";
    }

    /// <summary>
    /// SHORT_TIMEOUT - standard short timeout for subprocess tests (seconds).
    /// </summary>
    public static double SHORT_TIMEOUT => 2.0;

    /// <summary>
    /// Check if subprocess module is available and functional.
    /// </summary>
    public static bool has_subprocess => CheckSubprocessAvailable();

    private static bool CheckSubprocessAvailable()
    {
        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows) ? "cmd" : "sh",
                Arguments = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows) ? "/c exit 0" : "-c exit 0",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            proc?.WaitForExit();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Test Utilities ─────────────────────────────────────────────────────────

    /// <summary>
    /// Retry a function with exponential backoff.
    /// Mirrors test.support.sleeping_retry().
    /// </summary>
    public Func<T> sleeping_retry<T>(Func<T> func, object? tries = null, object? timeout = null)
    {
        int maxTries = tries is int i ? i : 5;
        double waitTime = timeout is double d ? d : 0.1;

        return () =>
        {
            Exception? lastException = null;
            for (int attempt = 0; attempt < maxTries; attempt++)
            {
                try
                {
                    return func();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < maxTries - 1)
                    {
                        System.Threading.Thread.Sleep((int)(waitTime * 1000));
                        waitTime *= 2; // exponential backoff
                    }
                }
            }

            if (lastException != null)
                throw lastException;
            throw new InvalidOperationException("sleeping_retry exhausted");
        };
    }

    /// <summary>
    /// Create a temporary filename (path doesn't necessarily exist).
    /// </summary>
    public static string make_filename(object? name = null)
    {
        var filename = name is not null ? S(name) : $"temp_{Guid.NewGuid():N}";
        return Path.Combine(TESTFN, filename);
    }

    /// <summary>
    /// Clean up temporary test files created in TESTFN.
    /// </summary>
    public static void cleanup_testfn()
    {
        if (_testfn != null && Directory.Exists(_testfn))
        {
            try
            {
                Directory.Delete(_testfn, recursive: true);
            }
            catch { }
            _testfn = null;
        }
    }

    /// <summary>
    /// Requires subprocess module to be available.
    /// Can be used as a test decorator condition.
    /// </summary>
    public static bool requires_subprocess()
    {
        return has_subprocess;
    }

    /// <summary>
    /// Check if a particular module is available.
    /// </summary>
    public static bool module_available(object module_name)
    {
        var name = S(module_name);
        // Simple check - in real implementation would try to import
        return !string.IsNullOrEmpty(name);
    }

    /// <summary>
    /// Get platform-specific path representation.
    /// </summary>
    public static string get_platform()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows))
            return "win32";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Linux))
            return "linux";
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.OSX))
            return "darwin";
        return "unknown";
    }
}

/// <summary>
/// test.support.os_helper - OS-specific test utilities.
/// Provides managed temporary paths and OS capability checks.
/// </summary>
public class NajaOsHelper
{
    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    /// <summary>
    /// TESTFN - alias for test.support.TESTFN managed temp directory.
    /// </summary>
    public static string TESTFN => NajaTestSupport.TESTFN;

    /// <summary>
    /// Remove directory tree recursively.
    /// Mirrors os_helper rmtree.
    /// </summary>
    public static void rmtree(object path)
    {
        var dir = S(path);
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Check if symbolic links are supported on this platform.
    /// On Windows, this requires admin or developer mode.
    /// </summary>
    public static bool can_symlink()
    {
        try
        {
            var testPath = Path.Combine(TESTFN, $"symlink_test_{Guid.NewGuid():N}");
            var testTarget = Path.Combine(TESTFN, $"symlink_target_{Guid.NewGuid():N}");
            
            Directory.CreateDirectory(testTarget);
            try
            {
                File.CreateSymbolicLink(testPath, testTarget);
                File.Delete(testPath);
                return true;
            }
            finally
            {
                try { Directory.Delete(testTarget); } catch { }
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Skip condition: True if symlinks are supported.
    /// Can be used to gate symlink-dependent tests.
    /// </summary>
    public static object skip_unless_symlink(object? message = null)
    {
        if (!can_symlink())
        {
            var msg = message is not null ? S(message) : "Symlinks not supported on this platform";
            throw new InvalidOperationException($"SKIP: {msg}");
        }
        return true;
    }

    /// <summary>
    /// Get the current platform name.
    /// </summary>
    public static string get_platform()
    {
        return NajaTestSupport.get_platform();
    }

    /// <summary>
    /// Check if running on Windows.
    /// </summary>
    public static bool is_windows()
    {
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);
    }

    /// <summary>
    /// Check if running on Linux.
    /// </summary>
    public static bool is_linux()
    {
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Linux);
    }

    /// <summary>
    /// Check if running on macOS.
    /// </summary>
    public static bool is_macos()
    {
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.OSX);
    }
}

/// <summary>
/// test.support.import_helper - module import utilities for tests.
/// Provides utilities for dynamically importing and checking modules.
/// </summary>
public class NajaImportHelper
{
    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    /// <summary>
    /// Try to import a module by name, returning None if unavailable.
    /// </summary>
    public static object? import_module(object module_name)
    {
        var name = S(module_name);
        try
        {
            var type = Type.GetType($"Naja.StdLib.Naja{name.Substring(0, 1).ToUpper()}{name.Substring(1)}", 
                throwOnError: false);
            return type != null ? (object)name : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if a module can be imported.
    /// </summary>
    public static bool can_import(object module_name)
    {
        return import_module(module_name) != null;
    }

    /// <summary>
    /// Skip test if a module is not available.
    /// </summary>
    public static object requires_module(object module_name, object? message = null)
    {
        var name = S(module_name);
        if (!can_import(module_name))
        {
            var msg = message is not null ? S(message) : $"Module {name} not available";
            throw new InvalidOperationException($"SKIP: {msg}");
        }
        return true;
    }
}

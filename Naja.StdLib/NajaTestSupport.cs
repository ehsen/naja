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

    // ── CPython test.support members required by the parity suite ─────────────

    /// <summary>
    /// test.support.cpython_only — decorator that marks a test as CPython-specific.
    /// Naja targets CPython parity, so run the test rather than skip: pass-through.
    /// </summary>
    public static object cpython_only(object func) => func;

    /// <summary>
    /// test.support.gc_collect — force a full garbage-collection cycle.
    /// CPython calls this after deleting cyclic garbage in tests (e.g.
    /// test_scope.testFreeingCell); refcount-only backends treat it as a no-op,
    /// but run the full GC so finalizer-based cleanup happens on .NET too.
    /// </summary>
    public static void gc_collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// test.support.check_syntax_error(testcase, statement, errtext='', *,
    /// lineno=None, offset=None) — CPython body at support/__init__.py:826:
    /// with testcase.assertRaisesRegex(SyntaxError, errtext) as cm:
    ///     compile(statement, '<test string>', 'exec')
    /// err = cm.exception
    /// testcase.assertIsNotNone(err.lineno)
    /// if lineno is not None: testcase.assertEqual(err.lineno, lineno)
    /// testcase.assertIsNotNone(err.offset)
    /// if offset is not None: testcase.assertEqual(err.offset, offset)
    ///
    /// Naja.StdLib must NOT reference Naja.CodeGen (§ circular-ref rule), so
    /// TypeSystem.Compile is reached via raw reflection (InvokeDecorated
    /// pattern). Asserts run directly on the NajaTestCase — same assembly.
    /// </summary>
    public static void check_syntax_error(object testcase, object statement,
                                         object? errtext = null,
                                         object? lineno = null, object? offset = null)
    {
        if (testcase is not NajaTestCase tc)
            throw new Exception(
                $"TypeError: check_syntax_error expects a unittest.TestCase, got {testcase?.GetType().Name ?? "None"}");

        Exception? err = null;
        try
        {
            ResolveCompileMethod().Invoke(null,
                new object?[] { new object[] { statement, "<test string>", "exec" } });
        }
        catch (System.Reflection.TargetInvocationException tie)
            when (tie.InnerException is Exception inner)
        {
            err = inner;
        }

        if (err is null)
            tc.fail("SyntaxError not raised");

        // The raised exception must be a SyntaxError — name-based check on the
        // inheritance chain since the CLR type lives in Naja.CodeGen.
        if (!IsSyntaxErrorType(err!))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(err).Throw();

        var expected = errtext is null ? "" : S(errtext);
        if (expected.Length > 0 &&
            !System.Text.RegularExpressions.Regex.IsMatch(err!.Message, expected))
            tc.fail($"'{err.Message}' does not match '{expected}'");

        var errType = err.GetType();
        var errLineno = errType.GetProperty("lineno")?.GetValue(err);
        var errOffset = errType.GetProperty("offset")?.GetValue(err);

        tc.assertIsNotNone(errLineno, "SyntaxError.lineno should not be None");
        if (lineno is not null) tc.assertEqual(errLineno, lineno);
        tc.assertIsNotNone(errOffset, "SyntaxError.offset should not be None");
        if (offset is not null) tc.assertEqual(errOffset, offset);
    }

    /// <summary>True when the exception is Naja's SyntaxError mapping
    /// (SyntaxErrorException or the IndentationError subclass).</summary>
    private static bool IsSyntaxErrorType(Exception err)
    {
        for (var t = (System.Type?)err.GetType(); t is not null; t = t.BaseType)
            if (t.Name is "SyntaxErrorException" or "IndentationErrorException")
                return true;
        return false;
    }

    private static System.Reflection.MethodInfo? _compileMethod;

    /// <summary>
    /// Raw-reflection lookup of Naja.CodeGen.Builtins.TypeSystem.Compile(object[])
    /// (cached). Keeps Naja.StdLib free of a Naja.CodeGen project reference.
    /// </summary>
    private static System.Reflection.MethodInfo ResolveCompileMethod()
    {
        if (_compileMethod is not null) return _compileMethod;

        var typeSystem = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Naja.CodeGen.Builtins.TypeSystem", throwOnError: false))
            .FirstOrDefault(t => t is not null)
            ?? throw new Exception(
                "ImportError: Naja.CodeGen is not loaded — compile() unavailable");

        _compileMethod = typeSystem.GetMethod("Compile", new[] { typeof(object[]) })
            ?? throw new Exception("AttributeError: TypeSystem.Compile not found");
        return _compileMethod;
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

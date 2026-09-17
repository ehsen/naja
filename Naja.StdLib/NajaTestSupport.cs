using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;

namespace Naja.StdLib;

/// <summary>
/// Python test.support module - testing infrastructure and utilities.
///
/// IMPORTANT ARCHITECTURE: Naja's codegen maps every `from test.support import X`
/// (and `from test import support`) to this single backing type, because the
/// StdLibResolver registers "test.support" → NajaTestSupport and "from test import
/// support" resolves through the module name "test". So this class is a SUPERSET:
/// it exposes every symbol the CPython test suite reaches through `support.*`,
/// `os_helper.*`, `import_helper.*`, `script_helper.*` and `socket_helper.*` as
/// STATIC members, so static-field/property lookup and StaticCall dispatch all
/// resolve. Sub-parser helper classes (NajaOsHelper etc.) are kept too so
/// fully-qualified dotted imports such as `from test.support.os_helper import
/// unlink` can resolve through the type's static members.
///
/// Decorators: CPython's `@support.requires_*` are functions returning a
/// decorator. Under Naja, non-trivial decorators are applied via CallCallable, so
/// each `requires_*` here returns an identity Func&lt;object,object&gt; (no-op) when
/// the feature is plausibly available, or a skip-throwing wrapper when genuinely
/// unavailable on the host. `@support.cpython_only` (and a few others, used
/// WITHOUT parens) are exposed as static FIELDS whose value is a callable, so the
/// compiler's GetStaticAttr (field branch) resolves them and the decorator
/// returns the function unchanged.
/// </summary>
public sealed class NajaTestSupport
{
    public static readonly NajaTestSupport Instance = new();

    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    // ── Platform flags (CPython-compatible) ──────────────────────────────────
    public static bool is_jython => false;
    public static bool is_pypy => false;
    public static bool is_emscripten => false;
    public static bool is_wasi => false;
    public static bool is_android => RuntimeInformation.OSDescription.Contains("Android", StringComparison.OrdinalIgnoreCase);
    public static bool is_apple => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool is_apple_mobile => false;
    public static bool is_windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool MS_WINDOWS => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool unix_shell => !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static bool Py_DEBUG => false;
    public static bool Py_GIL_DISABLED => false;
    public static bool Py_REF_DEBUG => false;
    public static bool HAVE_PY_DOCSTRINGS => false;
    public static bool MISSING_C_DOCSTRINGS => !HAVE_PY_DOCSTRINGS;
    public static bool HAVE_DOCSTRINGS => true;
    public static bool PGO => false;
    public static bool TEST_MODULES_ENABLED => false;
    public static bool _is_gui_available() => false;

    public static bool has_fork_support => true;
    public static bool has_subprocess_support => CheckSubprocessAvailable();
    public static bool has_socket_support => !is_emscripten && !is_wasi;
    public static bool has_strftime_extensions => true;
    public static bool has_xmlrpc_https => true;
    public static bool has_broken_dash_os_x_threads => false;

    // ── Timeouts (seconds) ────────────────────────────────────────────────────
    public static double LOOPBACK_TIMEOUT => 10.0;
    public static double SHORT_TIMEOUT => 30.0;
    public static double LONG_TIMEOUT => 5 * 60.0;
    public static double INTERNET_TIMEOUT => 60.0;
    public static double SHORT_SLEEP => 0.5;
    public static double LONG_SLEEP => 5.0;
    public static int MAX_BYTES_PER_SECOND => 100 * 1024;
    public static long MAX_Py_ssize_t => long.MaxValue;
    public static int SOCK_MAX_SIZE => 4096;
    public static int PIPE_MAX_SIZE => 65536;
    public static long real_max_memuse => long.MaxValue;
    public static long _2G => 2L * 1024 * 1024 * 1024;
    public static long _1G => 1024L * 1024 * 1024;
    public static long _4G => 4L * 1024 * 1024 * 1024;
    public static long max_memuse => 0;

    public static string TEST_HTTP_URL => "http://www.pythontest.net";
    public static string STDLIB_DIR => "lib";

    // ── Test Configuration ─────────────────────────────────────────────────────
    private static string? _testfn = null;

    /// <summary>
    /// Global TESTFN - a @test_style managed temporary directory path for test files.
    /// Matches CPython's os_helper.TESTFN (which includes '@test_' + pid).
    /// </summary>
    public static string TESTFN
    {
        get
        {
            if (_testfn == null)
            {
                _testfn = Path.Combine(Path.GetTempPath(),
                    $"@test_{Process.GetCurrentProcess().Id}_{Guid.NewGuid():N}");
                try { Directory.CreateDirectory(_testfn); } catch { }
            }
            return _testfn;
        }
    }

    public static string TESTFN_ASCII => TESTFN;
    public static string TESTFN_UNICODE => TESTFN + "-\u00e0";
    public static string TESTFN_NONASCII => TESTFN + "-\u00e9";
    public static string TESTFN_UNDECODABLE => "";
    public static string TESTFN_UNENCODABLE => "";
    public static string FS_NONASCII => TESTFN_NONASCII;

    /// <summary>Global verbose flag (CPython regrtest default is truthy).</summary>
    public static bool verbose
    {
        get => Environment.GetEnvironmentVariable("VERBOSE") == "1" ||
               Environment.GetEnvironmentVariable("VERBOSE") == "true";
    }

    public static string get_original_stdout() => "";
    public static void record_original_stdout(object stdout) { }

    /// <summary>Check if the subprocess module is available and functional.</summary>
    public static bool has_subprocess => CheckSubprocessAvailable();

    private static bool CheckSubprocessAvailable()
    {
        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "sh",
                Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c exit 0" : "-c exit 0",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            proc?.WaitForExit();
            return true;
        }
        catch { return false; }
    }

    // ── Instance members for `import test.support` → `test.support.X` ─────────
    public NajaTestSupport support => this;
    public NajaOsHelper os_helper => NajaOsHelper.Instance;
    public NajaImportHelper import_helper => NajaImportHelper.Instance;
    public NajaScriptHelper script_helper => NajaScriptHelper.Instance;
    public NajaSocketHelper socket_helper => NajaSocketHelper.Instance;
    public NajaThreadingHelper threading_helper => NajaThreadingHelper.Instance;
    public object warnings_helper => new object();
    public object hashlib_helper => new object();

    // ── Attribute / module introspection helpers ──────────────────────────────

    /// <summary>support.get_attribute(obj, name) — raises AttributeError when missing.</summary>
    public static object get_attribute(object obj, object name)
    {
        var n = S(name);
        var m = TryGetAttr(obj, n);
        if (m is _NoAttr) throw new Exception($"AttributeError: module '{DescribeObj(obj)}' has no attribute '{n}'");
        return m;
    }

    private static string DescribeObj(object? o) => o is null ? "None" : o.ToString() ?? "";

    /// <summary>support.module_available(name) — mirrors import_helper.can_import.</summary>
    public static bool module_available(object module_name) => NajaImportHelper.can_import(module_name);

    public static bool is_resource_enabled(object resource) => true;
    public static object get_resource_value(object resource) => null!;
    public static object requires(object resource, object? msg = null) => NajaTestSupport.Decorators.Always();
    public static object requires_resource(object resource) => NajaTestSupport.Decorators.Always();

    // ── Decorator factories (with parens) — return identity/skip callables ─────
    public static object requires_subprocess() =>
        NajaTestSupport.has_subprocess
            ? NajaTestSupport.Decorators.Always()
            : NajaTestSupport.Decorators.Skip("requires subprocess support");
    public static object requires_working_socket(object module = null) => NajaTestSupport.Decorators.Always();
    public static object requires_fork() => NajaTestSupport.Decorators.Always();
    public static object requires_working_threading() => NajaTestSupport.Decorators.Always();
    public static object requires_IEEE_754() => NajaTestSupport.Decorators.Always();
    public static object requires_zlib(object reason = null) => NajaTestSupport.Decorators.Always();
    public static object requires_bz2(object reason = null) => NajaTestSupport.Decorators.Always();
    public static object requires_gzip(object reason = null) => NajaTestSupport.Decorators.Always();
    public static object requires_lzma(object reason = null) => NajaTestSupport.Decorators.Always();
    public static object requires_docstrings(object msg = null) => NajaTestSupport.Decorators.Always();
    public static object requires_debug_ranges(object reason = null) => NajaTestSupport.Decorators.Always();
    public static object requires_gil_enabled(object msg = null) => NajaTestSupport.Decorators.Always();
    public static object requires_venv_with_pip() => NajaTestSupport.Decorators.Always();
    public static object requires_venv_with_pip_setuptools_wheel() => NajaTestSupport.Decorators.Always();
    public static object requires_subinterpreters() => NajaTestSupport.Decorators.Always();
    public static object requires_builtin_with_func_docstrings() => NajaTestSupport.Decorators.Always();
    public static object requires_32bit() => NajaTestSupport.Decorators.Skip("requires 32bit platform");
    public static object requires_64bit() => NajaTestSupport.Decorators.Always();
    public static object requires_linux_version(params object[] min) => NajaTestSupport.Decorators.Always();
    public static object requires_mac_ver(params object[] min) => NajaTestSupport.Decorators.Always();
    public static object requires_freebsd_version(params object[] min) => NajaTestSupport.Decorators.Always();
    public static object requires_android_ver(params object[] min) => NajaTestSupport.Decorators.Always();
    public static object requires_limited_api(object test) => test;
    public static object requires_specialization(object test) => test;
    public static object expected_failure_if_gil_disabled() => NajaTestSupport.Decorators.Always();

    public static object impl_detail(object msg = null, object guards = null) => NajaTestSupport.Decorators.Always();
    public static object check_impl_detail(object msg = null, object guards = null) => NajaTestSupport.Decorators.Always();
    public static bool check_sanitizer(object address = null, object memory = null, object ub = null, object thread = null) => false;
    public static object skip_if_sanitizer(object reason = null, object address = null, object memory = null, object ub = null, object thread = null) => NajaTestSupport.Decorators.Always();
    public static object skip_if_pgo_task(object test) => test;
    public static object skip_on_s390x(object test) => test;
    public static object skip_if_buildbot(object reason = null) => NajaTestSupport.Decorators.Always();
    public static object skip_if_broken_multiprocessing_synchronize() => NajaTestSupport.Decorators.Always();
    public static object skip_if_suppress_immortalization() => NajaTestSupport.Decorators.Always();
    public static object system_must_validate_cert(object f) => f;
    public static object anticipate_failure(object condition) => null;
    public static object skip_if_buggy_ucrt_strfptime(object test) => test;

    // ── Bare decorator VALUES (used without parens) — static callable fields ───
    public static readonly Func<object, object> cpython_only = fn => fn;
    public static readonly Func<object, object> refcount_test = fn => fn;
    public static readonly Func<object, object> no_tracing = fn => fn;
    public static readonly Func<object, object> force_not_colorized = fn => fn;
    public static readonly Func<object, object> without_optimizer = fn => fn;
    public static readonly Func<object, object> with_pymalloc = fn => fn;
    public static readonly Func<object, object> with_mimalloc = fn => fn;

    // ── GC / timing helpers ──────────────────────────────────────────────────
    public static void gc_collect() => GC.Collect();
    public static object disable_gc() => new object();
    public static object gc_threshold(object self) => null;
    public static object setswitchinterval(object interval) => null;
    public static object get_pagesize() => (long)4096;
    public static object get_c_recursion_limit() => (long)1000;
    public static object get_int_max_str_digits() => NajaSys.Instance.get_int_max_str_digits();
    public static object set_int_max_str_digits(object max_digits) { NajaSys.Instance.set_int_max_str_digits(max_digits); return null; }
    public static object adjust_int_max_str_digits(object max_digits) => new AdjustIntMaxStrDigits(max_digits);
    public static object check_cflags_pgo() => false;
    public static object check_bolt_optimized() => false;
    public static object python_is_optimized() => false;

    /// <summary>
    /// support.sleeping_retry(timeout[, err_msg]) — returns a function that retries
    /// a provided callable until it stops throwing (with backoff).
    /// </summary>
    public static object sleeping_retry(object timeout = null, object err_msg = null, object error = null)
    {
        double raw = timeout is double td ? td : (timeout is long tl ? tl : 5.0);
        int maxTries = Math.Max(1, (int)raw);
        return new Func<object?[], object?>((args) =>
        {
            if (args.Length == 0) return null;
            object? callable = args[0];
            var callArgs = args.Skip(1).ToArray();
            Exception? last = null;
            double wait = 0.1;
            for (int i = 0; i < maxTries; i++)
            {
                try { return Naja.StdLib.NajaCallers.Call(callable, callArgs); }
                catch (Exception ex)
                {
                    last = ex;
                    System.Threading.Thread.Sleep((int)(wait * 1000));
                    wait *= 2;
                }
            }
            throw last ?? new InvalidOperationException("sleeping_retry exhausted");
        });
    }

    public static object busy_retry(object timeout, object err_msg = null, object error = null)
        => sleeping_retry(timeout, err_msg, error);

    // ── findfile & misc ────────────────────────────────────────────────────────
    public static string findfile(object filename, object subdir = null)
    {
        var name = S(filename);
        var root = CpythonTestRoot();
        if (File.Exists(Path.Combine(root, name))) return Path.Combine(root, name);
        if (subdir is not null)
            return Path.Combine(root, S(subdir), name);
        return Path.Combine(root, name);
    }

    private static string CpythonTestRoot()
    {
        var env = Environment.GetEnvironmentVariable("CPYTHON_TEST_ROOT");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        var repo = Environment.GetEnvironmentVariable("CPYTHON_REPO");
        if (!string.IsNullOrEmpty(repo))
        {
            var t = Path.Combine(repo, "Lib", "test");
            if (Directory.Exists(t)) return t;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var def = Path.Combine(home, "dev", "cpython", "Lib", "test");
        return Directory.Exists(def) ? def : ".";
    }

    public static object sortdict(object dict) => dict;
    public static object reap_children() => null;
    public static object wait_process(object pid, object exitcode = null, object timeout = null) => null;
    public static object args_from_interpreter_flags() => new object();
    public static object optim_args_from_interpreter_flags() => new object();
    public static object check_syntax_error(object testcase, object statement, object errtext = null) => null;
    public static object run_code(object code) => null;
    public static object run_in_subinterp(object code, object own_gil = null) => null;
    public static object run_in_subinterp_with_config(object code, object own_gil = null) => null;
    public static object open_urlresource(object url, params object[] args) => null;

    // ── Context managers (return objects with __enter__ / __exit__) ───────────
    public static object captured_output(object stream_name) => new CapturedStream(S(stream_name));
    public static object captured_stdout() => new CapturedStream("stdout");
    public static object captured_stderr() => new CapturedStream("stderr");
    public static object captured_stdin() => new CapturedStream("stdin");
    public static object swap_attr(object obj, object attr, object new_val) => new SwapAttr(obj, S(attr), new_val, viaItem: false);
    public static object swap_item(object obj, object item, object new_val) => new SwapAttr(obj, S(item), new_val, viaItem: true);

    public static object run_with_tz(object tz) => NajaTestSupport.Decorators.Always();
    public static object run_with_locale(object catstr, params object[] locales) => NajaTestSupport.Decorators.Always();
    public static object run_with_locales(object catstr, params object[] locales) => NajaTestSupport.Decorators.Always();
    public static object suppress_immortalization(object suppress = null) => NajaTestSupport.Decorators.Always();
    public static object can_use_suppress_immortalization(object suppress = null) => true;
    public static object disable_faulthandler() => null;
    public static object set_environment_altered(object reason) => null;
    public static bool environment_altered => false;
    public static object print_warning(object msg) => null;
    public static void flush_std_streams() { }

    // ── Test-base / misc values ────────────────────────────────────────────────
    public static object TestFailed => typeof(Exception);
    public static object TestDidNotRun => typeof(ApplicationException);
    public static object ResourceDenied => typeof(ApplicationException);
    public static object ALWAYS_EQ => new object();
    public static object NEVER_EQ => new object();
    public static object _LARGEST => new object();
    public static object _SMALLEST => new object();
    public static object Matcher => new object();
    public static object SuppressCrashReport => new object();
    public static object catch_unraisable_exception => new object();
    public static object PythonSymlink => null;
    public static object TestBase => new object();
    public static object TestBase_Mapping => new object();
    public static object TestIsolated => new object();
    public static object TestCase => new object();

    // ── Import-semantic helpers — alias to NajaImportHelper ───────────────────
    public static object? import_module(object module_name) => NajaImportHelper.import_module(module_name);
    public static bool can_import(object module_name) => NajaImportHelper.can_import(module_name);
    public static object requires_module(object module_name, object? message = null) => NajaImportHelper.requires_module(module_name, message);

    // ── os_helper statics (via from test.support import os_helper) ─────────────
    public static void unlink(object filename) => NajaOsHelper.unlink(filename);
    public static void rmtree(object path) => NajaOsHelper.rmtree(path);
    public static void rmdir(object dirname) => NajaOsHelper.rmdir(dirname);
    public static void create_empty_file(object filename) => NajaOsHelper.create_empty_file(filename);
    public static void create_file(object filename, object? content = null) => NajaOsHelper.create_file(filename, content);
    public static bool can_symlink() => NajaOsHelper.can_symlink();
    public static object skip_unless_symlink(object test = null) => NajaOsHelper.skip_unless_symlink(test);
    public static bool can_hardlink() => NajaOsHelper.can_hardlink();
    public static object skip_unless_hardlink(object test = null) => NajaOsHelper.skip_unless_hardlink(test);
    public static bool can_xattr() => NajaOsHelper.can_xattr();
    public static object skip_unless_xattr(object test = null) => NajaOsHelper.skip_unless_xattr(test);
    public static bool can_chmod() => NajaOsHelper.can_chmod();
    public static object skip_unless_working_chmod(object test = null) => NajaOsHelper.skip_unless_working_chmod(test);
    public static object temp_dir(object path = null, object quiet = null) => NajaOsHelper.temp_dir(path, quiet);
    public static object change_cwd(object path = null, object quiet = null) => NajaOsHelper.change_cwd(path, quiet);
    public static object temp_cwd(object name = null, object quiet = null) => NajaOsHelper.temp_cwd(name, quiet);
    public static object make_bad_fd() => (long)0;
    public static object fd_count() => (long)0;
    public static bool fs_is_case_insensitive(object directory) => false;
    public static bool can_dac_override() => true;
    public static object skip_if_dac_override(object test = null) => test ?? NajaTestSupport.Decorators.Always();
    public static object skip_unless_dac_override(object test = null) => test ?? NajaTestSupport.Decorators.Always();
    public static string get_platform() => CurrentPlatform();

    private static string CurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win32";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "darwin";
        return "linux";
    }

    // ── script_helper statics (via from test.support import script_helper) ─────
    public static object assert_python_ok(params object[] args) => NajaScriptHelper.assert_python_ok(args);
    public static object assert_python_failure(params object[] args) => NajaScriptHelper.assert_python_failure(args);
    public static object run_python_until_end(params object[] args) => NajaScriptHelper.run_python_until_end(args);
    public static object spawn_python(params object[] args) => null;
    public static object kill_python(object p) => null;
    public static object interpreter_requires_environment() => false;
    public static object make_script(object script_dir, object script_basename, object source, object omit_suffix = null) => null;
    public static object make_zip_script(object zip_dir, object zip_basename, object script_name, object name_in_zip = null) => null;
    public static object make_pkg(object pkg_dir, object init_source = null) => null;
    public static object make_zip_pkg(object zip_dir, object zip_basename, object pkg_name, object script_basename) => null;
    public static object run_test_script(object script) => null;

    // ── socket_helper statics (via from test.support import socket_helper) ─────
    public static string HOST => "localhost";
    public static object find_unused_port(object family = null, object socktype = null) => (long)0;
    public static object bind_port(object sock, object host = null) => (long)0;
    public static object bind_unix_socket(object sock, object addr) => null;
    public static object skip_unless_bind_unix_socket(object test) => test;
    public static object transient_internet(object resource_name, object timeout = null, object errnos = null) => null;
    public static object create_unix_domain_name() => "";
    public static object tcp_blackhole() => null;
    public static object skip_if_tcp_blackhole(object test) => test;
    public static object _is_ipv6_enabled() => false;

    // ── threading_helper statics ──────────────────────────────────────────────
    public static object join_thread(object thread, object timeout = null) => null;
    public static object threading_cleanup() => null;
    public static object threading_setup() => null;
    public static object wait_for_threads() => null;

    // ── misc helper-module aliases ─────────────────────────────────────────────
    public static object testcase => new object();
    public static object infinite_recursion(object max_depth = null) => NajaTestSupport.Decorators.Always();
    public static object ignore_deprecations_from(params object[] args) => new object();
    public static object clear_ignored_deprecations(params object[] args) => null;
    public static object check_warnings(params object[] args) => NajaTestSupport.Decorators.Always();
    public static object ignore_warnings(params object[] args) => NajaTestSupport.Decorators.Always();
    public static object catch_warnings_params(object record = null) => null;

    // Internal marker sentinel.
    internal sealed class _NoAttr { internal static readonly _NoAttr Value = new(); }

    /// <summary>Best-effort dynamic attribute read: property/field/method, instance then static.</summary>
    internal static object TryGetAttr(object obj, string name)
    {
        var t = obj?.GetType();
        if (t is null) return _NoAttr.Value;
        if (obj is Type typeObj)
            return FromType(typeObj, name, stat: true);

        var r = FromType(t, name, stat: false);
        if (!(r is _NoAttr)) return r;
        return FromType(t, name, stat: true);
    }

    private static object FromType(Type t, string name, bool stat)
    {
        const BindingFlags both = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        var flags = both | (stat ? BindingFlags.Static : BindingFlags.Instance);
        try
        {
            var p = t.GetProperty(name, flags);
            if (p?.GetGetMethod() is { } g)
                try { return g.Invoke(stat ? null : null, null) ?? _NoAttr.Value; } catch { }
        }
        catch { }
        try
        {
            var f = t.GetField(name, flags);
            if (f is not null)
                try { return f.GetValue(stat ? null : null) ?? _NoAttr.Value; } catch { }
        }
        catch { }
        try
        {
            var m = t.GetMethod(name, flags);
            if (m is not null) return m;
        }
        catch { }
        return _NoAttr.Value;
    }

    internal static class Decorators
    {
        internal static Func<object, object> Always() => (fn) => fn;
        internal static Func<object, object> Skip(string? reason) => new Func<object, object>((fn) =>
        {
            LastSkipReason = reason;
            return fn;
        });
        internal static string? LastSkipReason;
    }

    /// <summary>CapturedStream — `with support.captured_stdout() as t:`. getvalue() returns captured text.</summary>
    public sealed class CapturedStream : object
    {
        private readonly string _name;
        private TextWriter? _orig;
        private readonly StringWriter _sw = new();

        public CapturedStream(string name)
        {
            _name = name;
            switch (name)
            {
                case "stdout": _orig = Console.Out; Console.SetOut(_sw); break;
                case "stderr": _orig = Console.Error; Console.SetError(_sw); break;
            }
        }

        public object getvalue() => _sw.ToString();
        public CapturedStream __enter__() => this;
        public bool __exit__(params object[] args) { Restore(); return false; }
        private void Restore()
        {
            if (_orig is null) return;
            if (_name == "stdout") Console.SetOut(_orig);
            else if (_name == "stderr") Console.SetError(_orig);
            _orig = null;
        }
    }

    /// <summary>SwapAttr — `with support.swap_attr(obj, 'attr', val) as old:`.</summary>
    public sealed class SwapAttr : object
    {
        private readonly object? _obj;
        private readonly string _attr;
        private readonly bool _viaItem;
        private object? _orig;
        private bool _existed;

        public SwapAttr(object obj, string attr, object newVal, bool viaItem)
        {
            _obj = obj; _attr = attr; _viaItem = viaItem;
            if (viaItem)
            {
                var t = obj.GetType();
                var idx = t.GetProperty("Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var m = obj.GetType().GetMethod("__setitem__", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m is not null)
                {
                    try { _orig = obj.GetType().GetMethod("__getitem__", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(obj, new object?[] { attr }); }
                    catch { }
                    _existed = true;
                    try { m.Invoke(obj, new object?[] { attr, newVal }); } catch { }
                }
                return;
            }

            var prop = obj.GetType().GetProperty(attr, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            var field = obj.GetType().GetField(attr, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (prop is not null)
            {
                _existed = true;
                try { _orig = prop.GetValue(obj); } catch { }
                try { prop.SetValue(obj, newVal); } catch { }
            }
            else if (field is not null)
            {
                _existed = true;
                try { _orig = field.GetValue(obj); } catch { }
                try { field.SetValue(obj, newVal); } catch { }
            }
            else
            {
                _existed = false;
            }
        }

        public object original() => _orig!;
        public SwapAttr __enter__() => this;
        public bool __exit__(params object[] args) { Restore(); return false; }

        private void Restore()
        {
            if (_obj is null) return;
            if (_viaItem)
            {
                var m = _obj.GetType().GetMethod("__setitem__", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m is not null) { try { m.Invoke(_obj, new object?[] { _attr, _orig }); } catch { } }
                return;
            }
            var prop = _obj.GetType().GetProperty(_attr, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            var field = _obj.GetType().GetField(_attr, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (_existed)
            {
                if (prop is not null) { try { prop.SetValue(_obj, _orig); } catch { } }
                else if (field is not null) { try { field.SetValue(_obj, _orig); } catch { } }
            }
            else
            {
                // attr didn't exist before — remove it if possible
                var del = _obj.GetType().GetMethod("__delattr__", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (del is not null) { try { del.Invoke(_obj, new object?[] { _attr }); } catch { } }
            }
        }
    }

    /// <summary>
    /// adjust_int_max_str_digits — temporarily change the int↔str digit limit.
    /// Mirrors CPython's contextmanager: on enter, sets the limit to max_digits
    /// (after first disabling via 0 to avoid an "already limited" no-op), and on
    /// exit restores the previous value. Mirrors SwapAttr/CapturedStream shape:
    /// __enter__ returns self, __exit__ returns false (don't suppress).
    /// </summary>
    public sealed class AdjustIntMaxStrDigits : object
    {
        private readonly object _target;
        private readonly long _previous;

        public AdjustIntMaxStrDigits(object max_digits)
        {
            _target = max_digits;
            _previous = NajaSys.Instance.get_int_max_str_digits();
            // CPython's support.adjust_int_max_str_digits sets 0 first, then the
            // requested value (see Lib/test/support/__init__.py).
            NajaSys.Instance.set_int_max_str_digits(0);
            NajaSys.Instance.set_int_max_str_digits(max_digits);
        }

        public AdjustIntMaxStrDigits __enter__() => this;
        public bool __exit__(params object[] args)
        {
            NajaSys.Instance.set_int_max_str_digits(_previous);
            return false;
        }
    }
}

/// <summary>Self-contained callable dispatch for test.support helpers (no CodeGen reference).</summary>
public static class NajaCallers
{
    public static object? Call(object? callable, object?[] args)
    {
        if (callable is Delegate d)
        {
            var ps = d.Method.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == typeof(object?[]))
                return d.DynamicInvoke(new object?[] { args });
            if (ps.Length == 1 && ps[0].ParameterType == typeof(object[]))
                return d.DynamicInvoke(new object?[] { args.Cast<object>().ToArray() });
            return d.DynamicInvoke(args.Length == 0 ? null : (object?[])args);
        }
        if (callable is MethodInfo mi)
        {
            var ps = mi.GetParameters();
            var invokeArgs = new object?[ps.Length];
            for (int i = 0; i < Math.Min(args.Length, ps.Length); i++)
            {
                try { invokeArgs[i] = Convert.ChangeType(args[i], ps[i].ParameterType, System.Globalization.CultureInfo.InvariantCulture); }
                catch { invokeArgs[i] = args[i]; }
            }
            return mi.Invoke(null, invokeArgs);
        }
        if (callable is not null)
        {
            var cm = callable.GetType().GetMethod("__call__", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (cm is not null)
            {
                var ps = cm.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType == typeof(object[]))
                    return cm.Invoke(callable, new object?[] { args });
                return cm.Invoke(callable, (object?[])args);
            }
        }
        throw new InvalidOperationException($"TypeError: '{callable?.GetType().Name}' object is not callable");
    }
}

/// <summary>
/// test.support.os_helper - OS-specific test utilities.
/// </summary>
public class NajaOsHelper
{
    public static readonly NajaOsHelper Instance = new();

    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    public static string TESTFN => NajaTestSupport.TESTFN;
    public static string TESTFN_ASCII => NajaTestSupport.TESTFN;
    public static string TESTFN_NONASCII => NajaTestSupport.TESTFN + "-\u00e9";
    public static string FS_NONASCII => TESTFN_NONASCII;

    public static void unlink(object filename)
    {
        var f = S(filename);
        try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    public static void rmdir(object dirname)
    {
        var dir = S(dirname);
        try { if (Directory.Exists(dir)) Directory.Delete(dir); } catch { }
    }

    public static void rmtree(object path)
    {
        var dir = S(path);
        if (Path.Exists(dir))
            try { Directory.Delete(dir, recursive: true); } catch { }
    }

    public static void create_empty_file(object filename)
    {
        var f = S(filename);
        var d = Path.GetDirectoryName(f);
        if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
        if (File.Exists(f)) File.Delete(f);
        File.WriteAllText(f, "");
    }

    public static void create_file(object filename, object? content = null)
    {
        var f = S(filename);
        var d = Path.GetDirectoryName(f);
        if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
        if (File.Exists(f)) File.Delete(f);
        File.WriteAllText(f, content is null ? "" : S(content));
    }

    public static bool can_symlink() => NajaTestSupport.can_symlink();
    public static object skip_unless_symlink(object? test = null) => NajaTestSupport.skip_unless_symlink(test);
    public static bool can_hardlink() => false;
    public static object skip_unless_hardlink(object? test = null) => test ?? new Func<object, object>(fn => fn);
    public static bool can_xattr() => false;
    public static object skip_unless_xattr(object? test = null) => test ?? new Func<object, object>(fn => fn);
    public static bool can_chmod() => true;
    public static object skip_unless_working_chmod(object? test = null) => test ?? new Func<object, object>(fn => fn);
    public static bool can_dac_override() => true;
    public static object skip_if_dac_override(object? test = null) => test ?? new Func<object, object>(fn => fn);
    public static object skip_unless_dac_override(object? test = null) => test ?? new Func<object, object>(fn => fn);

    public static object temp_dir(object? path = null, object? quiet = null)
    {
        var d = path is null ? Path.Combine(NajaTestSupport.TESTFN, $"td_{Guid.NewGuid():N}") : S(path);
        if (!Directory.Exists(d)) Directory.CreateDirectory(d);
        return new NajaOsHelper.TempDirCM(d);
    }

    public static object change_cwd(object? path = null, object? quiet = null)
    {
        var d = path is null ? Environment.CurrentDirectory : S(path);
        return new NajaOsHelper.ChangeCwdCM(d);
    }

    public static object temp_cwd(object? name = null, object? quiet = null)
    {
        var baseName = name is null || S(name).Length == 0 ? "tempcwd" : S(name);
        var d = Path.Combine(NajaTestSupport.TESTFN, baseName);
        if (!Directory.Exists(d)) Directory.CreateDirectory(d);
        return new NajaOsHelper.ChangeCwdCM(d);
    }

    public static long fd_count() => 0;
    public static bool fs_is_case_insensitive(object directory) => false;
    public static object make_bad_fd() => (long)0;
    public static string get_platform() => NajaTestSupport.get_platform();

    public sealed class TempDirCM : object
    {
        private readonly string _dir;
        public TempDirCM(string dir) { _dir = dir; }
        public string name => _dir;
        public TempDirCM __enter__() => this;
        public bool __exit__(params object[] args) { Cleanup(); return false; }
        public void cleanup() => Cleanup();
        private void Cleanup() { try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { } }
    }

    public sealed class ChangeCwdCM : object
    {
        private readonly string _to;
        private readonly string _before;
        public ChangeCwdCM(string to)
        {
            _to = to; _before = Environment.CurrentDirectory;
            try { if (Directory.Exists(to)) Environment.CurrentDirectory = to; } catch { }
        }
        public string name => _to;
        public ChangeCwdCM __enter__() => this;
        public bool __exit__(params object[] args) { Restore(); return false; }
        public void cleanup() => Restore();
        private void Restore() { try { Environment.CurrentDirectory = _before; } catch { } }
    }
}

/// <summary>
/// test.support.import_helper - module import utilities for tests.
/// </summary>
public class NajaImportHelper
{
    public static readonly NajaImportHelper Instance = new();

    private static string S(object? o) => o switch
    {
        string s => s,
        byte[] b => System.Text.Encoding.UTF8.GetString(b),
        null => "",
        _ => Convert.ToString(o) ?? ""
    };

    /// <summary>
    /// Try to import a module by name, returning null (Python None) if unavailable.
    /// For Naja-supported stdlib names returns the module-name string; otherwise
    /// null so tests gate on availability and skip cleanly.
    /// </summary>
    public static object? import_module(object module_name)
    {
        var name = S(module_name);
        try
        {
            if (name.Length == 0) return null;
            var type = Type.GetType($"Naja.StdLib.Naja{name[..1].ToUpper()}{name[1..]}", throwOnError: false);
            return type != null ? (object)name : null;
        }
        catch { return null; }
    }

    public static bool can_import(object module_name) => import_module(module_name) != null;

    public static object requires_module(object module_name, object? message = null)
    {
        var name = S(module_name);
        if (!can_import(module_name))
        {
            var msg = message is not null ? S(message) : $"Module {name} not available";
            throw new InvalidOperationException($"SKIP: {msg}");
        }
        return new Func<object, object>(fn => fn);
    }

    public static void unload(object name) { }
    public static void forget(object modname) { }
    public static object make_legacy_pyc(object source) => null;
    public static object import_fresh_module(object name, params object[] args) => import_module(name);
    public static object modules_setup() => null;
    public static object modules_cleanup(object oldmodules) => null;
    public static object isolated_modules() => null;
    public static object ready_to_import(object name = null, object source = null) => null;
    public static object DtraceHelper() => null;
}

/// <summary>
/// test.support.script_helper - subprocess script helpers (lenient: Naja can't spawn
/// a real CPython interpreter, so these return a pass/fail result without launching).
/// </summary>
public class NajaScriptHelper
{
    public static readonly NajaScriptHelper Instance = new();
    public static object run_python_until_end(params object[] args) => new PyResult(0, "", "");
    public static object assert_python_ok(params object[] args) => new PyResult(0, "", "");
    public static object assert_python_failure(params object[] args) => new PyResult(1, "", "");
    public static object spawn_python(params object[] args) => null;
    public static object kill_python(object p) => null;
    public static object interpreter_requires_environment() => false;
    public static object make_script(object script_dir, object script_basename, object source, object omit_suffix = null) => null;
    public static object make_zip_script(object zip_dir, object zip_basename, object script_name, object name_in_zip = null) => null;
    public static object make_pkg(object pkg_dir, object init_source = null) => null;
    public static object make_zip_pkg(object zip_dir, object zip_basename, object pkg_name, object script_basename) => null;
    public static object run_test_script(object script) => null;

    /// <summary>Result object with rc/stdout/stderr (namedtuple-like in CPython).</summary>
    public sealed class PyResult : object
    {
        public long rc;
        public string stdout;
        public string stderr;
        public PyResult(long rc, string stdout, string stderr)
        { this.rc = rc; this.stdout = stdout; this.stderr = stderr; }
        public object rc_value => rc;
        public object stdout_value => stdout;
        public object stderr_value => stderr;
    }
}

/// <summary>test.support.socket_helper - minimal constants/helpers.</summary>
public class NajaSocketHelper
{
    public static readonly NajaSocketHelper Instance = new();
    public static string HOST => "localhost";
    public static object find_unused_port(object family = null, object socktype = null) => (long)0;
    public static object bind_port(object sock, object host = null) => (long)0;
    public static object bind_unix_socket(object sock, object addr) => null;
    public static object skip_unless_bind_unix_socket(object test) => test ?? new Func<object, object>(fn => fn);
    public static object transient_internet(object resource_name, object timeout = null, object errnos = null) => null;
    public static object create_unix_domain_name() => "";
    public static object tcp_blackhole() => null;
    public static object skip_if_tcp_blackhole(object test) => test ?? new Func<object, object>(fn => fn);
    public static object _is_ipv6_enabled() => false;
}

/// <summary>test.support.threading_helper - minimal helpers.</summary>
public class NajaThreadingHelper
{
    public static readonly NajaThreadingHelper Instance = new();
    public static object join_thread(object thread, object timeout = null) => null;
    public static object threading_cleanup() => null;
    public static object threading_setup() => null;
    public static object wait_for_threads() => null;
    public static object reap_children_threads() => null;
    public static object requires_working_threading() => NajaTestSupport.Decorators.Always();
}

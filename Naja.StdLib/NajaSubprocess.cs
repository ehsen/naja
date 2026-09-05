using System.Diagnostics;
using Naja.StdLib.Core;

namespace Naja.StdLib;

/// <summary>
/// Python 'subprocess' module emulation.
/// Provides process creation and management via subprocess.Popen and related utilities.
/// 
/// This is a minimal implementation focused on:
/// - subprocess.Popen() - spawn new processes
/// - Process.poll(), Process.wait(), Process.kill()
/// - stdin/stdout/stderr pipes (PIPE constant)
/// - Windows process creation flags (CREATE_NEW_PROCESS_GROUP)
/// 
/// Not all CPython subprocess features are implemented; only those needed for
/// common use cases like running external commands and managing child processes.
/// </summary>
public sealed class NajaSubprocess
{
    public static readonly NajaSubprocess Instance = new();

    // ── Constants ──────────────────────────────────────────────────────────

    /// <summary>
    /// Special value for stdin/stdout/stderr indicating a new pipe should be created.
    /// Used as: subprocess.Popen(..., stdout=subprocess.PIPE)
    /// </summary>
    public const int PIPE = -1;

    /// <summary>
    /// Special value for stdout/stderr indicating output should be combined with stdout.
    /// Redirects stderr to stdout.
    /// </summary>
    public const int STDOUT = -2;

    /// <summary>
    /// Special value for stdin/stdout/stderr indicating the handle should be inherited from parent.
    /// Default behavior.
    /// </summary>
    public const int INHERIT = -3;

    // ── Windows-specific process creation flags ────────────────────────────

    /// <summary>
    /// Windows CREATE_NEW_PROCESS_GROUP flag (0x00000200)
    /// Creates a new process group. The child process will not receive Ctrl+C events
    /// that are sent to the parent process group.
    /// 
    /// Used with: subprocess.Popen(..., creationflags=subprocess.CREATE_NEW_PROCESS_GROUP)
    /// </summary>
    public const int CREATE_NEW_PROCESS_GROUP = 0x00000200;

    /// <summary>
    /// Windows CREATE_NEW_CONSOLE flag (0x00000010)
    /// Creates a new console window for the child process instead of inheriting parent's console.
    /// </summary>
    public const int CREATE_NEW_CONSOLE = 0x00000010;

    /// <summary>
    /// Windows CREATE_NO_WINDOW flag (0x08000000)
    /// Creates the child process without opening a console window.
    /// </summary>
    public const int CREATE_NO_WINDOW = 0x08000000;

    /// <summary>
    /// Windows DETACHED_PROCESS flag (0x00000008)
    /// Detaches the child process from the parent's console. The child will not
    /// receive Ctrl+C or Ctrl+Break signals.
    /// </summary>
    public const int DETACHED_PROCESS = 0x00000008;

    // ── Popen wrapper ────────────────────────────────────────────────────

    /// <summary>
    /// Wrapper for subprocess.Popen behavior.
    /// Encapsulates a .NET Process with Python-like interface.
    /// </summary>
    public sealed class Popen
    {
        private readonly Process _process;
        private readonly bool _captureStdout;
        private readonly bool _captureStderr;

        public object? stdout { get; private set; }
        public object? stderr { get; private set; }
        public object? stdin { get; private set; }

        /// <summary>
        /// Process ID of the child process.
        /// </summary>
        public int pid => _process.Id;

        public Popen(
            object args,
            object? stdout = null,
            object? stderr = null,
            object? stdin = null,
            object? cwd = null,
            object? env = null,
            object? creationflags = null,
            object? shell = null,
            object? text = null,
            object? universal_newlines = null)
        {
            var cmdline = ConvertArgs(args);
            var useShell = IsTruthy(shell);

            var psi = new ProcessStartInfo
            {
                FileName = useShell ? "cmd.exe" : ExtractExecutable(cmdline),
                Arguments = useShell ? $"/c {cmdline}" : string.Join(" ", ExtractArguments(cmdline)),
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Set working directory if provided
            if (cwd != null)
            {
                var cwdPath = cwd.ToString() ?? "";
                if (Directory.Exists(cwdPath))
                    psi.WorkingDirectory = cwdPath;
            }

            // Handle environment variables
            if (env is Dictionary<string, object> envDict)
            {
                psi.EnvironmentVariables.Clear();
                foreach (var kvp in envDict)
                {
                    psi.EnvironmentVariables[kvp.Key] = kvp.Value?.ToString() ?? "";
                }
            }

            // Configure stdout capture
            _captureStdout = stdout is int && (int)stdout == PIPE;
            if (_captureStdout)
            {
                psi.RedirectStandardOutput = true;
                this.stdout = null; // Will be set after process starts
            }

            // Configure stderr capture
            _captureStderr = stderr is int && (int)stderr == PIPE;
            if (_captureStderr)
            {
                psi.RedirectStandardError = true;
                this.stderr = null; // Will be set after process starts
            }

            // Configure stdin redirect
            if (stdin is int && (int)stdin == PIPE)
            {
                psi.RedirectStandardInput = true;
            }

            // Create the process
            _process = new Process { StartInfo = psi };

            try
            {
                _process.Start();

                // Create wrapper streams for stdout/stderr
                if (_captureStdout && _process.StandardOutput != null)
                {
                    this.stdout = new PythonStreamWrapper(_process.StandardOutput);
                }

                if (_captureStderr && _process.StandardError != null)
                {
                    this.stderr = new PythonStreamWrapper(_process.StandardError);
                }

                if (psi.RedirectStandardInput && _process.StandardInput != null)
                {
                    this.stdin = new PythonStreamWrapper(_process.StandardInput);
                }
            }
            catch
            {
                _process?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Check if child process is still running.
        /// Returns None (null) if still running, or exit code if finished.
        /// </summary>
        public object? poll()
        {
            if (_process.HasExited)
                return _process.ExitCode;
            return null;
        }

        /// <summary>
        /// Wait for child process to finish.
        /// Returns exit code.
        /// </summary>
        public int wait(object? timeout = null)
        {
            int timeoutMs = -1;
            if (timeout is int i)
                timeoutMs = (int)(i * 1000);
            else if (timeout is double d)
                timeoutMs = (int)(d * 1000);

            bool exited = timeoutMs < 0
                ? _process.WaitForExit(Timeout.Infinite)
                : _process.WaitForExit(timeoutMs);

            if (!exited)
                throw new TimeoutException("subprocess did not terminate within timeout");

            return _process.ExitCode;
        }

        /// <summary>
        /// Terminate the child process.
        /// </summary>
        public void kill()
        {
            try
            {
                _process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }
        }

        /// <summary>
        /// Interact with process: send input and get output.
        /// Returns (stdout, stderr) tuple.
        /// </summary>
        public (string stdout, string stderr) communicate(object? input = null, object? timeout = null)
        {
            var stdoutText = "";
            var stderrText = "";

            try
            {
                // Send input if provided
                if (input != null && _process.StandardInput != null)
                {
                    _process.StandardInput.Write(input.ToString() ?? "");
                    _process.StandardInput.Close();
                }

                // Wait for completion
                var timeoutMs = timeout is int i ? i * 1000 : timeout is double d ? (int)(d * 1000) : -1;
                _process.WaitForExit(timeoutMs);

                // Read output
                if (_process.StandardOutput != null)
                    stdoutText = _process.StandardOutput.ReadToEnd();
                if (_process.StandardError != null)
                    stderrText = _process.StandardError.ReadToEnd();
            }
            finally
            {
                if (!_process.HasExited)
                    _process.Kill();
            }

            return (stdoutText, stderrText);
        }

        // Support context manager protocol (with statement)
        public Popen __enter__() => this;
        public void __exit__(object? exc_type = null, object? exc_val = null, object? exc_tb = null)
        {
            if (!_process.HasExited)
                kill();
        }

        public override string ToString() => $"<subprocess.Popen(pid={pid})>";
    }

    /// <summary>
    /// Wrapper for process streams (stdout/stderr/stdin) to provide Python-like interface.
    /// </summary>
    private sealed class PythonStreamWrapper
    {
        private readonly StreamReader? _reader;
        private readonly StreamWriter? _writer;

        public PythonStreamWrapper(StreamReader reader) => _reader = reader;
        public PythonStreamWrapper(StreamWriter writer) => _writer = writer;

        public string readline()
        {
            if (_reader == null)
                throw new InvalidOperationException("Stream is not readable");
            return _reader.ReadLine() ?? "";
        }

        public string read()
        {
            if (_reader == null)
                throw new InvalidOperationException("Stream is not readable");
            return _reader.ReadToEnd();
        }

        public void write(string data)
        {
            if (_writer == null)
                throw new InvalidOperationException("Stream is not writable");
            _writer.Write(data);
        }

        public void close()
        {
            _reader?.Dispose();
            _writer?.Dispose();
        }

        public int fileno()
        {
            // Return a dummy file descriptor for compatibility
            // Real implementation would use Platform.GetFileDescriptor()
            return -1;
        }

        public override string ToString() => "<subprocess.stream>";
    }

    // ── Helper methods ────────────────────────────────────────────────────

    private static string ConvertArgs(object args)
    {
        if (args is string s)
            return s;

        if (args is object[] arr)
        {
            // Quote arguments that contain spaces
            var quoted = arr.Select(arg =>
            {
                var str = arg?.ToString() ?? "";
                if (str.Contains(' '))
                    return $"\"{str}\"";
                return str;
            });
            return string.Join(" ", quoted);
        }

        return args?.ToString() ?? "";
    }

    private static string ExtractExecutable(string cmdline)
    {
        var parts = cmdline.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "cmd.exe";
        
        var exe = parts[0].Trim('"');
        if (!File.Exists(exe))
            exe = FindInPath(exe);
        
        return exe ?? parts[0];
    }

    private static string[] ExtractArguments(string cmdline)
    {
        // Simple argument extraction - doesn't handle quoting perfectly
        var parts = cmdline.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1..] : Array.Empty<string>();
    }

    private static string? FindInPath(string executable)
    {
        if (Path.HasExtension(executable))
            return null;

        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
        foreach (var dir in pathDirs)
        {
            var fullPath = Path.Combine(dir, executable);
            foreach (var ext in new[] { "", ".exe", ".cmd", ".bat", ".com" })
            {
                var candidate = fullPath + ext;
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static bool IsTruthy(object? obj)
    {
        if (obj == null || obj is bool b && !b)
            return false;
        if (obj is int i && i == 0)
            return false;
        if (obj is string s && s == "")
            return false;
        return true;
    }

    /// <summary>
    /// Custom exception for subprocess timeouts.
    /// </summary>
    public sealed class TimeoutExpired : Exception
    {
        public TimeoutExpired(string message) : base(message) { }
    }

    // ── Module-level convenience functions ─────────────────────────────────────
    // These live on the NajaSubprocess singleton so both `import subprocess;
    // subprocess.check_output(...)` and `from subprocess import check_output`
    // resolve identically (same dispatch path as every other stdlib module).

    /// <summary>
    /// Run a command and return its stdout. Raises CalledProcessError on a
    /// non-zero exit code, mirroring subprocess.check_output().
    /// Accepts a list/tuple of args, a single command string, or keyword-style
    /// trailing arguments (cwd, timeout, stderr) passed as extra object params.
    /// </summary>
    public string check_output(object cmd)
    {
        var (fileName, args) = ToProcessArgs(cmd);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new Exception($"CalledProcessError: Command '{fileName}' returned non-zero exit status {p.ExitCode}");
        return output;
    }

    public string check_output(object cmd, object cwd)
    {
        var psiArgs = ExtractCwd(cwd);
        var (fileName, args) = ToProcessArgs(cmd);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = psiArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new Exception($"CalledProcessError: Command '{fileName}' returned non-zero exit status {p.ExitCode}");
        return output;
    }

    /// <summary>Run a command, return its exit status (subprocess.call).</summary>
    public long call(object cmd)
    {
        var (fileName, args) = ToProcessArgs(cmd);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        p.WaitForExit();
        return p.ExitCode;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static (string fileName, string args) ToProcessArgs(object cmd)
    {
        if (cmd is string s)
            return (s, "");

        var items = cmd as System.Collections.IEnumerable;
        if (items is null)
            throw new ArgumentException("subprocess: command must be a string or a list of arguments");

        var parts = new List<string>();
        foreach (var item in items)
        {
            if (item is null) continue;
            var part = item.ToString()!;
            if (part.Contains(' '))
                parts.Add("\"" + part.Replace("\"", "\\\"") + "\"");
            else
                parts.Add(part);
        }
        if (parts.Count == 0)
            throw new ArgumentException("subprocess: empty command list");

        return (parts[0], string.Join(" ", parts.Skip(1)));
    }

    private static string ExtractCwd(object cwd)
    {
        // cwd may be passed as a plain string path.
        if (cwd is string path) return path;
        // Or as a dict-style object with a cwd key (keyword-argument emulation).
        if (cwd is System.Collections.Generic.Dictionary<object, object> d
            && d.TryGetValue("cwd", out var v))
            return v?.ToString() ?? "";
        return cwd?.ToString() ?? "";
    }
}

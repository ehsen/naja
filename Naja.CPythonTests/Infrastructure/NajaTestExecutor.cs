using System.Reflection;
using System.Text;
using Naja.CodeGen;
using Naja.CodeGen.Builtins;
using Naja.StdLib;

namespace Naja.CPythonTests.Infrastructure;

/// <summary>
/// Compiles and executes Python files through NajaEngine with full output capture.
///
/// Separates compile from execute so that compilation errors can be reported
/// independently of runtime failures. All Console.Out / Console.Error output
/// is captured and attached to the xUnit result — nothing escapes to the console.
///
/// Thread-safety: each call creates its own NajaEngine instance. The
/// <see cref="NajaUnittest.MethodFilter"/> thread-static ensures per-method
/// filtering is scoped to the calling thread.
/// </summary>
public sealed class NajaTestExecutor
{
    // ── Compile ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Compile a Python file to an in-memory assembly without executing it.
    /// Returns a <see cref="CompilationResult"/> that can be passed to <see cref="Run"/>.
    /// </summary>
    public CompilationResult Compile(string pythonFilePath)
    {
        if (!File.Exists(pythonFilePath))
            return CompilationResult.Fail($"File not found: {pythonFilePath}");

        var engine   = new NajaEngine();
        var assembly = engine.TryCompile(pythonFilePath, out var errors);

        return assembly is not null
            ? CompilationResult.Ok(assembly, Path.GetFileNameWithoutExtension(pythonFilePath))
            : CompilationResult.Fail(errors);
    }

    // ── Execute ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Execute a previously compiled assembly.
    /// Captures all stdout and stderr. If <paramref name="specificMethod"/> is
    /// non-null (format: "ClassName.method_name"), only that test method runs
    /// via <see cref="NajaUnittest.MethodFilter"/>.
    /// </summary>
    public ExecutionResult Run(
        CompilationResult compilation,
        string?           specificMethod,
        int               timeoutMs = 60_000)
    {
        if (!compilation.Success || compilation.Assembly is null)
        {
            return new ExecutionResult
            {
                ExitCode = -1,
                Stderr   = string.Join("\n", compilation.Errors),
            };
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var savedOut = Console.Out;
        var savedErr = Console.Error;

        try
        {
            Console.SetOut(new StringWriter(stdout));
            Console.SetError(new StringWriter(stderr));

            NajaUnittest.MethodFilter = specificMethod;

            var assembly     = compilation.Assembly;
            var assemblyName = compilation.AssemblyName;

            var entryType = assembly.GetType(assemblyName)
                         ?? assembly.GetType("NajaModule")
                         ?? throw new InvalidOperationException(
                                $"No entry type found in assembly '{assemblyName}'.");

            var entryMethod = entryType.GetMethod(
                "Main", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                       $"No static Main() found on '{entryType.FullName}'.");

            TypeSystem.SetCurrentAssembly(assembly);

            try
            {
                entryMethod.Invoke(null, null);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                // NajaUnittest.main() throws AssertionException on test failures —
                // that is the normal "some tests failed" path; exit code 1.
                // Other inner exceptions are unexpected runtime errors; exit code -1.
                bool isAssertionFailure =
                    tie.InnerException is Naja.StdLib.AssertionException;

                return new ExecutionResult
                {
                    ExitCode = isAssertionFailure ? 1 : -1,
                    Stdout   = stdout.ToString(),
                    Stderr   = stderr.ToString(),
                };
            }

            return new ExecutionResult
            {
                ExitCode = 0,
                Stdout   = stdout.ToString(),
                Stderr   = stderr.ToString(),
            };
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                ExitCode = -1,
                Stdout   = stdout.ToString(),
                Stderr   = stderr.ToString() + $"\n[EXECUTOR ERROR] {ex.GetType().Name}: {ex.Message}",
            };
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
            NajaUnittest.MethodFilter = null;
            TypeSystem.SetCurrentAssembly(null);
        }
    }

    // ── Convenience: compile + run in one call ────────────────────────────────

    /// <summary>
    /// Compile and run a Python file, returning a fully parsed
    /// <see cref="CPythonTestResult"/>. Used by Category 2 full-suite tests.
    /// </summary>
    public CPythonTestResult RunFile(string pythonFilePath, int timeoutMs = 60_000)
    {
        var compilation = Compile(pythonFilePath);
        if (!compilation.Success)
            return CPythonTestResult.CompilationFailure(compilation.Errors);

        var execution = Run(compilation, null, timeoutMs);
        return UnittestOutputParser.Parse(execution);
    }

    /// <summary>
    /// Compile and run a single test method from a Python file.
    /// Used by Category 3 granular tests.
    /// </summary>
    public CPythonTestResult RunMethod(
        string pythonFilePath,
        string className,
        string methodName,
        int    timeoutMs = 30_000)
    {
        var compilation = Compile(pythonFilePath);
        if (!compilation.Success)
            return CPythonTestResult.CompilationFailure(compilation.Errors);

        var execution = Run(compilation, $"{className}.{methodName}", timeoutMs);
        return UnittestOutputParser.Parse(execution);
    }

    /// <summary>
    /// Compile a Python file and return the compilation result without executing.
    /// Used by Category 4 stdlib compilation tests.
    /// </summary>
    public CompilationResult CompileOnly(string pythonFilePath) =>
        Compile(pythonFilePath);

    // ── Inline snippets ───────────────────────────────────────────────────────

    /// <summary>
    /// Write <paramref name="pythonSource"/> to a temp file, compile and run it.
    /// Used by Category 1 tests that write inline Python snippets.
    /// </summary>
    public CPythonTestResult RunInline(string pythonSource, int timeoutMs = 10_000)
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"naja_snippet_{Guid.NewGuid():N}.py");
        try
        {
            File.WriteAllText(tempPath, pythonSource);

            var compilation = Compile(tempPath);
            if (!compilation.Success)
                return CPythonTestResult.CompilationFailure(compilation.Errors);

            var execution = Run(compilation, null, timeoutMs);

            // Inline snippets may not call unittest.main() at all —
            // treat a clean exit (no exception) as passing.
            if (execution.ExitCode == 0)
                return CPythonTestResult.SnippetPassed();

            return UnittestOutputParser.Parse(execution);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

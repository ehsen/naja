using System;
using System.IO;
using System.Reflection;
using Naja.Parser;
using Naja.Semantics;
using Naja.Inference;

using NajaParserModule = Naja.Parser.Module;

namespace Naja.CodeGen;

/// <summary>
/// Scripting entry point for Naja.
///
/// Compiles a .naja source file to an in-memory assembly using
/// AssemblyBuilderAccess.Run (no disk writes) and immediately invokes
/// the entry point. The entire parse → semantic → IL → execute pipeline
/// runs in-process, making this the correct target for xUnit integration
/// tests — exceptions propagate directly into the test runner rather than
/// being lost in process exit codes.
///
/// Usage:
///   var engine = new NajaEngine();
///   engine.Eval("testdata/winforms/test_struct_boxing.naja");
///
/// On success  — returns normally.
/// On assert   — throws Exception (emitted by StatementEmitter.Emit(AssertStatement)).
/// On IL error — throws CodeGenException with line/column info.
/// On runtime  — throws whatever WinForms / .NET throws, unwrapped.
/// </summary>
public sealed class NajaEngine
{
    /// <summary>
    /// Compile a Python/Naja script to an in-memory assembly without executing it.
    /// Returns the compiled <see cref="Assembly"/> on success, or null on failure.
    /// <paramref name="errors"/> is populated with human-readable error messages on failure.
    /// </summary>
    public Assembly? TryCompile(string scriptPath, out List<string> errors,
        CompilationProfile profile = CompilationProfile.Console)
    {
        errors = new List<string>();

        if (!File.Exists(scriptPath))
        {
            errors.Add($"File not found: {scriptPath}");
            return null;
        }

        // PEP 263: decode via the BOM / coding declaration, strict UTF-8 default.
        var source = SourceDecoder.ReadFileText(scriptPath);

        NajaParserModule ast;
        try
        {
            var lexer  = new Naja.Lexer.Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Naja.Parser.Parser(tokens);
            ast = parser.ParseModule();
        }
        catch (Exception ex) when (ex is not CodeGenException)
        {
            errors.Add($"Syntax error in '{Path.GetFileName(scriptPath)}': {ex.Message}");
            return null;
        }

        var model = new SemanticAnalyzer().Analyze(ast);

        try
        {
            var ie = new TypeInferenceEngine(model);
            ie.Infer(ast);
        }
        catch { /* best-effort; continue without inference hints */ }

        var assemblyName = Path.GetFileNameWithoutExtension(scriptPath);
        var emitter      = new AssemblyEmitter(model, assemblyName);
        try
        {
            return emitter.EmitToMemory(ast, profile);
        }
        catch (CodeGenException ex)
        {
            errors.Add(ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            errors.Add($"IL emission failed in '{Path.GetFileName(scriptPath)}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Compile and execute a .naja script file in-memory.
    /// </summary>
    /// <param name="scriptPath">
    ///     Path to the .naja source file. Relative paths are resolved from
    ///     the current working directory (which xUnit sets to the test output
    ///     directory — the same place CopyToOutputDirectory puts testdata/).
    /// </param>
    /// <param name="profile">
    ///     Compilation profile. Defaults to auto-detect from imports, but
    ///     can be forced to WinForms for tests that need the WinForms preamble.
    /// </param>
    /// <param name="dumpIL">
    ///     If true, dumps the compiled IL to a text file in %TEMP% for debugging.
    /// </param>
    public void Eval(string scriptPath, CompilationProfile profile = CompilationProfile.Console, bool dumpIL = false)
    {
        // ── 1. Read source ────────────────────────────────────────────────────
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException(
                $"Naja script not found: {scriptPath}", scriptPath);

        // PEP 263: decode via the BOM / coding declaration, strict UTF-8 default.
        var source = SourceDecoder.ReadFileText(scriptPath);

        // ── 2. Parse ──────────────────────────────────────────────────────────
        NajaParserModule ast;
        try
        {
            var lexer = new Naja.Lexer.Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Naja.Parser.Parser(tokens);
            ast = parser.ParseModule();
        }
        catch (Exception ex) when (ex is not CodeGenException)
        {
            // Re-wrap parse errors so callers always see a CodeGenException
            // with the script name in the message for easy diagnosis.
            throw new CodeGenException(
                $"Syntax error in '{Path.GetFileName(scriptPath)}': {ex.Message}");
        }

        // ── 3. Semantic analysis ──────────────────────────────────────────────
        var semanticAnalyzer = new SemanticAnalyzer();
        var model = semanticAnalyzer.Analyze(ast);

        // ── 4. Optional inference pass ────────────────────────────────────────
        // The inference pass is best-effort; if it fails we continue without it
        // (same behaviour as the CLI in permissive mode).
        InferenceResult? inference = null;
        try
        {
            var inferenceEngine = new TypeInferenceEngine(model);
            inference = inferenceEngine.Infer(ast);
        }
        catch
        {
            // Inference failure is non-fatal — emit without inference hints.
        }

        // ── 5. Compile to in-memory assembly ──────────────────────────────────
        // Use the script filename (without extension) as the assembly name so
        // stack traces show a meaningful assembly name rather than "NajaScript".
        var assemblyName = Path.GetFileNameWithoutExtension(scriptPath);
        var emitter      = new AssemblyEmitter(model, assemblyName);

        Assembly assembly;
        try
        {
            // EmitToMemory uses AssemblyBuilderAccess.Run — no files written.

            assembly = emitter.EmitToMemory(ast,profile);
        }
        catch (CodeGenException)
        {
            throw; // already has line/col context — pass through as-is
        }
        catch (Exception ex)
        {
            throw new CodeGenException(
                $"IL emission failed in '{Path.GetFileName(scriptPath)}': {ex.Message}");
        }

        // ── 6. Locate entry point ─────────────────────────────────────────────
        // AssemblyEmitter emits a static Main() method on the NajaModule type.
        // We look it up by name rather than Assembly.EntryPoint because
        // AssemblyBuilder.DefineDynamicAssembly(Run) does not set EntryPoint.
        var entryType = assembly.GetType(assemblyName)          // module class name
                     ?? assembly.GetType("NajaModule")          // fallback legacy name
                     ?? throw new CodeGenException(
                            $"Could not find module type in compiled assembly for '{scriptPath}'.");

        var entryMethod = entryType.GetMethod(
            "Main",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new CodeGenException(
                   $"No static Main() found on '{entryType.FullName}' in '{scriptPath}'.");

        // Optionally dump IL for debugging before executing
        if (dumpIL)
        {
            try
            {
                var ilDumpPath = Path.Combine(Path.GetTempPath(), $"naja_il_{assemblyName}.txt");
                using (var sw = new System.IO.StreamWriter(ilDumpPath, false))
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        sw.WriteLine($"=== Type: {type.FullName} ===");
                        sw.WriteLine();
                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                        {
                            if (method.DeclaringType == type)  // skip inherited methods
                            {
                                sw.WriteLine(ILDumper.DumpMethodIL(method));
                                sw.WriteLine();
                            }
                        }
                    }
                }
                Console.WriteLine($"IL dump written to: {ilDumpPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: IL dump failed: {ex.Message}");
            }
        }

        // ── 7. Execute ────────────────────────────────────────────────────────
        // TargetInvocationException wraps any exception thrown by the script.
        // We unwrap it so xUnit sees the real exception type — in particular
        // the Exception thrown by failed assert statements, which the test
        // runner distinguishes from CodeGenException.

        Builtins.TypeSystem.SetCurrentAssembly(assembly);
        try
        {
            entryMethod.Invoke(null, null);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(tie.InnerException)
                .Throw();
        }
        finally
        {
            Builtins.TypeSystem.SetCurrentAssembly(null);
        }
    }
}

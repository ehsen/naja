using System.Diagnostics;
using System.Reflection;
using Naja.Lexer;
using Naja.Semantics;
using Naja.CodeGen;
using Naja.Parser;
//using Naja.Inference;
using NajaParser = Naja.Parser.Parser;
using NajaModule = Naja.Parser.Module;
using ProjectType = Naja.CodeGen.ProjectType;

namespace Naja.CLI;

public static class Commands
{
    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>
    /// naja compile file1.naja [file2.naja ...] [file.py ...] -o output.dll -t type
    /// Called directly by MSBuild via CoreCompile in Sdk.targets.
    /// Only job: Lex → Parse → Analyse → EmitToFile. Exit 0 or 1.
    /// Supports both native .naja syntax and Python .py files.
    /// </summary>
    public static int Compile(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: no input files specified");
            Console.Error.WriteLine("usage: naja compile <file.naja | file.py> [file2.naja ...] [-o output] [-t type]");
            return 1;
        }

        var opts = ParseCompileArgs(args);
        if (opts is null) return 1;

        return RunCompile(opts);
    }

    /// <summary>
    /// naja run &lt;file.naja | file.py&gt; [options]
    /// Compiles to memory and executes immediately in-process.
    /// Fast inner loop — no MSBuild involved.
    /// Supports both native .naja syntax and Python .py files.
    /// </summary>
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("error: no input file specified");
            Console.Error.WriteLine("usage: naja run <file.naja | file.py> [-v]");
            return 1;
        }

        var opts = ParseRunArgs(args);
        if (opts is null) return 1;

        return RunInMemory(opts.InputFile, opts.Verbose);
    }

    /// <summary>
    /// naja publish [file.naja | project.najaproj] [options]
    /// Compiles then hands off to `dotnet publish` for a self-contained
    /// single-file executable.
    /// </summary>
    public static int Publish(string[] args)
    {
        var opts = ParsePublishArgs(args);
        if (opts is null) return 1;

        return RunPublish(opts);
    }

    // ── compile ───────────────────────────────────────────────────────────────

    private static int RunCompile(CompileOptions opts)
    {
        // Join all source files into one compilation unit.
        // MSBuild passes them all at once via @(NajaCompile) in Sdk.targets.
        string source;
        try
        {
            source = string.Join("\n", opts.InputFiles.Select(File.ReadAllText));
        }
        catch (Exception ex)
        {
            PrintError($"Cannot read source file: {ex.Message}");
            return 1;
        }

        var asmName = Path.GetFileNameWithoutExtension(opts.OutputFile);

        if (!TryCompile(source, asmName, opts.Verbose, out var module, out var model, out _))
            return 1;

        // Print diagnostics in MSBuild-compatible format so errors appear
        // inline in Visual Studio, Rider, and `dotnet build` output.
        PrintDiagnostics(model, opts.InputFiles[0]);

        if (model.Diagnostics.HasErrors) return 1;

        try
        {
            var emitter = new AssemblyEmitter(model, asmName, opts.ProjectType, opts.Profile);
            emitter.EmitToFile(module, opts.OutputFile);
        }
        catch (Exception ex)
        {
            PrintError($"Code generation failed: {ex.Message}");
            if (opts.Verbose) Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }

        if (opts.Verbose)
        {
            PrintSuccess($"Emitted '{opts.OutputFile}'");
            Console.WriteLine($"  Symbols:  {model.ModuleSymbols.Count()} module-level");
            Console.WriteLine($"  Errors:   {model.Diagnostics.ErrorCount}");
            Console.WriteLine($"  Warnings: {model.Diagnostics.WarningCount}");
        }

        return 0;
    }

    // ── run ───────────────────────────────────────────────────────────────────

    private static int RunInMemory(string inputFile, bool verbose)
    {
        var sw = Stopwatch.StartNew();

        string source;
        try { source = File.ReadAllText(inputFile); }
        catch (Exception ex)
        {
            PrintError($"Cannot read '{inputFile}': {ex.Message}");
            return 1;
        }

        var asmName = Path.GetFileNameWithoutExtension(inputFile);

        if (!TryCompile(source, asmName, verbose, out var module, out var model, out _))
            return 1;

        if (model.Diagnostics.HasErrors)
        {
            PrintDiagnostics(model, inputFile);
            return 1;
        }

        if (model.Diagnostics.WarningCount > 0 && verbose)
            PrintDiagnostics(model, inputFile);

        try
        {
            // Detect only to give a useful error — not to change behavior.
            bool hasAspNet = module.Body.OfType<FromImportStatement>()
                .Any(s => s.Module.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
            if (hasAspNet)
            {
                PrintError(
                    "ASP.NET Core applications cannot be run with 'naja run' because they require " +
                    "the dotnet host and a properly initialized ASP.NET runtime environment.\n" +
                    "Use a .najaproj with <NajaProfile>web</NajaProfile> and run with 'dotnet run' instead.");
                return 1;
            }

            // naja run is for scripts and console/desktop apps.
            // ASP.NET Core cannot run in-process — it requires the dotnet host.
            // We do NOT detect profile from imports here. For ASP.NET projects,
            // users must use `dotnet run` with a .najaproj, not `naja run`.
            //
            // The only thing we decide here is that scripts executed via `naja run`
            // are always treated as Exe (entry point required) with Console profile
            // unless the user explicitly passes --profile in the future.
            // This matches `dotnet-script` and `csi.exe` behaviour.
            var emitter = new AssemblyEmitter(model, asmName, ProjectType.Exe, CompilationProfile.Console);
            var assembly = emitter.EmitToMemory(module, CompilationProfile.Console);

            sw.Stop();
            if (verbose) PrintInfo($"Compiled in {sw.ElapsedMilliseconds}ms — running...\n");

            var type = assembly.GetType(asmName)
                ?? throw new Exception($"Module type '{asmName}' not found in emitted assembly");
            var main = type.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
                ?? throw new Exception("No static Main() found — was the script compiled as Exe?");

            main.Invoke(null, null);
            return 0;
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException ?? tie;
            PrintError($"Runtime error: {inner.Message}");
            if (verbose) Console.Error.WriteLine(inner.StackTrace);
            return 1;
        }
        catch (Exception ex)
        {
            PrintError($"Execution failed: {ex.Message}");
            if (verbose) Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // ── publish ───────────────────────────────────────────────────────────────

    private static int RunPublish(PublishOptions opts)
    {
        var sw = Stopwatch.StartNew();
        var projDir = Path.GetDirectoryName(Path.GetFullPath(opts.InputFile))!;
        var asmName = Path.GetFileNameWithoutExtension(opts.InputFile);

        var source = opts.InputFile.EndsWith(".najaproj", StringComparison.OrdinalIgnoreCase)
            ? CollectSources(projDir)
            : ReadSource(opts.InputFile);

        if (source is null) return 1;

        if (!TryCompile(source, asmName, opts.Verbose, out var module, out var model, out _))
            return 1;

        PrintDiagnostics(model, opts.InputFile);
        if (model.Diagnostics.HasErrors) return 1;

        var tempDir = MakeTempDir("naja_publish_");
        try
        {
            var tempDll = Path.Combine(tempDir, asmName + ".dll");

            if (!TryEmit(model, module, asmName, opts.ProjectType, opts.Profile, opts.Verbose, tempDll))
                return 1;

            // Use the user's own .najaproj if available so PackageReferences survive.
            // For a bare .naja file, write a minimal temp project.
            string tempProj;
            if (opts.InputFile.EndsWith(".najaproj", StringComparison.OrdinalIgnoreCase))
            {
                tempProj = Path.Combine(tempDir, asmName + ".najaproj");
                File.Copy(opts.InputFile, tempProj);
            }
            else
            {
                tempProj = WriteTempProject(tempDir, asmName, opts.ProjectType, opts.Profile);
            }

            var outDir = opts.OutputDir
                ?? Path.Combine(projDir, "bin", "Release", "publish");
            Directory.CreateDirectory(outDir);

            var result = InvokeDotnet(
            [
                "publish", tempProj,
                "-c", "Release",
                "-o", outDir,
                "/p:SelfContained=true",
                $"/p:RuntimeIdentifier={opts.RuntimeIdentifier}",
                "/p:PublishSingleFile=true",
                "/p:IncludeNativeLibrariesForSelfExtract=true",
            ], opts.Verbose);

            sw.Stop();
            if (result == 0)
                PrintSuccess($"Published '{opts.InputFile}' → '{outDir}' in {sw.ElapsedMilliseconds}ms");

            return result;
        }
        finally { Cleanup(tempDir); }
    }

    // ── Naja compile pipeline ─────────────────────────────────────────────────

    private static bool TryCompile(
        string source,
        string asmName,
        bool verbose,
        out NajaModule module,
        out Naja.Semantics.SemanticModel model,
        out long elapsedMs)
    {
        module = null!;
        model = null!;
        elapsedMs = 0;
        var sw = Stopwatch.StartNew();

        List<Token> tokens;
        try
        {
            tokens = (List<Token>)new Naja.Lexer.Lexer(source).Tokenize();
            if (verbose) PrintStage("Lex", sw.ElapsedMilliseconds);
        }
        catch (LexerException ex)
        {
            PrintError($"Lexer error: {ex.Message}");
            return false;
        }

        try
        {
            module = new NajaParser(tokens).ParseModule();
            if (verbose) PrintStage("Parse", sw.ElapsedMilliseconds);
        }
        catch (Naja.Parser.ParseException ex)
        {
            PrintError($"Parse error: {ex.Message}");
            return false;
        }

        model = new SemanticAnalyzer().Analyze(module);
        elapsedMs = sw.ElapsedMilliseconds;
        if (verbose) PrintStage("Analyse", elapsedMs);

        return true;
    }

    private static bool TryEmit(
        Naja.Semantics.SemanticModel model,
        NajaModule module,
        string asmName,
        ProjectType projectType,
        CompilationProfile profile,
        bool verbose,
        string outputPath)
    {
        try
        {
            var emitter = new AssemblyEmitter(model, asmName, projectType, profile);
            emitter.EmitToFile(module, outputPath);
            if (verbose) PrintStage("Emit", 0);
            return true;
        }
        catch (Exception ex)
        {
            PrintError($"Code generation failed: {ex.Message}");
            if (verbose) Console.Error.WriteLine(ex.StackTrace);
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? CollectSources(string projDir)
    {
        var files = Directory.GetFiles(projDir, "*.naja", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            PrintError($"No .naja source files found in '{projDir}'");
            return null;
        }
        return string.Join("\n", files.Select(File.ReadAllText));
    }

    private static string? ReadSource(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex)
        {
            PrintError($"Cannot read '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes a minimal .najaproj for publish when the user only has a bare
    /// .naja file. MSBuild uses it for OutputType and TargetFramework.
    /// </summary>
    private static string WriteTempProject(
        string tempDir,
        string asmName,
        ProjectType projectType,
        CompilationProfile profile)
    {
        var outputType = projectType switch
        {
            ProjectType.Exe => "Exe",
            ProjectType.WinExe => "WinExe",
            _ => "Library"
        };

        // Select the correct TFM for each profile.
        // -windows suffix is ONLY for WinForms/WPF — it activates Microsoft.WindowsDesktop.App.
        // Console and Web use plain net10.0.
        var tfm = profile switch
        {
            CompilationProfile.WinForms => "net10.0-windows",
            CompilationProfile.Wpf => "net10.0-windows",
            _ => "net10.0"
        };

        // WinForms needs <UseWindowsForms> so MSBuild adds the WinForms references.
        var winForms = profile == CompilationProfile.WinForms
            ? "\n    <UseWindowsForms>true</UseWindowsForms>" : string.Empty;

        var wpf = profile == CompilationProfile.Wpf
            ? "\n    <UseWPF>true</UseWPF>" : string.Empty;

        // ASP.NET Core needs a FrameworkReference — this is how Microsoft.NET.Sdk.Web does it.
        // Without this, dotnet publish will not include the ASP.NET shared framework.
        var aspNetRef = profile == CompilationProfile.AspNetCore
            ? """
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
              """
            : string.Empty;

        var xml = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>{tfm}</TargetFramework>
                <AssemblyName>{asmName}</AssemblyName>{winForms}{wpf}
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <EnableDefaultItems>false</EnableDefaultItems>
              </PropertyGroup>
              {aspNetRef}
              <ItemGroup>
                <Content Include="{asmName}.dll">
                  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
                </Content>
              </ItemGroup>
              <Target Name="CoreCompile" />
              <Target Name="CreateManifestResourceNames" />
            </Project>
            """;

        var path = Path.Combine(tempDir, asmName + ".najaproj");
        File.WriteAllText(path, xml);
        return path;
    }

    private static int InvokeDotnet(string[] arguments, bool verbose)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = !verbose,
            RedirectStandardError = !verbose,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        var proc = Process.Start(psi)!;
        proc.WaitForExit();

        if (proc.ExitCode != 0 && !verbose)
            PrintError("MSBuild step failed — re-run with -v for details");

        return proc.ExitCode;
    }

    private static string MakeTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Argument parsing ──────────────────────────────────────────────────────

    private record CompileOptions(
        List<string> InputFiles,
        string OutputFile,
        ProjectType ProjectType,
        CompilationProfile Profile,
        string Configuration,
        bool Verbose);

    private record RunOptions(string InputFile, bool Verbose);

    private record PublishOptions(
        string InputFile,
        string? OutputDir,
        ProjectType ProjectType,
        CompilationProfile Profile,
        string RuntimeIdentifier,
        bool Verbose);

    private static CompileOptions? ParseCompileArgs(string[] args)
    {
        var inputs = new List<string>();
        string? output = null;
        bool verbose = false;
        var projectType = ProjectType.Library;
        var profile = CompilationProfile.Console;
        var config = "Debug";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--output":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("error: -o requires a value"); return null; }
                    output = args[++i];
                    break;
                case "-t" or "--type":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("error: -t requires a value"); return null; }
                    projectType = ParseProjectType(args[++i]);
                    if (projectType == (ProjectType)(-1)) return null;
                    break;
                case "--profile":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("error: --profile requires a value"); return null; }
                    profile = ParseProfile(args[++i]);
                    if (profile == (CompilationProfile)(-1)) return null;
                    break;
                case "-c" or "--configuration":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("error: -c requires a value"); return null; }
                    config = args[++i];
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    // Accept both .naja and .py files
                    var isValidFile = args[i].EndsWith(".naja", StringComparison.OrdinalIgnoreCase) ||
                                      args[i].EndsWith(".py", StringComparison.OrdinalIgnoreCase);
                    if (isValidFile)
                        inputs.Add(args[i]);
                    else
                    { Console.Error.WriteLine($"error: unexpected argument '{args[i]}' (expected .naja or .py file)"); return null; }
                    break;
            }
        }

        if (inputs.Count == 0) { Console.Error.WriteLine("error: no input files"); return null; }

        foreach (var f in inputs)
            if (!File.Exists(f))
            { Console.Error.WriteLine($"error: file not found: '{f}'"); return null; }

        output ??= projectType switch
        {
            ProjectType.Exe or ProjectType.WinExe => Path.ChangeExtension(inputs[0], ".exe"),
            _ => Path.ChangeExtension(inputs[0], ".dll")
        };

        return new CompileOptions(inputs, output, projectType, profile, config, verbose);
    }

    private static RunOptions? ParseRunArgs(string[] args)
    {
        string? input = null;
        bool verbose = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    if (input is not null) { Console.Error.WriteLine($"error: unexpected argument '{args[i]}'"); return null; }
                    input = args[i];
                    break;
            }
        }

        if (input is null) { Console.Error.WriteLine("error: no input file"); return null; }
        if (!File.Exists(input)) { Console.Error.WriteLine($"error: file not found: '{input}'"); return null; }

        // Accept both .naja and .py files
        var isValidExtension = input.EndsWith(".naja", StringComparison.OrdinalIgnoreCase) ||
                               input.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
        if (!isValidExtension) 
        { 
            Console.Error.WriteLine($"error: file '{input}' must be .naja or .py");
            return null;
        }

        return new RunOptions(input, verbose);
    }

    private static PublishOptions? ParsePublishArgs(string[] args)
    {
        string? input = null, outputDir = null;
        var rid = "win-x64";
        bool verbose = false;
        var projectType = ProjectType.Exe;
        var profile = CompilationProfile.Console;

        if (args.Length == 0)
        {
            input = Directory.GetFiles(".", "*.najaproj").FirstOrDefault();
            if (input is null) { Console.Error.WriteLine("error: no .najaproj found in current directory"); return null; }
            return new PublishOptions(input, null, projectType, CompilationProfile.Console, rid, verbose);
        }

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--output":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("error: -o requires a value"); return null; }
                    outputDir = args[++i];
                    break;
                case "-t" or "--type":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("error: -t requires a value"); return null; }
                    projectType = ParseProjectType(args[++i]);
                    if (projectType == (ProjectType)(-1)) return null;
                    break;
                case "--profile":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("error: --profile requires a value"); return null; }
                    profile = ParseProfile(args[++i]);
                    if (profile == (CompilationProfile)(-1)) return null;
                    break;
                case "-r" or "--runtime":
                    if (i + 1 >= args.Length) { Console.Error.WriteLine("error: -r requires a value"); return null; }
                    rid = args[++i];
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    if (input is not null) { Console.Error.WriteLine($"error: unexpected argument '{args[i]}'"); return null; }
                    input = args[i];
                    break;
            }
        }

        input ??= Directory.GetFiles(".", "*.najaproj").FirstOrDefault();
        if (input is null) { Console.Error.WriteLine("error: no input file or .najaproj found"); return null; }
        if (!File.Exists(input)) { Console.Error.WriteLine($"error: file not found: '{input}'"); return null; }

        return new PublishOptions(input, outputDir, projectType, profile, rid, verbose);
    }

    private static ProjectType ParseProjectType(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "exe": return ProjectType.Exe;
            case "winexe": return ProjectType.WinExe;
            case "library": return ProjectType.Library;
            default:
                Console.Error.WriteLine($"error: unknown type '{value}'. Valid: exe, winexe, library");
                return (ProjectType)(-1);
        }
    }

    private static CompilationProfile ParseProfile(string value) =>
        value.ToLowerInvariant() switch
        {
            "console"  => CompilationProfile.Console,
            "winforms" => CompilationProfile.WinForms,
            "wpf"      => CompilationProfile.Wpf,
            "web"      => CompilationProfile.AspNetCore,
            _ => (CompilationProfile)(-1)   // sentinel for parse failure
        };

    // ── Console output ────────────────────────────────────────────────────────

    private static void PrintStage(string stage, long ms)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [{stage}] {ms}ms");
        Console.ResetColor();
    }

    private static void PrintSuccess(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ {msg}");
        Console.ResetColor();
    }

    internal static void PrintError(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"✗ {msg}");
        Console.ResetColor();
    }

    private static void PrintInfo(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(msg);
        Console.ResetColor();
    }

    /// <summary>
    /// Prints diagnostics in MSBuild-compatible format:
    ///   filename.naja(line,col): error NAJA001: message
    /// This makes errors appear inline in Visual Studio, Rider, and dotnet build output.
    /// </summary>
    private static void PrintDiagnostics(Naja.Semantics.SemanticModel model, string sourceFile)
    {
        foreach (var d in model.Diagnostics.All)
        {
            var severity = d.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            var code = d.Severity == DiagnosticSeverity.Error ? "NAJA001" : "NAJA002";
            var writer = d.Severity == DiagnosticSeverity.Error ? Console.Error : Console.Out;

            Console.ForegroundColor = d.Severity == DiagnosticSeverity.Error
                ? ConsoleColor.Red : ConsoleColor.Yellow;

            writer.WriteLine($"{sourceFile}({d.Line},{d.Column}): {severity} {code}: {d.Message}");

            Console.ResetColor();
        }
    }
}
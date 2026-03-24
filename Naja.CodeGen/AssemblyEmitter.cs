using Naja.CodeGen;
using Naja.Inference;
using Naja.Parser;
using Naja.Semantics;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using NajaParserModule = Naja.Parser.Module;
using SysModule = System.Reflection.Module;

namespace Naja.CodeGen;

/// <summary>
/// Describes which .NET runtime framework the output requires.
/// Inferred from imports when compiling a single .naja file, or driven
/// by .najaproj OutputType + TargetFramework when a project file is present.
/// </summary>
public enum CompilationProfile
{
    /// <summary>Microsoft.NETCore.App — console or background app.</summary>
    Console,

    /// <summary>Microsoft.WindowsDesktop.App — System.Windows.Forms app.</summary>
    WinForms,

    /// <summary>Microsoft.WindowsDesktop.App — System.Windows (WPF) app.</summary>
    Wpf,

    /// <summary>Microsoft.AspNetCore.App — ASP.NET Core web app.</summary>
    AspNetCore,
}

/// <summary>
/// PE output type — mirrors .najaproj / .csproj OutputType values.
/// </summary>
public enum ProjectType
{
    Library,
    Exe,
    WinExe,
}

/// <summary>
/// Top-level IL emitter.
/// Takes a parsed + analysed module and produces a .NET 10 PE assembly.
///
/// Assembly structure:
///   output.exe / output.dll
///     └── <AssemblyName>  (static class — TypeAttributes.Abstract | Sealed)
///           ├── static fields   (module-level variables)
///           ├── static methods  (module-level functions)
///           └── static Main()   (entry point — runs module body)
///     └── UserClass1, UserClass2 ...  (one TypeBuilder per Python class)
///
/// Compilation is three-pass per module:
///   Pass 1 — declare stubs (fields, method signatures, class TypeBuilders)
///   Pass 2 — emit Main() body  (statement walker, skips FunctionDef/ClassDef)
///   Pass 3 — emit function bodies and class bodies
///
/// The three-pass structure is mandatory because Python allows forward references:
/// a class can be used before its definition in the file.  Pass 1 fills all
/// lookup dictionaries so EmitName never sees an undefined identifier.
/// </summary>
public sealed partial class AssemblyEmitter
{
    private readonly SemanticModel _model;
    private readonly string _assemblyName;
    private readonly ProjectType _projectType;

    // Instance-level registries — populated during Pass 1, read by EmitToFile
    private readonly Dictionary<string, TypeBuilder> _classTypes = new();
    private readonly Dictionary<string, ConstructorBuilder> _classConstructors = new();
    private readonly Dictionary<string, int> _classCtorArgCounts = new();
    private readonly Dictionary<string, MethodBuilder> _classMethods = new();
    private readonly Dictionary<string, Type[]> _classMethodParamTypes = new();
    private readonly HashSet<string> _classMethodNames = new();

    // Nested class support: maps unique internal name → ClassDef AST node.
    // Unique name format: "{cls.Name}_L{cls.Line}" to avoid collisions when the same
    // class name appears in multiple function bodies.  Populated by Pass 1 scanning.
    private readonly Dictionary<string, ClassDef> _nestedClassDefs = new();

    // Methods/types declared inside function bodies (e.g. nested defs like `make_decorator`).
    // Nested class bodies are compiled in Pass 3 with only module-level dicts; these
    // supplemental dicts carry inner-function declarations so class methods can resolve them.
    private readonly Dictionary<string, MethodBuilder> _innerFunctionMethods = new();
    private readonly Dictionary<string, TypeBuilder> _innerFunctionClassTypes = new();
    private readonly Dictionary<string, ConstructorBuilder> _innerFunctionClassCtors = new();
    private readonly Dictionary<string, FieldBuilder> _innerFunctionFields = new();

    // Deferred constructor completion.
    // DeclareClass (Pass 1) emits only the base ctor call and leaves the
    // ILGenerator open. EmitClassBody (Pass 3) completes it with the
    // __init__ call + Ret once the MethodBuilder is available from Pass 1.5.
    // Key = class name. Value = (ILGenerator, argCount).
    private readonly Dictionary<string, (ILGenerator IL, int ArgCount)> _pendingCtorIL = new();

    // Stores FieldBuilders for class-level static variables (x = 42 at class scope).
    // Populated by Pass 1.5, used by Pass 3 (.cctor emission).
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _classStaticFieldBuilders = new();

    // Stashed by EmitModule so EmitToFile can wire it as PE entry point.
    // Must be a MethodBuilder — PersistedAssemblyBuilder only accepts the handle form.
    private MethodBuilder? _mainMethod;

    // Names of Application.* methods already emitted in the WinForms preamble.
    // Passed into EmitContext so StatementEmitter can skip re-emitting them.
    private readonly HashSet<string> _winFormsPreambleEmitted = new();

    // ── Framework type resolver ───────────────────────────────────────────────
    // Resolves CLR types from disk-based reference assemblies instead of the
    // live AppDomain.  This mirrors what Roslyn does: types are looked up via
    // their PE metadata, so the compiler process does NOT need to have the target
    // framework loaded.  This fixes ASP.NET Core type resolution entirely.
    private static readonly FrameworkTypeResolver _typeResolver = new();

    public AssemblyEmitter(
        SemanticModel model,
        string assemblyName,
        ProjectType projectType = ProjectType.Exe,
        CompilationProfile console = default)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _assemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
        _projectType = projectType;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Compile the module and write a PE file (.exe or .dll) plus sidecar files
    /// (runtimeconfig.json, deps.json) to <paramref name="outputPath"/>.
    /// </summary>
    public void EmitToFile(NajaParserModule najaModule, string outputPath)
    {
        // ── 1. Detect compilation profile ────────────────────────────────────
        var profile = DetectProfile(najaModule);
        bool isGui = profile is CompilationProfile.WinForms or CompilationProfile.Wpf;
        bool needsExe = isGui
                     || _projectType == ProjectType.Exe
                     || _projectType == ProjectType.WinExe;

        if (needsExe && !outputPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            outputPath = Path.ChangeExtension(outputPath, ".exe");

        // ── 2. PE header ──────────────────────────────────────────────────────
        // WindowsGui suppresses the console window for WinForms/WPF.
        var peHeader = new PEHeaderBuilder(
            imageCharacteristics: Characteristics.ExecutableImage,
            subsystem: isGui ? Subsystem.WindowsGui : Subsystem.WindowsCui);

        // ── 3. PersistedAssemblyBuilder — the correct .NET 9+/10 save API ────
        // AssemblyBuilder.DefineDynamicAssembly(RunAndSave) does NOT exist on
        // .NET Core.  PersistedAssemblyBuilder is the only correct path.
        var asmName = new AssemblyName(_assemblyName);
        var asmBuilder = new PersistedAssemblyBuilder(asmName, typeof(object).Assembly);
        var modBuilder = asmBuilder.DefineDynamicModule(_assemblyName);

        // Reset per-compilation state
        _mainMethod = null;
        _winFormsPreambleEmitted.Clear();
        _classTypes.Clear();
        _classConstructors.Clear();
        _classMethodNames.Clear();
        _pendingCtorIL.Clear();
        _nestedClassDefs.Clear();
        

        // ── 4. Emit all IL ────────────────────────────────────────────────────
        var typeBuilder = EmitModule(najaModule, modBuilder, profile);

        // Finalise all user-defined class TypeBuilders FIRST
        foreach (var ct in _classTypes.Values)
        {
            try { if (!ct.IsCreated()) ct.CreateType(); }
            catch { /* already created or circular — skip */ }
        }

        // Bake the main module type (containing Main)
        typeBuilder.CreateType();



        // ── 5. Apply [STAThread] to Main() for WinForms/WPF ──────────────────
        // Application.Run() on .NET 10 requires the calling thread to be STA.
        // Without this attribute the runtime throws at Application.Run().
        if (_mainMethod != null && isGui)
        {
            var staCtor = typeof(STAThreadAttribute).GetConstructor(Type.EmptyTypes)!;
            _mainMethod.SetCustomAttribute(new CustomAttributeBuilder(staCtor, []));
        }

        // ── 6. Serialise to PE using extracted helper ─────────────────────────
        AssemblyPEWriter.SerializeToFile(asmBuilder, _mainMethod, needsExe, peHeader, profile, outputPath);
    }

    /// <summary>
    /// Emit to an in-memory assembly for testing / REPL use.
    ///
    /// Uses PersistedAssemblyBuilder (same path as EmitToFile) so that
    /// type-creation ordering constraints do not apply — the CLR resolves
    /// all forward references at the final Write step, not incrementally.
    /// The resulting bytes are loaded via Assembly.Load so NajaEngine
    /// receives a live Assembly exactly as before.
    /// </summary>
    public Assembly EmitToMemory(NajaParserModule najaModule, CompilationProfile profile)
    {
        var asmName = new AssemblyName(_assemblyName);
        var asmBuilder = new PersistedAssemblyBuilder(asmName, typeof(object).Assembly);
        var modBuilder = asmBuilder.DefineDynamicModule(_assemblyName);

        _mainMethod = null;
        _winFormsPreambleEmitted.Clear();
        _classTypes.Clear();
        _classConstructors.Clear();
        _classMethodNames.Clear();
        _pendingCtorIL.Clear();

        var typeBuilder = EmitModule(najaModule, modBuilder, profile);

        // Finalise all class types then the module type
        foreach (var ct in _classTypes.Values)
            try { if (!ct.IsCreated()) ct.CreateType(); } catch { }
        typeBuilder.CreateType();

        // Serialise to PE bytes and load as a live Assembly —
        // NajaEngine sees no difference from the old AssemblyBuilder.Run path.
        var metaBuilder = asmBuilder.GenerateMetadata(
            out BlobBuilder ilStream,
            out BlobBuilder fieldData);

        var peHeader = new PEHeaderBuilder(
            imageCharacteristics: Characteristics.ExecutableImage);
        var peBuilder = new ManagedPEBuilder(
            header: peHeader,
            metadataRootBuilder: new MetadataRootBuilder(metaBuilder),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            flags: CorFlags.ILOnly);

        var peBlob = new BlobBuilder();
        peBuilder.Serialize(peBlob);

        return Assembly.Load(peBlob.ToArray());
    }

    // ── Profile detection ─────────────────────────────────────────────────────

    private static CompilationProfile DetectProfile(NajaParserModule module)
    {
        foreach (var stmt in module.Body)
        {
            if (stmt is FromImportStatement imp)
            {
                if (imp.Module.Contains("Windows.Forms", StringComparison.OrdinalIgnoreCase))
                    return CompilationProfile.WinForms;
                if (imp.Module.StartsWith("System.Windows", StringComparison.OrdinalIgnoreCase)
                    && !imp.Module.Contains("Forms", StringComparison.OrdinalIgnoreCase))
                    return CompilationProfile.Wpf;
                if (imp.Module.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
                    return CompilationProfile.AspNetCore;
            }
            if (stmt is ImportStatement plain)
            {
                foreach (var alias in plain.Names)
                {
                    if (alias.Name.Contains("Windows.Forms", StringComparison.OrdinalIgnoreCase))
                        return CompilationProfile.WinForms;
                    if (alias.Name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
                        return CompilationProfile.AspNetCore;
                }
            }
        }
        return CompilationProfile.Console;
    }
    }

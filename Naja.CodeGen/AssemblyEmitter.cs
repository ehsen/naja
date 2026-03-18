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
public sealed class AssemblyEmitter
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

    // Deferred constructor completion.
    // DeclareClass (Pass 1) emits only the base ctor call and leaves the
    // ILGenerator open. EmitClassBody (Pass 3) completes it with the
    // __init__ call + Ret once the MethodBuilder is available from Pass 1.5.
    // Key = class name. Value = (ILGenerator, argCount).
    private readonly Dictionary<string, (ILGenerator IL, int ArgCount)> _pendingCtorIL = new();

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

        // ── 6. Serialise to PE using ManagedPEBuilder ─────────────────────────
        // PersistedAssemblyBuilder.Save() does NOT accept an entry point — the
        // entry point MUST go through GenerateMetadata → ManagedPEBuilder.
        var metaBuilder = asmBuilder.GenerateMetadata(
            out BlobBuilder ilStream,
            out BlobBuilder fieldData);

        MethodDefinitionHandle entryHandle = default;
        if (_mainMethod != null && needsExe)
            entryHandle = MetadataTokens.MethodDefinitionHandle(_mainMethod.MetadataToken);

        var peBuilder = new ManagedPEBuilder(
            header: peHeader,
            metadataRootBuilder: new MetadataRootBuilder(metaBuilder),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            entryPoint: entryHandle,
            flags: CorFlags.ILOnly);

        var peBlob = new BlobBuilder();
        peBuilder.Serialize(peBlob);

        using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            peBlob.WriteContentTo(fs);

        // ── 7. Write sidecar files ────────────────────────────────────────────
        WriteRuntimeConfig(outputPath, profile);
        WriteDepsJson(outputPath);
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

    // ── runtimeconfig.json ────────────────────────────────────────────────────

    private static void WriteRuntimeConfig(string assemblyPath, CompilationProfile profile)
    {
        // Determine framework name and TFM based on profile
        string frameworkName;
        string tfm;

        switch (profile)
        {
            case CompilationProfile.WinForms:
            case CompilationProfile.Wpf:
                // CRITICAL: WinForms/WPF MUST use "Microsoft.WindowsDesktop.App".
                // Using "Microsoft.NETCore.App" means System.Windows.Forms.dll is never
                // loaded by the dotnet host and the process crashes before Main() runs.
                frameworkName = "Microsoft.WindowsDesktop.App";
                tfm = "net10.0-windows";
                break;
            case CompilationProfile.AspNetCore:
                // ASP.NET Core apps need Microsoft.AspNetCore.App framework
                frameworkName = "Microsoft.AspNetCore.App";
                tfm = "net10.0";
                break;
            default:
                frameworkName = "Microsoft.NETCore.App";
                tfm = "net10.0";
                break;
        }

        var config = new
        {
            runtimeOptions = new
            {
                tfm = tfm,
                framework = new
                {
                    name = frameworkName,
                    version = "10.0.0"
                },
                configProperties = new Dictionary<string, object>
                {
                    ["System.Runtime.Loader.UseRidGraph"] = false
                }
            }
        };

        var path = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
        File.WriteAllText(path,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── deps.json ─────────────────────────────────────────────────────────────

    private static void WriteDepsJson(string assemblyPath)
    {
        // Dynamically get the name and version of the Naja.CodeGen assembly
        var najaAsm = typeof(NajaBuiltins).Assembly.GetName();
        var najaName = najaAsm.Name ?? "Naja.CodeGen";
        var najaVersion = najaAsm.Version?.ToString() ?? "1.0.0.0";
        var najaTarget = $"{najaName}/{najaVersion}";

        var deps = new
        {
            runtimeTarget = new { name = ".NETCoreApp,Version=v10.0", signature = "" },
            targets = new Dictionary<string, object>
            {
                [".NETCoreApp,Version=v10.0"] = new Dictionary<string, object>
                {
                    // Explicitly tell the .NET Host it is allowed to load Naja.CodeGen.dll
                    // from the application directory.
                    [najaTarget] = new
                    {
                        runtime = new Dictionary<string, object>
                        {
                            [$"{najaName}.dll"] = new { }
                        }
                    }
                }
            },
            libraries = new Dictionary<string, object>
            {
                [najaTarget] = new
                {
                    type = "project",
                    serviceable = false,
                    sha512 = ""
                }
            }
        };

        var path = Path.ChangeExtension(assemblyPath, ".deps.json");
        File.WriteAllText(path,
            JsonSerializer.Serialize(deps, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── EmitModule (three-pass) ───────────────────────────────────────────────

    private TypeBuilder EmitModule(
        NajaParserModule module,
        ModuleBuilder modBuilder,
        CompilationProfile profile)
    {
        // Use assembly name as the module type name so entry point resolution works correctly.
        // The type name must match the assembly name for proper entry point lookup.
        var typeBuilder = modBuilder.DefineType(
            _assemblyName,
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);

        // ── Build import map ──────────────────────────────────────────────────
        // Must happen before Pass 1 because DeclareClass needs it to resolve base types.
        // We search AppDomain loaded assemblies first so strong-named WinForms types
        // (e.g. System.Windows.Forms.Form) resolve correctly without guessing AQNs.
        var importMap = new Dictionary<string, (string TypeName, string AssemblyName)>();
        var namespaceImports = new Dictionary<string, string>();
        foreach (var stmt in module.Body)
        {
            if (stmt is FromImportStatement fis)
            {
                foreach (var alias in fis.Names)
                {
                    var localName = alias.Alias ?? alias.Name;
                    // Handle both "from System import X" and "from System.X import Y"
                    var typeName = fis.Module.Contains('.')
                        ? fis.Module + "." + alias.Name
                        : fis.Module + "." + alias.Name;

                    // Resolve the assembly that owns this type.
                    // Strategy (mirrors Roslyn): ask the disk-based FrameworkTypeResolver
                    // first so the compiler process never needs the target framework loaded.
                    // Fall back to AppDomain only for types the host itself has loaded
                    // (e.g. mscorlib / System.Private.CoreLib primitives).
                    string asmShortName = _typeResolver.ResolveAssemblyName(typeName)
                        ?? AppDomain.CurrentDomain.GetAssemblies()
                               .Select(a => { try { return a.GetType(typeName, false, true); } catch { return null; } })
                               .FirstOrDefault(t => t is not null)
                               ?.Assembly.GetName().Name
                        ?? fis.Module;   // last-resort: use the namespace itself

                    importMap[localName] = (typeName, asmShortName);
                }
            }
            else if (stmt is ImportStatement imp)
            {
                // Handle "import System" style namespace imports
                // We don't scan assemblies here - types are resolved on-demand at access time
                foreach (var alias in imp.Names)
                {
                    var nsName = alias.Alias ?? alias.Name;
                    // Store the namespace import with null assembly - we'll search for the type at access time
                    namespaceImports[nsName] = "";
                }
            }
        }

        // ── Pass 1: declare ALL stubs before emitting any IL ──────────────────
        // EmitName searches ctx.Fields / ctx.Methods / ctx.ClassTypes at emit time.
        // If a name is used before its definition in the file (forward reference,
        // or simply inside "if __name__ == '__main__':") and the stub is not here,
        // EmitName throws "Undefined name".  Pass 1 prevents that entirely.
        var fields = new Dictionary<string, FieldBuilder>();
        var methods = new Dictionary<string, MethodBuilder>();
        var paramTypes = new Dictionary<string, Type[]>();
        var classTypes = new Dictionary<string, TypeBuilder>();
        var classCtors = new Dictionary<string, ConstructorBuilder>();

        foreach (var stmt in module.Body)
        {
            switch (stmt)
            {
                case FunctionDef fn:
                    {
                        var (mb, pts) = DeclareMethod(fn, typeBuilder);
                        methods[fn.Name] = mb;
                        paramTypes[fn.Name] = pts;
                        break;
                    }
                case ClassDef cls:
                    {
                        var ct = DeclareClass(cls, modBuilder, importMap, module.Body, out var ctor);
                        classTypes[cls.Name] = ct;
                        classCtors[cls.Name] = ctor;
                        _classTypes[cls.Name] = ct;
                        _classConstructors[cls.Name] = ctor;

                        // Store parameter count for later inheritance lookups
                        var initFn = cls.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
                        int ac = 0;
                        if (initFn != null)
                        {
                            bool hasSelf = initFn.Params.Count > 0 && initFn.Params[0].Name is "self" or "cls";
                            ac = hasSelf ? initFn.Params.Count - 1 : initFn.Params.Count;
                        }
                        _classCtorArgCounts[cls.Name] = ac;

                        // PASS 1.5: Handle class-level attributes like __match_args__
                        foreach (var member in cls.Body)
                        {
                            if (member is FunctionDef cfn)
                            {
                                _classMethodNames.Add($"{cls.Name}.{cfn.Name}");
                            }
                            else if (member is AssignStatement cas)
                            {
                                foreach (var target in cas.Targets)
                                {
                                    if (target is NameExpr cn)
                                    {
                                        // Emit as public static field on the class
                                        ct.DefineField(cn.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static);
                                    }
                                }
                            }
                             else if (member is AnnAssignStatement ann && ann.Target is NameExpr an)
                            {
                                // Handle class-level annotated assignments
                                // typing.Final fields should be instance fields with InitOnly
                                bool isFinal =
                                    ann.Annotation is NameExpr { Name: "Final" }
                                    || (ann.Annotation is AttributeExpr fa
                                        && fa.Attribute == "Final"
                                        && fa.Object is NameExpr { Name: "typing" })
                                    || (ann.Annotation is SubscriptExpr sub
                                        && (sub.Object is NameExpr { Name: "Final" }
                                            || (sub.Object is AttributeExpr sa
                                                && sa.Attribute == "Final"
                                                && sa.Object is NameExpr { Name: "typing" })));

                                if (isFinal)
                                {
                                    // typing.Final fields are instance fields with InitOnly flag
                                    var fbAttrs = FieldAttributes.Public | FieldAttributes.InitOnly;
                                    ct.DefineField(an.Name, typeof(object), fbAttrs);
                                }
                                else
                                {
                                    // Non-Final class variables are static
                                    var fbAttrs = FieldAttributes.Public | FieldAttributes.Static;
                                    ct.DefineField(an.Name, typeof(object), fbAttrs);
                                }
                            }
                        }
                        break;
                    }
                case AssignStatement assign:
                    {
                        foreach (var target in assign.Targets)
                        {
                            if (target is NameExpr n && !fields.ContainsKey(n.Name))
                            {
                                var sym = _model.ModuleScope.Lookup(n.Name);
                                var clrT = sym is not null
                                    ? TypeMapper.ToClrType(sym.Type)
                                    : typeof(object);
                                if (clrT == typeof(void)) clrT = typeof(object);

                                fields[n.Name] = typeBuilder.DefineField(
                                    n.Name, clrT,
                                    FieldAttributes.Public | FieldAttributes.Static);
                            }
                        }
                        break;
                    }
                case AnnAssignStatement ann when ann.Target is NameExpr an:
                    {
                        if (!fields.ContainsKey(an.Name))
                        {
                            var clrT = typeof(object);
                            if (ann.Annotation is NameExpr annot)
                                clrT = TypeMapper.ToClrType(NajaTypes.FromAnnotation(annot.Name))
                                    ?? typeof(object);
                            fields[an.Name] = typeBuilder.DefineField(
                                an.Name, clrT,
                                FieldAttributes.Public | FieldAttributes.Static);
                        }
                        break;
                    }
            }
        }

        // ── Pass 1.5: declare ALL class methods ───────────────────────────────
        foreach (var stmt in module.Body)
        {
            if (stmt is ClassDef cls && classTypes.TryGetValue(cls.Name, out var ct))
            {
                foreach (var member in cls.Body)
                {
                    if (member is not FunctionDef fnd) continue;

                    var (mb, pts, uniqueName) = DeclareInstanceMethod(fnd, ct);
                    string key = $"{cls.Name}.{uniqueName}";
                    _classMethods[key] = mb;
                    _classMethodParamTypes[key] = pts;
                    _classMethodNames.Add(key);
                }
            }
        }


        // Module builtins — always declared so "if __name__ == '__main__':" works
        if (!fields.ContainsKey("__name__"))
            fields["__name__"] = typeBuilder.DefineField(
                "__name__", typeof(string), FieldAttributes.Public | FieldAttributes.Static);
        if (!fields.ContainsKey("__file__"))
            fields["__file__"] = typeBuilder.DefineField(
                "__file__", typeof(string), FieldAttributes.Public | FieldAttributes.Static);

        // ── WinForms preamble bookkeeping ─────────────────────────────────────
        // Record which Application.* methods we will emit in the preamble so that
        // StatementEmitter can skip them when it encounters the same calls in source.
        if (profile == CompilationProfile.WinForms)
        {
            var appType = Type.GetType("System.Windows.Forms.Application, System.Windows.Forms");
            if (appType != null)
            {
                if (appType.GetMethod("SetHighDpiMode") != null)
                    _winFormsPreambleEmitted.Add("SetHighDpiMode");
                if (appType.GetMethod("EnableVisualStyles", Type.EmptyTypes) != null)
                    _winFormsPreambleEmitted.Add("EnableVisualStyles");
                if (appType.GetMethod("SetCompatibleTextRenderingDefault", new[] { typeof(bool) }) != null)
                    _winFormsPreambleEmitted.Add("SetCompatibleTextRenderingDefault");
            }
        }

        // ── Pass 2: define and emit Main() ────────────────────────────────────
        // ── Pass 2: define and emit Main() ────────────────────────────────────
        var mainBuilder = typeBuilder.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);

        // Stash for EmitToFile — PersistedAssemblyBuilder needs the MethodBuilder
        // handle to wire the PE entry point via MetadataTokens.MethodDefinitionHandle.
        _mainMethod = mainBuilder;

        var mainIL = mainBuilder.GetILGenerator();

        // WinForms preamble IL — emitted once here; StatementEmitter skips these
        // calls when it walks the Python source (double-emit prevention).
        if (profile == CompilationProfile.WinForms)
        {
            var appType = Type.GetType("System.Windows.Forms.Application, System.Windows.Forms");
            if (appType != null)
            {
                // Order matters: SetHighDpiMode must come before EnableVisualStyles
                var shdpi = appType.GetMethod("SetHighDpiMode");
                if (shdpi != null)
                {
                    mainIL.Emit(OpCodes.Ldc_I4_2);   // HighDpiMode.SystemAware = 2
                    mainIL.Emit(OpCodes.Call, shdpi);

                    // FIX: SetHighDpiMode returns a bool. We MUST pop it so the stack stays balanced!
                    if (shdpi.ReturnType != typeof(void))
                    {
                        mainIL.Emit(OpCodes.Pop);
                    }
                }
                var evs = appType.GetMethod("EnableVisualStyles", Type.EmptyTypes);
                if (evs != null)
                    mainIL.Emit(OpCodes.Call, evs);

                var sctrd = appType.GetMethod("SetCompatibleTextRenderingDefault", new[] { typeof(bool) });
                if (sctrd != null)
                {
                    mainIL.Emit(OpCodes.Ldc_I4_0);
                    mainIL.Emit(OpCodes.Call, sctrd);
                }
            }
        }

        // Initialise __name__ / __file__ so "if __name__ == '__main__':" evaluates
        mainIL.Emit(OpCodes.Ldstr, "__main__");
        mainIL.Emit(OpCodes.Stsfld, fields["__name__"]);
        mainIL.Emit(OpCodes.Ldstr, "");
        mainIL.Emit(OpCodes.Stsfld, fields["__file__"]);

        // Build emit context with all Pass-1 dictionaries populated
        var mainCtx = new EmitContext(mainIL, _model, typeBuilder, modBuilder, typeof(void), []);
        foreach (var (k, v) in fields) mainCtx.Fields[k] = v;
        foreach (var (k, v) in methods) mainCtx.Methods[k] = v;
        foreach (var (k, v) in paramTypes) mainCtx.MethodParamTypes[k] = v;
        foreach (var (k, v) in classTypes) mainCtx.ClassTypes[k] = v;
        foreach (var (k, v) in classCtors) mainCtx.ClassConstructors[k] = v;
        foreach (var (k, v) in _classCtorArgCounts) mainCtx.ClassCtorArgCounts[k] = v;
        foreach (var (k, v) in importMap) mainCtx.ImportMap[k] = v;

        foreach (var (k, v) in namespaceImports) mainCtx.NamespaceImports[k] = v;
        foreach (var mn in _classMethodNames) mainCtx.ClassMethods.Add(mn);
        foreach (var (k, v) in _classMethods) mainCtx.AllClassMethods[k] = v;
        foreach (var (k, v) in _classMethodParamTypes) mainCtx.AllClassMethodParamTypes[k] = v;
        foreach (var mn in _winFormsPreambleEmitted) mainCtx.WinFormsPreambleEmitted.Add(mn);


        // Walk module body — skip FunctionDef and ClassDef (handled in Pass 3)
        var stmtEmitter = new StatementEmitter(mainCtx);
        foreach (var stmt in module.Body)
        {
            if (stmt is FunctionDef or ClassDef) continue;
            stmtEmitter.Emit(stmt);
        }

        mainIL.Emit(OpCodes.Ret);

        // ── Pass 3: emit function and class bodies ────────────────────────────
        foreach (var stmt in module.Body)
        {
            if (stmt is FunctionDef fn && methods.TryGetValue(fn.Name, out var mb))
                EmitFunctionBody(fn, mb, typeBuilder, modBuilder, fields, methods, paramTypes, importMap);
            else if (stmt is ClassDef cls && classTypes.TryGetValue(cls.Name, out var ct))
                EmitClassBody(cls, ct, modBuilder, fields, methods, paramTypes, classTypes, classCtors, importMap);
        }

        // Finalise class TypeBuilders created in this module
        foreach (var ct in classTypes.Values)
        {
            try { ct.CreateType(); } catch { /* already created */ }
        }

        return typeBuilder;
    }

    // ── Class declaration stub ────────────────────────────────────────────────

    private TypeBuilder DeclareClass(
        ClassDef cls,
        ModuleBuilder modBuilder,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap,
        IReadOnlyList<Statement> moduleBody,
        out ConstructorBuilder defaultCtor)
    {
        // Resolve base class — search loaded assemblies first to handle strong-named
        // WinForms types correctly (Type.GetType with AQN is unreliable for them).
        Type baseType = typeof(object);
        if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr baseExpr)
        {
            if (importMap.TryGetValue(baseExpr.Name, out var imp))
            {
                // Resolve from disk metadata first (Roslyn-style), then fall back
                // to AppDomain for types the host already has loaded (BCL etc.).
                baseType =
                    _typeResolver.ResolveType(imp.TypeName)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                           .Select(a => { try { return a.GetType(imp.TypeName, false, true); } catch { return null; } })
                           .FirstOrDefault(t => t is not null)
                    ?? Type.GetType($"{imp.TypeName}, {imp.AssemblyName}")
                    ?? Type.GetType(imp.TypeName)
                    ?? typeof(object);
            }
            else if (_classTypes.TryGetValue(baseExpr.Name, out var localBase))
            {
                baseType = localBase;
            }
            else
            {
                baseType = baseExpr.Name switch
                {
                    "Exception" or "BaseException" => typeof(Exception),
                    "ValueError" => typeof(ArgumentException),
                    "TypeError" => typeof(InvalidCastException),
                    "RuntimeError" => typeof(InvalidOperationException),
                    "NotImplementedError" => typeof(NotImplementedException),
                    _ => typeof(object)
                };
            }

        }

        // Check for @typing.final or @final decorator to make class sealed
        bool isFinal = cls.Decorators.Any(d =>
            d is NameExpr { Name: "final" }
            || (d is AttributeExpr a && a.Attribute == "final" && a.Object is NameExpr { Name: "typing" }));

        var typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (isFinal) typeAttrs |= TypeAttributes.Sealed;

        var tb = modBuilder.DefineType(
            cls.Name,
            typeAttrs,
            baseType);

        // ── Determine constructor parameter count ────────────────────────────
        var initFn = cls.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
        int argCount = 0;
        bool isProxy = false;

        if (initFn != null)
        {
            bool hasSelf = initFn.Params.Count > 0
                        && initFn.Params[0].Name is "self" or "cls";
            argCount = hasSelf ? initFn.Params.Count - 1 : initFn.Params.Count;
        }
        else if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr bExpr)
        {
            // Inherit signature from Naja base class if the class has NO __init__
            var baseClsDef = moduleBody.OfType<ClassDef>().FirstOrDefault(c => c.Name == bExpr.Name);
            if (baseClsDef != null)
            {
                var baseInit = baseClsDef.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
                if (baseInit != null)
                {
                    bool hasSelf = baseInit.Params.Count > 0 && baseInit.Params[0].Name is "self" or "cls";
                    argCount = hasSelf ? baseInit.Params.Count - 1 : baseInit.Params.Count;
                    isProxy = true;
                    initFn = baseInit; // Use base init for parameter names
                }
            }
        }

        var ctorParams = Enumerable.Repeat(typeof(object), argCount).ToArray();
        defaultCtor = tb.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            ctorParams);


        // Name parameters for debuggability
        if (initFn != null)
        {
            bool hasSelf = initFn.Params.Count > 0 && initFn.Params[0].Name is "self" or "cls";
            int skip = hasSelf ? 1 : 0;
            for (int i = 0; i < argCount; i++)
                defaultCtor.DefineParameter(i + 1, ParameterAttributes.None,
                    initFn.Params[i + skip].Name);
        }

        var ctorIL = defaultCtor.GetILGenerator();

        // ── Call base constructor ────────────────────────────────────────────
        // We must push the correct number of arguments to the base constructor
        // to avoid InvalidProgramException (stack imbalance).

        ConstructorInfo? baseCtor = null;
        int baseParamCount = 0;
        bool foundNajaBase = false;

        if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr bExprResolve && _classConstructors.TryGetValue(bExprResolve.Name, out var bc))
        {
            baseCtor = bc;
            _classCtorArgCounts.TryGetValue(bExprResolve.Name, out baseParamCount);
            foundNajaBase = true;
        }

        if (isProxy && baseCtor == null)
        {
            // Proxy: call base constructor with EXACTLY the same signature
            baseCtor = baseType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance, null, ctorParams, null);
            if (baseCtor != null) baseParamCount = argCount;
        }

        // Fallback or non-proxy: find any accessible constructor
        if (baseCtor == null)
        {
            var baseCtors = baseType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            baseCtor = baseCtors.FirstOrDefault(c => c.GetParameters().Length == 0)
                    ?? baseCtors.FirstOrDefault()
                    ?? typeof(object).GetConstructor(Type.EmptyTypes)!;
            baseParamCount = baseCtor.GetParameters().Length;
        }

        ctorIL.Emit(OpCodes.Ldarg_0); // this
        if (isProxy && foundNajaBase && baseParamCount == argCount)
        {
            // Pass through all arguments
            for (int i = 0; i < argCount; i++)
            {
                int argIdx = i + 1;
                if (argIdx == 1) ctorIL.Emit(OpCodes.Ldarg_1);
                else if (argIdx == 2) ctorIL.Emit(OpCodes.Ldarg_2);
                else if (argIdx == 3) ctorIL.Emit(OpCodes.Ldarg_3);
                else ctorIL.Emit(OpCodes.Ldarg_S, (byte)argIdx);
            }
        }
        else
        {
            // Push nulls for any required arguments
            for (int i = 0; i < baseParamCount; i++)
                ctorIL.Emit(OpCodes.Ldnull);
        }

        ctorIL.Emit(OpCodes.Call, baseCtor);

        if (isProxy)
        {
            ctorIL.Emit(OpCodes.Ret);
        }
        else
        {
            // ── Deferred constructor completion ──────────────────────────────
            // At Pass 1 time the __init__ MethodBuilder does not exist yet —
            // it is declared in Pass 1.5.  Emitting Call on a MethodBuilder
            // that belongs to a type not yet created causes InvalidProgramException
            // with both AssemblyBuilder.Run and PersistedAssemblyBuilder.
            //
            // Solution: leave the ILGenerator open (no Ret here) and store it
            // so EmitClassBody (Pass 3) can complete the constructor after
            // __init__ is available. EmitClassBody calls CompleteConstructor().
            _pendingCtorIL[cls.Name] = (ctorIL, argCount);
            // Ret is emitted by CompleteConstructor — NOT here.
        }

        return tb;

    }

    // ── Deferred constructor completion ───────────────────────────────────────

    /// <summary>
    /// Completes the constructor body left open by DeclareClass (Pass 1).
    /// Called at the START of EmitClassBody (Pass 3) after Pass 1.5 has
    /// populated _classMethods with the __init__ MethodBuilder.
    ///
    /// Emits:
    ///   ldarg.0         (self)
    ///   ldarg.1 … N    (constructor parameters)
    ///   call __init__
    ///   [pop if non-void]
    ///   ret
    ///
    /// If no __init__ exists, emits ret only so the constructor is valid IL.
    /// </summary>
    private void CompleteConstructor(string className)
    {
        if (!_pendingCtorIL.TryGetValue(className, out var pending))
            return; // proxy ctor or already completed

        var (ctorIL, argCount) = pending;
        _pendingCtorIL.Remove(className);

        string key = $"{className}.__init__";
        if (_classMethods.TryGetValue(key, out var initMb))
        {
            ctorIL.Emit(OpCodes.Ldarg_0); // self
            for (int i = 0; i < argCount; i++)
            {
                int argIdx = i + 1;
                if (argIdx == 1) ctorIL.Emit(OpCodes.Ldarg_1);
                else if (argIdx == 2) ctorIL.Emit(OpCodes.Ldarg_2);
                else if (argIdx == 3) ctorIL.Emit(OpCodes.Ldarg_3);
                else ctorIL.Emit(OpCodes.Ldarg_S, (byte)argIdx);
            }
            ctorIL.Emit(OpCodes.Call, initMb);
            if (initMb.ReturnType != typeof(void))
                ctorIL.Emit(OpCodes.Pop);
        }

        ctorIL.Emit(OpCodes.Ret);
    }

    // ── Instance-field scanner ────────────────────────────────────────────────

    /// <summary>
    /// Recursively walks all statements looking for self.x = ... assignments
    /// so every instance field used anywhere in a method body gets declared on
    /// the TypeBuilder before IL emission begins.
    /// </summary>
    private static void ScanForInstanceFields(
        IReadOnlyList<Statement> stmts,
        Dictionary<string, FieldBuilder> instanceFields,
        TypeBuilder ct)
    {
        foreach (var stmt in stmts)
        {
            switch (stmt)
            {
                case AssignStatement assign:
                    foreach (var t in assign.Targets)
                        TryDeclareInstanceField(t, instanceFields, ct);
                    break;

                case AnnAssignStatement ann:
                    bool isFinal =
                        ann.Annotation is NameExpr { Name: "Final" }
                        || (ann.Annotation is AttributeExpr fa
                            && fa.Attribute == "Final"
                            && fa.Object is NameExpr { Name: "typing" })
                        || (ann.Annotation is SubscriptExpr sub
                            && (sub.Object is NameExpr { Name: "Final" }
                                || (sub.Object is AttributeExpr sa
                                    && sa.Attribute == "Final"
                                    && sa.Object is NameExpr { Name: "typing" })));
                    TryDeclareInstanceField(ann.Target, instanceFields, ct, isFinal);
                    break;

                case IfStatement ifs:
                    ScanForInstanceFields(ifs.Then, instanceFields, ct);
                    foreach (var (_, body) in ifs.Elifs)
                        ScanForInstanceFields(body, instanceFields, ct);
                    ScanForInstanceFields(ifs.Else, instanceFields, ct);
                    break;

                case ForStatement forS:
                    ScanForInstanceFields(forS.Body, instanceFields, ct);
                    ScanForInstanceFields(forS.Else, instanceFields, ct);
                    break;

                case WhileStatement whileS:
                    ScanForInstanceFields(whileS.Body, instanceFields, ct);
                    ScanForInstanceFields(whileS.Else, instanceFields, ct);
                    break;

                case TryStatement tryS:
                    ScanForInstanceFields(tryS.Body, instanceFields, ct);
                    foreach (var h in tryS.Handlers)
                        ScanForInstanceFields(h.Body, instanceFields, ct);
                    ScanForInstanceFields(tryS.Else, instanceFields, ct);
                    ScanForInstanceFields(tryS.Finally, instanceFields, ct);
                    break;

                case WithStatement withS:
                    ScanForInstanceFields(withS.Body, instanceFields, ct);
                    break;
            }
        }
    }

    private static void TryDeclareInstanceField(
    Expression target,
    Dictionary<string, FieldBuilder> instanceFields,
    TypeBuilder ct,
    bool isFinal = false)
    {
        // Matches: self.x = ...  or  cls.x = ...
        if (target is AttributeExpr attr
            && attr.Object is NameExpr { Name: "self" or "cls" }
            && !instanceFields.ContainsKey(attr.Attribute))
        {
            // 1. PREVENT SHADOWING BASE CLASS PROPERTIES (e.g. Form.Text, Form.Size)
            if (ct.BaseType != null)
            {
                var existingMembers = ct.BaseType.GetMember(
                    attr.Attribute,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (existingMembers.Length > 0)
                {
                    // Base class already has this member. By returning here, we force
                    // StatementEmitter to fall back to NajaBuiltins.SetAttr at runtime, 
                    // which will correctly trigger the WinForms property setters!
                    return;
                }
            }

            // 2. Define the field normally for custom Python state
            bool isPriv = attr.Attribute.StartsWith('_')
                       && !(attr.Attribute.StartsWith("__") && attr.Attribute.EndsWith("__"));
            var fbAttrs = isPriv ? FieldAttributes.Private : FieldAttributes.Public;
            if (isFinal) fbAttrs |= FieldAttributes.InitOnly;

            instanceFields[attr.Attribute] = ct.DefineField(attr.Attribute, typeof(object), fbAttrs);
        }
    }

    // ── Class body emission ───────────────────────────────────────────────────

    private void EmitClassBody(
        ClassDef cls,
        TypeBuilder ct,
        ModuleBuilder modBuilder,
        Dictionary<string, FieldBuilder> moduleFields,
        Dictionary<string, MethodBuilder> moduleMethods,
        Dictionary<string, Type[]> moduleParamTypes,
        Dictionary<string, TypeBuilder> moduleClassTypes,
        Dictionary<string, ConstructorBuilder> moduleClassCtors,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap)
    {
        var instanceFields = new Dictionary<string, FieldBuilder>();
        var classMethods = new Dictionary<string, MethodBuilder>();
        var classParamTs = new Dictionary<string, Type[]>();

        // ── Scan for class-level field declarations (AssignStatement and AnnAssignStatement) ──
        foreach (var member in cls.Body)
        {
            switch (member)
            {
                case AssignStatement assign:
                    foreach (var target in assign.Targets)
                        TryDeclareInstanceField(target, instanceFields, ct);
                    break;

                case AnnAssignStatement ann:
                    bool isFinal =
                        ann.Annotation is NameExpr { Name: "Final" }
                        || (ann.Annotation is AttributeExpr fa
                            && fa.Attribute == "Final"
                            && fa.Object is NameExpr { Name: "typing" })
                        || (ann.Annotation is SubscriptExpr sub
                            && (sub.Object is NameExpr { Name: "Final" }
                                || (sub.Object is AttributeExpr sa
                                    && sa.Attribute == "Final"
                                    && sa.Object is NameExpr { Name: "typing" })));
                    TryDeclareInstanceField(ann.Target, instanceFields, ct, isFinal);
                    break;
            }
        }

        // ── Scan for all instance fields (deep — covers if/for/try/with) ──────
        foreach (var member in cls.Body)
            if (member is FunctionDef fn)
                ScanForInstanceFields(fn.Body, instanceFields, ct);

        // ── Use pre-declared instance method stubs (from Pass 1.5) ────────────
        foreach (var member in cls.Body)
        {
            if (member is not FunctionDef fn) continue;

            // Resolve unique name (get_/set_ if property)
            bool isPropStub = fn.Decorators.Any(d => d is NameExpr { Name: "property" });
            bool isSetterStub = fn.Decorators.Any(d => d is AttributeExpr a && a.Attribute == "setter");
            string uname = isPropStub ? "get_" + fn.Name : isSetterStub ? "set_" + fn.Name : fn.Name;

            string key = $"{cls.Name}.{uname}";
            if (_classMethods.TryGetValue(key, out var mb))
            {
                classMethods[uname] = mb;
                classParamTs[uname] = _classMethodParamTypes[key];
            }
        }

        // ── Complete the deferred constructor now that __init__ is available ───
        // DeclareClass (Pass 1) left the constructor ILGenerator open after the
        // base ctor call. Now that _classMethods has the __init__ MethodBuilder
        // from Pass 1.5, we can emit the __init__ call and Ret.
        CompleteConstructor(cls.Name);


        // ── Dispose / IDisposable wiring ──────────────────────────────────────
        var delFn = cls.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__del__");
        var disposeBoolBase = (ct.BaseType != null && ct.BaseType is not TypeBuilder)
            ? ct.BaseType.GetMethod("Dispose",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null)
            : null;


        if (delFn != null || disposeBoolBase != null)
        {
            if (disposeBoolBase != null
                && (delFn != null
                    || ct.BaseType?.FullName?.StartsWith("System.Windows.Forms") == true))
            {
                var dispMb = ct.DefineMethod("Dispose",
                    MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    typeof(void), new[] { typeof(bool) });
                dispMb.DefineParameter(1, ParameterAttributes.None, "disposing");

                var dil = dispMb.GetILGenerator();
                if (delFn != null && classMethods.TryGetValue("__del__", out var delMb))
                {
                    var lbl = dil.DefineLabel();
                    dil.Emit(OpCodes.Ldarg_1);
                    dil.Emit(OpCodes.Brfalse, lbl);
                    dil.Emit(OpCodes.Ldarg_0);
                    dil.Emit(OpCodes.Call, delMb);
                    if (delMb.ReturnType != typeof(void)) dil.Emit(OpCodes.Pop);
                    dil.MarkLabel(lbl);
                }
                dil.Emit(OpCodes.Ldarg_0);
                dil.Emit(OpCodes.Ldarg_1);
                dil.Emit(OpCodes.Call, disposeBoolBase);
                dil.Emit(OpCodes.Ret);
                ct.DefineMethodOverride(dispMb, disposeBoolBase);
            }
            else if (delFn != null
                     && classMethods.TryGetValue("__del__", out var delMb2)
                     && !typeof(IDisposable).IsAssignableFrom(ct.BaseType ?? typeof(object)))
            {
                ct.AddInterfaceImplementation(typeof(IDisposable));
                var dispMb = ct.DefineMethod("Dispose",
                    MethodAttributes.Public | MethodAttributes.Virtual
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                    typeof(void), Type.EmptyTypes);
                var dil = dispMb.GetILGenerator();
                dil.Emit(OpCodes.Ldarg_0);
                dil.Emit(OpCodes.Call, delMb2);
                if (delMb2.ReturnType != typeof(void)) dil.Emit(OpCodes.Pop);
                dil.Emit(OpCodes.Ret);
            }
        }

        // ── Emit method bodies ────────────────────────────────────────────────
        foreach (var member in cls.Body)
        {
            if (member is not FunctionDef fn) continue;

            bool isProp = fn.Decorators.Any(d => d is NameExpr { Name: "property" });
            bool isSetter = fn.Decorators.Any(d => d is AttributeExpr a && a.Attribute == "setter");
            string uname = isProp ? "get_" + fn.Name : isSetter ? "set_" + fn.Name : fn.Name;

            if (classMethods.TryGetValue(uname, out var mb))
                EmitMethodBody(fn, mb, ct, modBuilder,
                               moduleFields, moduleMethods, moduleParamTypes,
                               moduleClassTypes, moduleClassCtors,
                               instanceFields, classMethods, classParamTs, importMap,
                               new Dictionary<string, string>()); // Namespace imports not yet passed to class methods
        }

        // ── Generate CLR property wrappers ────────────────────────────────────
        var propNames = cls.Body.OfType<FunctionDef>()
            .Where(f => f.Decorators.Any(d =>
                d is NameExpr { Name: "property" }
                || (d is AttributeExpr a && a.Attribute == "setter")))
            .Select(f => f.Name)
            .Distinct();

        foreach (var pName in propNames)
        {
            classMethods.TryGetValue("get_" + pName, out var getMb);
            classMethods.TryGetValue("set_" + pName, out var setMb);
            if (getMb == null && setMb == null) continue;
            var pb = ct.DefineProperty(pName, PropertyAttributes.None, typeof(object), null);
            if (getMb != null) pb.SetGetMethod(getMb);
            if (setMb != null) pb.SetSetMethod(setMb);
        }

        // ── Dunder overrides ──────────────────────────────────────────────────
        if (classMethods.TryGetValue("__str__", out var strMb))
        {
            var tsMb = ct.DefineMethod("ToString",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(string), Type.EmptyTypes);
            var dil = tsMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, strMb);
            dil.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToString", new[] { typeof(object) })!);
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(tsMb, typeof(object).GetMethod("ToString")!);
        }

        if (classMethods.TryGetValue("__eq__", out var eqMb))
        {
            var eqOvr = ct.DefineMethod("Equals",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(bool), new[] { typeof(object) });
            var dil = eqOvr.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Ldarg_1);
            dil.Emit(OpCodes.Call, eqMb);
            dil.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToBoolean", new[] { typeof(object) })!);
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(eqOvr, typeof(object).GetMethod("Equals", new[] { typeof(object) })!);
        }

        if (classMethods.TryGetValue("__hash__", out var hashMb))
        {
            var ghMb = ct.DefineMethod("GetHashCode",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                typeof(int), Type.EmptyTypes);
            var dil = ghMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, hashMb);
            dil.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", new[] { typeof(object) })!);
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(ghMb, typeof(object).GetMethod("GetHashCode")!);
        }

        // __len__ → Count property (for ICollection compatibility)
        if (classMethods.TryGetValue("__len__", out var lenMb))
        {
            var countProp = ct.DefineProperty("Count", PropertyAttributes.None, typeof(int), null);
            var getCountMb = ct.DefineMethod("get_Count",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                typeof(int), Type.EmptyTypes);
            var dil = getCountMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, lenMb);
            dil.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", new[] { typeof(object) })!);
            dil.Emit(OpCodes.Ret);
            countProp.SetGetMethod(getCountMb);
        }

        // __bool__ → op_True, op_False, op_Implicit
        // Python __bool__ maps to three .NET operator methods:
        //   op_True     → result of __bool__
        //   op_False    → logical negation of __bool__
        //   op_Implicit → same as op_True (implicit bool conversion)
        if (classMethods.TryGetValue("__bool__", out var boolMb))
        {
            var selfParam = new[] { ct.AsType() };

            foreach (var opName in new[] { "op_True", "op_False", "op_Implicit" })
            {
                var opMb = ct.DefineMethod(
                    opName,
                    MethodAttributes.Public | MethodAttributes.Static
                        | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                    typeof(bool),
                    selfParam);
                opMb.DefineParameter(1, ParameterAttributes.None, "obj");

                var oil = opMb.GetILGenerator();
                oil.Emit(OpCodes.Ldarg_0);      // push obj (self)
                oil.Emit(OpCodes.Call, boolMb); // call __bool__(self)

                // __bool__ returns object — convert to bool
                if (boolMb.ReturnType != typeof(bool))
                    oil.Emit(OpCodes.Call,
                        typeof(Convert).GetMethod("ToBoolean", new[] { typeof(object) })!);

                if (opName == "op_False")
                {
                    // Negate: ldc.i4.0 + ceq is the standard IL NOT pattern
                    oil.Emit(OpCodes.Ldc_I4_0);
                    oil.Emit(OpCodes.Ceq);
                }

                oil.Emit(OpCodes.Ret);
            }
        }

        // __copy__ → ICloneable.Clone
        if (classMethods.TryGetValue("__copy__", out var copyMb))
        {
            ct.AddInterfaceImplementation(typeof(ICloneable));
            var cloneMb = ct.DefineMethod("Clone",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(object), Type.EmptyTypes);
            var dil = cloneMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, copyMb);
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(cloneMb, typeof(ICloneable).GetMethod("Clone")!);
        }

        // __getitem__/__setitem__ → Indexer with DefaultMemberAttribute
        bool hasGetItem = classMethods.TryGetValue("__getitem__", out var getItemMb);
        bool hasSetItem = classMethods.TryGetValue("__setitem__", out var setItemMb);
        if (hasGetItem || hasSetItem)
        {
            // Add DefaultMemberAttribute for indexer support
            var defaultMemberCtor = typeof(DefaultMemberAttribute).GetConstructor(new[] { typeof(string) })!;
            ct.SetCustomAttribute(new CustomAttributeBuilder(defaultMemberCtor, new object[] { "Item" }));

            var indexerProp = ct.DefineProperty("Item", PropertyAttributes.None, typeof(object), new[] { typeof(object) });

            if (hasGetItem)
            {
                var getIndexerMb = ct.DefineMethod("get_Item",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    typeof(object), new[] { typeof(object) });
                getIndexerMb.DefineParameter(1, ParameterAttributes.None, "key");
                var dil = getIndexerMb.GetILGenerator();
                dil.Emit(OpCodes.Ldarg_0);
                dil.Emit(OpCodes.Ldarg_1);
                dil.Emit(OpCodes.Call, getItemMb);
                dil.Emit(OpCodes.Ret);
                indexerProp.SetGetMethod(getIndexerMb);
            }

            if (hasSetItem)
            {
                var setIndexerMb = ct.DefineMethod("set_Item",
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                    typeof(void), new[] { typeof(object), typeof(object) });
                setIndexerMb.DefineParameter(1, ParameterAttributes.None, "key");
                setIndexerMb.DefineParameter(2, ParameterAttributes.None, "value");
                var dil = setIndexerMb.GetILGenerator();
                dil.Emit(OpCodes.Ldarg_0);
                dil.Emit(OpCodes.Ldarg_1);
                dil.Emit(OpCodes.Ldarg_2);
                dil.Emit(OpCodes.Call, setItemMb);
                if (setItemMb.ReturnType != typeof(void)) dil.Emit(OpCodes.Pop);
                dil.Emit(OpCodes.Ret);
                indexerProp.SetSetMethod(setIndexerMb);
            }
        }

        // __iter__/__next__ → IEnumerable/IEnumerator
        bool hasIter = classMethods.TryGetValue("__iter__", out var iterMb);
        bool hasNext = classMethods.TryGetValue("__next__", out var nextMb);
        if (hasIter)
        {
            ct.AddInterfaceImplementation(typeof(IEnumerable));
            var getEnumMb = ct.DefineMethod("GetEnumerator",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(System.Collections.IEnumerator), Type.EmptyTypes);
            var dil = getEnumMb.GetILGenerator();
            dil.Emit(OpCodes.Ldarg_0);
            dil.Emit(OpCodes.Call, iterMb);
            // If __iter__ returns 'this', we need to box it for IEnumerator
            if (hasNext)
            {
                // The class implements IEnumerator via __next__
                ct.AddInterfaceImplementation(typeof(IEnumerator));
                if (nextMb.ReturnType.IsValueType)
                    dil.Emit(OpCodes.Box, nextMb.ReturnType);
            }
            dil.Emit(OpCodes.Castclass, typeof(IEnumerator));
            dil.Emit(OpCodes.Ret);
            ct.DefineMethodOverride(getEnumMb, typeof(IEnumerable).GetMethod("GetEnumerator")!);
        }

        // Implement IEnumerator interface if __next__ is present
        if (hasNext)
        {
            ct.AddInterfaceImplementation(typeof(IEnumerator));

            // Define fields to store the current value and exhausted state
            var currentValueField = ct.DefineField("__current_value", typeof(object), FieldAttributes.Private);
            var exhaustedField = ct.DefineField("__exhausted", typeof(bool), FieldAttributes.Private);

            // Current property
            var currentProp = ct.DefineProperty("Current", PropertyAttributes.None, typeof(object), null);

            // get_Current - returns the stored current value
            var getCurrentMb = ct.DefineMethod("get_Current",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(object), Type.EmptyTypes);
            var curIl = getCurrentMb.GetILGenerator();
            curIl.Emit(OpCodes.Ldarg_0);
            curIl.Emit(OpCodes.Ldfld, currentValueField);
            curIl.Emit(OpCodes.Ret);
            currentProp.SetGetMethod(getCurrentMb);

            // MoveNext - call __next__ and handle StopIteration via runtime helper
            var moveNextMb = ct.DefineMethod("MoveNext",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(bool), Type.EmptyTypes);
            var mnIl = moveNextMb.GetILGenerator();

            // Call NajaBuiltins.IteratorMoveNext(__next__ method delegate, currentField, exhaustedField)
            mnIl.Emit(OpCodes.Ldarg_0);  // this (for method call)
            mnIl.Emit(OpCodes.Ldftn, nextMb);  // method pointer
            mnIl.Emit(OpCodes.Newobj, typeof(Func<object>).GetConstructor(new[] { typeof(object), typeof(IntPtr) })!);
            mnIl.Emit(OpCodes.Ldarg_0);  // this for field addresses
            mnIl.Emit(OpCodes.Ldflda, currentValueField);  // ref to current value field
            mnIl.Emit(OpCodes.Ldarg_0);
            mnIl.Emit(OpCodes.Ldflda, exhaustedField);  // ref to exhausted field
            mnIl.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.IteratorMoveNext))!);
            mnIl.Emit(OpCodes.Ret);

            // Reset - throw NotSupportedException
            var resetMb = ct.DefineMethod("Reset",
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
                typeof(void), Type.EmptyTypes);
            var rIl = resetMb.GetILGenerator();
            rIl.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
            rIl.Emit(OpCodes.Throw);

            ct.DefineMethodOverride(getCurrentMb, typeof(IEnumerator).GetProperty("Current")!.GetMethod!);
            ct.DefineMethodOverride(moveNextMb, typeof(IEnumerator).GetMethod("MoveNext")!);
            ct.DefineMethodOverride(resetMb, typeof(IEnumerator).GetMethod("Reset")!);
        }
    }



    // ── Instance method declaration ───────────────────────────────────────────

    private (MethodBuilder mb, Type[] paramTypes, string uniqueName) DeclareInstanceMethod(
        FunctionDef fn, TypeBuilder ct)
    {
        if (fn.IsAsync)
            throw new CodeGenException("async/await is not yet supported.", fn.Line, fn.Column);

        bool hasSelf = fn.Params.Count > 0 && fn.Params[0].Name is "self" or "cls";
        var clrParams = fn.Params
            .Skip(hasSelf ? 1 : 0)
            .Select(_ => typeof(object))
            .ToArray();

        bool isStatic = fn.Decorators.Any(d => d is NameExpr { Name: "staticmethod" or "classmethod" });
        bool isFinal = fn.Decorators.Any(d =>
            d is NameExpr { Name: "final" }
            || (d is AttributeExpr a && a.Attribute == "final" && a.Object is NameExpr { Name: "typing" }));
        bool isProp = fn.Decorators.Any(d => d is NameExpr { Name: "property" });
        bool isSetter = fn.Decorators.Any(d => d is AttributeExpr a2 && a2.Attribute == "setter");

        string uniqueName = isProp ? "get_" + fn.Name
                          : isSetter ? "set_" + fn.Name
                          : fn.Name;
        string emitName = uniqueName;

        bool isPriv = fn.Name.StartsWith('_')
                   && !(fn.Name.StartsWith("__") && fn.Name.EndsWith("__"));

        var attrs = isPriv ? MethodAttributes.Private : MethodAttributes.Public;

        if (isStatic)
        {
            attrs |= MethodAttributes.Static;
        }
        else
        {
            attrs |= MethodAttributes.Virtual | MethodAttributes.HideBySig;

            // Avoid reflection on TypeBuilder base
            MethodInfo? baseMethod = null;
            if (ct.BaseType != null && ct.BaseType is not TypeBuilder)
            {
                baseMethod = ct.BaseType.GetMethod(
                    emitName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, clrParams, null);
            }

            if (baseMethod == null || !baseMethod.IsVirtual || baseMethod.IsFinal)
                attrs |= MethodAttributes.NewSlot;
        }


        if (isFinal && !isStatic) attrs |= MethodAttributes.Final;
        if (isProp || isSetter) attrs |= MethodAttributes.SpecialName;

        var mb = ct.DefineMethod(emitName, attrs, typeof(object), clrParams);

        if (!isStatic)
            for (int i = 0; i < clrParams.Length; i++)
                mb.DefineParameter(i + 1, ParameterAttributes.None,
                    fn.Params[i + (hasSelf ? 1 : 0)].Name);

        return (mb, clrParams, uniqueName);
    }

    // ── Instance method body emission ─────────────────────────────────────────

    private void EmitMethodBody(
        FunctionDef fn,
        MethodBuilder mb,
        TypeBuilder ct,
        ModuleBuilder modBuilder,
        Dictionary<string, FieldBuilder> moduleFields,
        Dictionary<string, MethodBuilder> moduleMethods,
        Dictionary<string, Type[]> moduleParamTypes,
        Dictionary<string, TypeBuilder> moduleClassTypes,
        Dictionary<string, ConstructorBuilder> moduleClassCtors,
        Dictionary<string, FieldBuilder> instanceFields,
        Dictionary<string, MethodBuilder> classMethods,
        Dictionary<string, Type[]> classParamTs,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap,
        Dictionary<string, string> namespaceImports)
    {
        var il = mb.GetILGenerator();
        bool isStatic = fn.Decorators.Any(d => d is NameExpr { Name: "staticmethod" or "classmethod" });
        bool hasSelf = !isStatic && fn.Params.Count > 0 && fn.Params[0].Name is "self" or "cls";

        var paramNames = fn.Params.Skip(hasSelf ? 1 : 0).Select(p => p.Name).ToList();

        var ctx = new EmitContext(il, _model, ct, modBuilder, typeof(object), paramNames);
        ctx.IsInstanceMethod = !isStatic;
        ctx.SelfName = hasSelf ? fn.Params[0].Name : null;
        ctx.IsInsideFunction = true;  // Fix 19: Mark as inside function for yield check

        foreach (var (k, v) in moduleFields) ctx.Fields[k] = v;
        foreach (var (k, v) in moduleMethods) ctx.Methods[k] = v;
        foreach (var (k, v) in moduleParamTypes) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in moduleClassTypes) ctx.ClassTypes[k] = v;
        foreach (var (k, v) in moduleClassCtors) ctx.ClassConstructors[k] = v;
        foreach (var (k, v) in _classCtorArgCounts) ctx.ClassCtorArgCounts[k] = v;
        foreach (var (k, v) in _classMethods) ctx.AllClassMethods[k] = v;

        foreach (var (k, v) in _classMethodParamTypes) ctx.AllClassMethodParamTypes[k] = v;
        foreach (var (k, v) in instanceFields) ctx.InstanceFields[k] = v;
        foreach (var (k, v) in classMethods) ctx.Methods[k] = v;
        foreach (var (k, v) in classParamTs) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in importMap) ctx.ImportMap[k] = v;
        foreach (var (k, v) in namespaceImports) ctx.NamespaceImports[k] = v;
        foreach (var mn in _classMethodNames) ctx.ClassMethods.Add(mn);


        var emitter = new StatementEmitter(ctx);
        emitter.EmitAll(fn.Body);

        bool endsWithReturn = fn.Body.Count > 0 && fn.Body[^1] is ReturnStatement;
        if (!endsWithReturn)
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    // ── Module-level function declaration stub ────────────────────────────────

    private (MethodBuilder mb, Type[] paramTypes) DeclareMethod(FunctionDef fn, TypeBuilder tb)
    {
        if (fn.IsAsync)
            throw new CodeGenException("async/await is not yet supported.", fn.Line, fn.Column);

        var sym = _model.ModuleScope.Lookup(fn.Name);
        var fnType = sym?.Type as FunctionType;
        var retType = fnType is not null
            ? TypeMapper.ToReturnType(fnType.ReturnType)
            : typeof(object);

        var pts = fn.Params.Select((p, i) =>
        {
            var pType = fnType?.ParamTypes.ElementAtOrDefault(i);
            var clr = pType is not null ? TypeMapper.ToClrType(pType) : typeof(object);
            return clr == typeof(void) ? typeof(object) : clr;
        }).ToArray();

        var mb = tb.DefineMethod(
            fn.Name,
            MethodAttributes.Public | MethodAttributes.Static,
            retType,
            pts);

        for (int i = 0; i < fn.Params.Count; i++)
            mb.DefineParameter(i + 1, ParameterAttributes.None, fn.Params[i].Name);

        return (mb, pts);
    }

    // ── Module-level function body emission ───────────────────────────────────

    private void EmitFunctionBody(
        FunctionDef fn,
        MethodBuilder mb,
        TypeBuilder tb,
        ModuleBuilder modBuilder,
        Dictionary<string, FieldBuilder> fields,
        Dictionary<string, MethodBuilder> methods,
        Dictionary<string, Type[]> paramTypes,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap)
    {
        var il = mb.GetILGenerator();
        var sym = _model.ModuleScope.Lookup(fn.Name);
        var fnType = sym?.Type as FunctionType;
        var returnType = fnType is not null
            ? TypeMapper.ToReturnType(fnType.ReturnType)
            : typeof(object);

        var paramNames = fn.Params.Select(p => p.Name).ToList();
        var ctx = new EmitContext(il, _model, tb, modBuilder, returnType, paramNames);
        ctx.IsInsideFunction = true;  // Fix 19: Mark as inside function for yield check

        foreach (var (k, v) in fields) ctx.Fields[k] = v;
        foreach (var (k, v) in methods) ctx.Methods[k] = v;
        foreach (var (k, v) in paramTypes) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in _classTypes) ctx.ClassTypes[k] = v;
        foreach (var (k, v) in _classConstructors) ctx.ClassConstructors[k] = v;
        foreach (var mn in _classMethodNames) ctx.ClassMethods.Add(mn);
        foreach (var (k, v) in importMap) ctx.ImportMap[k] = v;

        // Hoist variables referenced by nested functions: if an inner function
        // references a name that is assigned in this function, promote that name
        // to a module-level static field so the inner function sees the enclosing
        // binding (Python LEGB semantics).
        var nestedRefs = StatementEmitter.CollectNamesReferencedByNestedFunctions(fn.Body);
        var assigned = StatementEmitter.CollectAssignedNames(fn.Body);
        foreach (var r in nestedRefs.Intersect(assigned))
        {
            // Create hoisted field EVEN IF a module-level field exists with this name
            // because we need to shadow it in this function's scope for proper closure semantics
            var fb = tb.DefineField($"__nl_{r}", typeof(object), FieldAttributes.Private | FieldAttributes.Static);
            ctx.Fields[r] = fb;  // Override any existing field in context
        }

        // Check if this function contains yield statements (is a generator)
        bool isGenerator = StatementEmitter.ContainsYield(fn.Body);
        if (isGenerator)
        {
            // Initialize the generator list at function entry
            var listType = typeof(System.Collections.Generic.List<object>);
            var ctor = listType.GetConstructor(Type.EmptyTypes)!;
            ctx.GeneratorListLocal = ctx.Locals.Declare($"__generator_{fn.Name}_{fn.Line}", listType);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, ctx.GeneratorListLocal);

            // For generators, override returnType to be object since we'll return the list
            returnType = typeof(object);
        }

        var emitter = new StatementEmitter(ctx);
        emitter.EmitAll(fn.Body);

        bool endsWithReturn = fn.Body.Count > 0 && fn.Body[^1] is ReturnStatement;
        if (!endsWithReturn)
        {
            if (isGenerator)
            {
                // Return the generator list
                il.Emit(OpCodes.Ldloc, ctx.GeneratorListLocal);
            }
            else if (returnType != typeof(void))
            {
                il.Emit(OpCodes.Ldnull);
            }
            il.Emit(OpCodes.Ret);
        }
    }
}



/// <summary>
/// Resolves .NET framework types from SDK reference-assembly packs on disk —
/// exactly the way Roslyn resolves them — so the Naja compiler process never
/// needs the target framework loaded into its own AppDomain.
///
/// Resolution order per type lookup:
///   1. In-memory cache (hot path after first hit).
///   2. All *.dll files found under every SDK pack directory that is probed.
///   3. Falls back gracefully to null (caller decides what to do).
///
/// Pack directories probed (in priority order):
///   • Microsoft.AspNetCore.App  (fixes the ASP.NET Core bug)
///   • Microsoft.WindowsDesktop.App  (WinForms / WPF)
///   • Microsoft.NETCore.App  (BCL — usually already in AppDomain, but belt-and-braces)
///
/// The resolver is a process-wide singleton (<see cref="AssemblyEmitter._typeResolver"/>)
/// and is thread-safe after construction.
/// </summary>
internal sealed class FrameworkTypeResolver
{
    // ── Known type → assembly short name table ────────────────────────────────
    // Hard-coded for the most-used ASP.NET Core surface.  Extended automatically
    // when MetadataLoadContext probing succeeds, so this list does not need to be
    // exhaustive — it is just a fast-path for the common cases.
    private static readonly Dictionary<string, string> _knownTypeToAssembly =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Microsoft.AspNetCore.Builder ──────────────────────────────────────
            ["Microsoft.AspNetCore.Builder.WebApplication"] = "Microsoft.AspNetCore",
            ["Microsoft.AspNetCore.Builder.WebApplicationBuilder"] = "Microsoft.AspNetCore",
            ["Microsoft.AspNetCore.Builder.WebApplicationOptions"] = "Microsoft.AspNetCore",
            ["Microsoft.AspNetCore.Builder.IApplicationBuilder"] = "Microsoft.AspNetCore.Http.Abstractions",
            ["Microsoft.AspNetCore.Builder.IEndpointRouteBuilder"] = "Microsoft.AspNetCore.Routing",
            ["Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions"] = "Microsoft.AspNetCore.Routing",
            // ── Microsoft.AspNetCore.Http ─────────────────────────────────────────
            ["Microsoft.AspNetCore.Http.HttpContext"] = "Microsoft.AspNetCore.Http.Abstractions",
            ["Microsoft.AspNetCore.Http.HttpRequest"] = "Microsoft.AspNetCore.Http.Abstractions",
            ["Microsoft.AspNetCore.Http.HttpResponse"] = "Microsoft.AspNetCore.Http.Abstractions",
            ["Microsoft.AspNetCore.Http.IResult"] = "Microsoft.AspNetCore.Http.Abstractions",
            ["Microsoft.AspNetCore.Http.Results"] = "Microsoft.AspNetCore.Http",
            ["Microsoft.AspNetCore.Http.RequestDelegate"] = "Microsoft.AspNetCore.Http.Abstractions",
            // ── Microsoft.AspNetCore.Routing ──────────────────────────────────────
            ["Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"] = "Microsoft.AspNetCore.Routing",
            // ── Microsoft.Extensions.Hosting ─────────────────────────────────────
            ["Microsoft.Extensions.Hosting.IHost"] = "Microsoft.Extensions.Hosting.Abstractions",
            ["Microsoft.Extensions.Hosting.IHostBuilder"] = "Microsoft.Extensions.Hosting.Abstractions",
            ["Microsoft.Extensions.Hosting.IHostedService"] = "Microsoft.Extensions.Hosting.Abstractions",
            // ── Microsoft.Extensions.DependencyInjection ─────────────────────────
            ["Microsoft.Extensions.DependencyInjection.IServiceCollection"] = "Microsoft.Extensions.DependencyInjection.Abstractions",
            ["Microsoft.Extensions.DependencyInjection.IServiceProvider"] = "Microsoft.Extensions.DependencyInjection.Abstractions",
        };

    // ── Runtime cache: fully-qualified type name → resolved Type ─────────────
    private readonly Dictionary<string, Type?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    // ── MetadataLoadContext + all probed DLLs ─────────────────────────────────
    private readonly MetadataLoadContext? _mlc;
    private readonly IReadOnlyList<string> _allDlls;

    public FrameworkTypeResolver()
    {
        var dlls = new List<string>();

        // Probe every installed SDK pack under the dotnet root.
        // We intentionally include ALL version subdirs and let the cache handle
        // de-duplication.  Cost is paid once at resolver construction.
        foreach (var packDir in GetPackDirectories())
        {
            if (Directory.Exists(packDir))
                dlls.AddRange(Directory.GetFiles(packDir, "*.dll", SearchOption.AllDirectories));
        }

        // Also add the BCL from the current runtime so primitives resolve without
        // needing a separate CoreLib pack.
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        if (Directory.Exists(runtimeDir))
            dlls.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));

        _allDlls = dlls;

        if (dlls.Count > 0)
        {
            try
            {
                var resolver = new PathAssemblyResolver(dlls);
                _mlc = new MetadataLoadContext(resolver);
            }
            catch
            {
                // Non-fatal: fall through to AppDomain-only resolution.
                _mlc = null;
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the short assembly name (e.g. "Microsoft.AspNetCore") for a
    /// fully-qualified type name, or null if not found.
    /// </summary>
    public string? ResolveAssemblyName(string fullyQualifiedTypeName)
    {
        // 1. Known-types fast path
        if (_knownTypeToAssembly.TryGetValue(fullyQualifiedTypeName, out var known))
            return known;

        // 2. Probe via MetadataLoadContext
        var t = ResolveType(fullyQualifiedTypeName);
        return t?.Assembly.GetName().Name;
    }

    /// <summary>
    /// Returns the <see cref="Type"/> object for a fully-qualified type name
    /// by scanning SDK reference assemblies on disk, or null if not found.
    /// </summary>
    public Type? ResolveType(string fullyQualifiedTypeName)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(fullyQualifiedTypeName, out var cached))
                return cached;

            var result = ResolveCore(fullyQualifiedTypeName);
            _cache[fullyQualifiedTypeName] = result;
            return result;
        }
    }

    // ── Internal resolution ───────────────────────────────────────────────────

    private Type? ResolveCore(string typeName)
    {
        if (_mlc is null) return null;

        // Try every DLL we know about.  MetadataLoadContext.LoadFromAssemblyPath
        // is cheap for already-loaded assemblies (returns cached instance).
        foreach (var dll in _allDlls)
        {
            try
            {
                var asm = _mlc.LoadFromAssemblyPath(dll);
                var t = asm.GetType(typeName, throwOnError: false, ignoreCase: true);
                if (t is not null) return t;
            }
            catch
            {
                // Corrupt / incompatible PE — skip silently.
            }
        }
        return null;
    }

    // ── SDK pack directory discovery ──────────────────────────────────────────

    private static IEnumerable<string> GetPackDirectories()
    {
        // dotnet install root: $DOTNET_ROOT or well-known paths per OS
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? GetDefaultDotnetRoot();

        if (string.IsNullOrEmpty(dotnetRoot) || !Directory.Exists(dotnetRoot))
            yield break;

        var packsRoot = Path.Combine(dotnetRoot, "packs");
        if (!Directory.Exists(packsRoot)) yield break;

        // The three framework packs in priority order.
        // Each pack contains a "ref/<tfm>/" sub-dir with the reference DLLs.
        string[] packNames =
        [
            "Microsoft.AspNetCore.App.Ref",
            "Microsoft.WindowsDesktop.App.Ref",
            "Microsoft.NETCore.App.Ref",
        ];

        foreach (var pack in packNames)
        {
            var packDir = Path.Combine(packsRoot, pack);
            if (Directory.Exists(packDir))
                yield return packDir;
        }
    }

    private static string? GetDefaultDotnetRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Standard Windows install location
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Path.Combine(pf, "dotnet");
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "/usr/local/share/dotnet";

        // Linux
        return "/usr/share/dotnet";
    }
}
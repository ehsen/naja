using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Naja.CodeGen.Builtins;

namespace Naja.CodeGen;

internal static class AssemblyPEWriter
{
    public static void SerializeToFile(PersistedAssemblyBuilder asmBuilder, MethodBuilder? mainMethod, bool needsExe, PEHeaderBuilder peHeader, CompilationProfile profile, string outputPath)
    {
        var metaBuilder = asmBuilder.GenerateMetadata(out BlobBuilder ilStream, out BlobBuilder fieldData);

        MethodDefinitionHandle entryHandle = default;
        if (mainMethod is not null && needsExe)
            entryHandle = MetadataTokens.MethodDefinitionHandle(mainMethod.MetadataToken);

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

        // Write sidecar files (runtimeconfig.json, deps.json)
        WriteRuntimeConfig(outputPath, profile);
        WriteDepsJson(outputPath);

        // Copy Naja runtime DLLs next to the generated exe.
        //
        // Root cause of the "WinForms fails silently" bug:
        //   The generated IL calls NajaBuiltins.* / DynamicOperators.* which live in
        //   Naja.CodeGen.dll.  That DLL (and its Naja.* transitive deps) are never
        //   placed in the output directory by `naja compile`, so the CLR throws
        //   FileNotFoundException on the very first call into the runtime helpers.
        //   Because the PE subsystem is WindowsGui there is no console to display
        //   the error on — the process exits silently.
        if (needsExe)
            CopyRuntimeDependencies(outputPath);
    }

    // Duplicate minimal implementations from AssemblyEmitter (kept local to avoid access changes)
    private static void WriteRuntimeConfig(string assemblyPath, CompilationProfile profile)
    {
        string frameworkName;
        string tfm;

        switch (profile)
        {
            case CompilationProfile.WinForms:
            case CompilationProfile.Wpf:
                frameworkName = "Microsoft.WindowsDesktop.App";
                tfm = "net10.0-windows";
                break;
            case CompilationProfile.AspNetCore:
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
                // Roll forward to any newer patch release of the same minor version.
                // Without this, "10.0.0" fails on machines that only have 10.0.1+.
                rollForward = "LatestPatch",
                framework = new
                {
                    name = frameworkName,
                    version = "10.0.0"
                },
                configProperties = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["System.Runtime.Loader.UseRidGraph"] = false
                }
            }
        };

        var path = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
        File.WriteAllText(path,
            System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteDepsJson(string assemblyPath)
    {
        // Collect every Naja.* assembly that was loaded into the compiler process.
        // These are all runtime deps of the generated exe because the emitted IL
        // calls directly into NajaBuiltins, DynamicOperators, NajaSlice, etc.
        var runtimeDlls = new System.Collections.Generic.Dictionary<string, object>();
        var libraryEntries = new System.Collections.Generic.Dictionary<string, object>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var an = asm.GetName();
            var name = an.Name;
            if (name == null) continue;

            // Include all Naja project assemblies plus MetadataLoadContext (NuGet package
            // referenced by Naja.CodeGen — needed if any compiler type is ever accessed).
            if (!name.StartsWith("Naja.", StringComparison.Ordinal)
                && name != "System.Reflection.MetadataLoadContext")
                continue;

            var version = an.Version?.ToString() ?? "1.0.0.0";
            var key = $"{name}/{version}";
            runtimeDlls[$"{name}.dll"] = new { };
            libraryEntries[key] = new { type = "project", serviceable = false, sha512 = "" };
        }

        var targetKey = ".NETCoreApp,Version=v10.0";
        var targetAssemblies = new System.Collections.Generic.Dictionary<string, object>();
        foreach (var (dll, _) in runtimeDlls)
        {
            var baseName = Path.GetFileNameWithoutExtension(dll);
            var version = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == baseName)
                ?.GetName().Version?.ToString() ?? "1.0.0.0";
            targetAssemblies[$"{baseName}/{version}"] = new
            {
                runtime = new System.Collections.Generic.Dictionary<string, object> { [dll] = new { } }
            };
        }

        var deps = new
        {
            runtimeTarget = new { name = targetKey, signature = "" },
            targets = new System.Collections.Generic.Dictionary<string, object>
            {
                [targetKey] = targetAssemblies
            },
            libraries = libraryEntries
        };

        var path = Path.ChangeExtension(assemblyPath, ".deps.json");
        File.WriteAllText(path,
            System.Text.Json.JsonSerializer.Serialize(deps, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Runtime dependency deployment ─────────────────────────────────────────

    // The Naja compiler DLLs that the generated exe needs at runtime.
    // All emitted IL calls into these assemblies (NajaBuiltins, DynamicOperators, etc.).
    private static readonly string[] _najaRuntimeDlls =
    [
        "Naja.CodeGen.dll",
        "Naja.Lexer.dll",
        "Naja.Parser.dll",
        "Naja.Semantics.dll",
        "Naja.Inference.dll",
        "Naja.StdLib.dll",
        "System.Reflection.MetadataLoadContext.dll",
    ];

    /// <summary>
    /// Copies every Naja runtime DLL from the compiler's own directory into the
    /// directory that contains <paramref name="outputExePath"/>.
    ///
    /// This is the fix for the WinForms (and any exe) silent-failure bug:
    ///   naja compile emits an exe whose IL calls NajaBuiltins / DynamicOperators
    ///   from Naja.CodeGen.dll, but nothing copies that DLL to the output folder.
    ///   The CLR throws FileNotFoundException at Main() entry — silently on
    ///   WindowsGui subsystem because there is no console to write to.
    /// </summary>
    private static void CopyRuntimeDependencies(string outputExePath)
    {
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputExePath)) ?? ".";
        var compilerDir = AppContext.BaseDirectory;

        foreach (var dll in _najaRuntimeDlls)
        {
            var src = Path.Combine(compilerDir, dll);
            if (!File.Exists(src)) continue;

            var dst = Path.Combine(outputDir, dll);
            // Overwrite — the compiler is authoritative for its own runtime version.
            File.Copy(src, dst, overwrite: true);
        }
    }
}

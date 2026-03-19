using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

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
        // Reuse existing private helpers by calling through AssemblyEmitter's static methods
        // located in the same class file. Those helpers are private; duplicate minimal logic here
        // to avoid changing access modifiers.
        WriteRuntimeConfig(outputPath, profile);
        WriteDepsJson(outputPath);
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
        var najaAsm = typeof(NajaBuiltins).Assembly.GetName();
        var najaName = najaAsm.Name ?? "Naja.CodeGen";
        var najaVersion = najaAsm.Version?.ToString() ?? "1.0.0.0";
        var najaTarget = $"{najaName}/{najaVersion}";

        var deps = new
        {
            runtimeTarget = new { name = ".NETCoreApp,Version=v10.0", signature = "" },
            targets = new System.Collections.Generic.Dictionary<string, object>
            {
                [".NETCoreApp,Version=v10.0"] = new System.Collections.Generic.Dictionary<string, object>
                {
                    [najaTarget] = new
                    {
                        runtime = new System.Collections.Generic.Dictionary<string, object>
                        {
                            [$"{najaName}.dll"] = new { }
                        }
                    }
                }
            },
            libraries = new System.Collections.Generic.Dictionary<string, object>
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
            System.Text.Json.JsonSerializer.Serialize(deps, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}

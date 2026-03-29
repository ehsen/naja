using System;
using System.IO;
using System.Linq;
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

        // For executables, write the managed assembly as .dll and place a native
        // apphost at the .exe path.  The apphost is a small native PE that the OS
        // can directly execute: it bootstraps the .NET runtime and loads the .dll.
        // Without this, `myapp.exe` is a managed-only PE that only the `dotnet`
        // CLI can load — double-clicking or running it from a shell fails silently.
        var managedPath = needsExe ? Path.ChangeExtension(outputPath, ".dll") : outputPath;

        using (var fs = new FileStream(managedPath, FileMode.Create, FileAccess.Write))
            peBlob.WriteContentTo(fs);

        // Sidecar files always sit next to the managed .dll.
        WriteRuntimeConfig(managedPath, profile);
        WriteDepsJson(managedPath);

        if (needsExe)
        {
            // Copy Naja runtime DLLs so the generated exe can resolve them.
            CopyRuntimeDependencies(managedPath);

            // Create the native apphost wrapper at the .exe path.
            bool isGui = profile is CompilationProfile.WinForms or CompilationProfile.Wpf;
            CreateAppHostExe(outputPath, managedPath, isGui);
        }
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
    /// directory that contains <paramref name="managedDllPath"/>.
    /// </summary>
    private static void CopyRuntimeDependencies(string managedDllPath)
    {
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(managedDllPath)) ?? ".";
        var compilerDir = AppContext.BaseDirectory;

        foreach (var dll in _najaRuntimeDlls)
        {
            var src = Path.Combine(compilerDir, dll);
            if (!File.Exists(src)) continue;

            var dst = Path.Combine(outputDir, dll);
            File.Copy(src, dst, overwrite: true);
        }
    }

    // ── Native apphost creation ───────────────────────────────────────────────

    // The .NET SDK embeds a 1024-byte placeholder in apphost.exe.
    // The first bytes spell out this ASCII string; the rest are zeroes.
    // Patching replaces the entire 1024-byte region with the actual
    // managed DLL name (UTF-8, null-terminated, zero-padded).
    private static readonly byte[] _appHostPlaceholder =
        System.Text.Encoding.ASCII.GetBytes("c3ab8ff13720e8ad9047dd39466b3c8974e592c2fa383d4a3960714caef0c4f2");
    private const int AppHostPlaceholderRegionLength = 1024;

    /// <summary>
    /// Creates a native apphost executable at <paramref name="exePath"/> that
    /// bootstraps the .NET runtime and loads the managed <paramref name="dllPath"/>.
    /// This is required so the compiled exe can be launched directly by the OS
    /// without invoking the <c>dotnet</c> CLI.
    /// </summary>
    private static void CreateAppHostExe(string exePath, string dllPath, bool isGui)
    {
        var templatePath = FindAppHostTemplate();
        if (templatePath == null)
        {
            // No SDK apphost template found — fall back to the managed dll so
            // `dotnet myapp.exe` continues to work, and warn the user.
            File.Copy(dllPath, exePath, overwrite: true);
            Console.Error.WriteLine(
                "warning: .NET SDK apphost template not found; '" + Path.GetFileName(exePath) +
                "' requires `dotnet` to run. Install the .NET SDK or set DOTNET_ROOT.");
            return;
        }

        var template = File.ReadAllBytes(templatePath);

        PatchAppHostBinaryName(template, Path.GetFileName(dllPath));

        if (isGui)
            PatchWindowsGuiSubsystem(template);

        File.WriteAllBytes(exePath, template);
    }

    /// <summary>
    /// Replaces the 1024-byte placeholder region in the apphost template with
    /// the actual managed DLL filename (UTF-8, null-terminated, zero-padded).
    /// </summary>
    private static void PatchAppHostBinaryName(byte[] template, string dllFileName)
    {
        var idx = IndexOfBytes(template, _appHostPlaceholder);
        if (idx < 0)
            throw new InvalidOperationException(
                "Apphost template: app binary placeholder not found. " +
                "The .NET SDK may have changed the template format.");

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(dllFileName);
        if (nameBytes.Length >= AppHostPlaceholderRegionLength)
            throw new ArgumentException(
                $"Managed assembly name '{dllFileName}' exceeds the {AppHostPlaceholderRegionLength}-byte apphost limit.");

        // Clear the region, then write the filename (null terminator provided by Array.Clear).
        Array.Clear(template, idx, AppHostPlaceholderRegionLength);
        nameBytes.CopyTo(template, idx);
    }

    /// <summary>
    /// Patches the PE subsystem field in the apphost template from
    /// <c>IMAGE_SUBSYSTEM_WINDOWS_CUI (3)</c> to
    /// <c>IMAGE_SUBSYSTEM_WINDOWS_GUI (2)</c> so WinForms apps run
    /// without a console window.
    /// </summary>
    private static void PatchWindowsGuiSubsystem(byte[] template)
    {
        if (template.Length < 0x40) return;
        var peOffset = BitConverter.ToInt32(template, 0x3C);
        var subsystemOffset = peOffset + 0x5C;
        if (subsystemOffset + 2 > template.Length) return;
        // 0x0002 = IMAGE_SUBSYSTEM_WINDOWS_GUI (little-endian)
        template[subsystemOffset]     = 0x02;
        template[subsystemOffset + 1] = 0x00;
    }

    /// <summary>
    /// Locates the <c>apphost.exe</c> template shipped with the .NET SDK.
    /// Searches <c>DOTNET_ROOT</c>, then the <c>PATH</c> for <c>dotnet.exe</c>.
    /// Returns <c>null</c> when no template can be found.
    /// </summary>
    private static string? FindAppHostTemplate()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (dotnetRoot == null)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (File.Exists(Path.Combine(dir, "dotnet.exe")))
                {
                    dotnetRoot = dir;
                    break;
                }
            }
        }

        if (dotnetRoot == null) return null;

        // Pick the highest SDK version that ships an AppHostTemplate.
        var sdkDir = Path.Combine(dotnetRoot, "sdk");
        if (!Directory.Exists(sdkDir)) return null;

        return Directory.GetDirectories(sdkDir)
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(d => Path.Combine(d, "AppHostTemplate", "apphost.exe"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Simple byte-sequence search (naive scan — apphost templates are small).</summary>
    private static int IndexOfBytes(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}

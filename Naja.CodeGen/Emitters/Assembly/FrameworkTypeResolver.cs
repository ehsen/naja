using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata.Ecma335;

namespace Naja.CodeGen
{
    internal sealed class FrameworkTypeResolver
    {
        private static readonly Dictionary<string, string> _knownTypeToAssembly =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Microsoft.AspNetCore.Builder.WebApplication"] = "Microsoft.AspNetCore",
                ["Microsoft.AspNetCore.Builder.WebApplicationBuilder"] = "Microsoft.AspNetCore",
                ["Microsoft.AspNetCore.Builder.WebApplicationOptions"] = "Microsoft.AspNetCore",
                ["Microsoft.AspNetCore.Builder.IApplicationBuilder"] = "Microsoft.AspNetCore.Http.Abstractions",
                ["Microsoft.AspNetCore.Builder.IEndpointRouteBuilder"] = "Microsoft.AspNetCore.Routing",
                ["Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions"] = "Microsoft.AspNetCore.Routing",
                ["Microsoft.AspNetCore.Http.HttpContext"] = "Microsoft.AspNetCore.Http.Abstractions",
                ["Microsoft.AspNetCore.Http.HttpRequest"] = "Microsoft.AspNetCore.Http.Abstractions",
                ["Microsoft.AspNetCore.Http.HttpResponse"] = "Microsoft.AspNetCore.Http.Abstractions",
                ["Microsoft.AspNetCore.Http.IResult"] = "Microsoft.AspNetCore.Http.Abstractions",
                ["Microsoft.AspNetCore.Http.Results"] = "Microsoft.AspNetCore.Http",
                ["Microsoft.AspNetCore.Http.RequestDelegate"] = "Microsoft.AspNetCore.Http.Abstractions",
                ["Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"] = "Microsoft.AspNetCore.Routing",
                ["Microsoft.Extensions.Hosting.IHost"] = "Microsoft.Extensions.Hosting.Abstractions",
                ["Microsoft.Extensions.Hosting.IHostBuilder"] = "Microsoft.Extensions.Hosting.Abstractions",
                ["Microsoft.Extensions.Hosting.IHostedService"] = "Microsoft.Extensions.Hosting.Abstractions",
                ["Microsoft.Extensions.DependencyInjection.IServiceCollection"] = "Microsoft.Extensions.DependencyInjection.Abstractions",
                ["Microsoft.Extensions.DependencyInjection.IServiceProvider"] = "Microsoft.Extensions.DependencyInjection.Abstractions",
            };

        private readonly Dictionary<string, Type?> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private readonly MetadataLoadContext? _mlc;
        private readonly IReadOnlyList<string> _allDlls;

        public FrameworkTypeResolver()
        {
            var dlls = new List<string>();
            foreach (var packDir in GetPackDirectories())
            {
                if (Directory.Exists(packDir))
                    dlls.AddRange(Directory.GetFiles(packDir, "*.dll", SearchOption.AllDirectories));
            }

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
                    _mlc = null;
                }
            }
        }

        public string? ResolveAssemblyName(string fullyQualifiedTypeName)
        {
            if (_knownTypeToAssembly.TryGetValue(fullyQualifiedTypeName, out var known))
                return known;

            var t = ResolveType(fullyQualifiedTypeName);
            return t?.Assembly.GetName().Name;
        }

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

        private Type? ResolveCore(string typeName)
        {
            if (_mlc is null) return null;
            foreach (var dll in _allDlls)
            {
                try
                {
                    var asm = _mlc.LoadFromAssemblyPath(dll);
                    var t = asm.GetType(typeName, throwOnError: false, ignoreCase: true);
                    if (t is not null) return t;
                }
                catch { }
            }
            return null;
        }

        private static IEnumerable<string> GetPackDirectories()
        {
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? GetDefaultDotnetRoot();
            if (string.IsNullOrEmpty(dotnetRoot) || !Directory.Exists(dotnetRoot)) yield break;
            var packsRoot = Path.Combine(dotnetRoot, "packs");
            if (!Directory.Exists(packsRoot)) yield break;
            string[] packNames = new[]
            {
                "Microsoft.AspNetCore.App.Ref",
                "Microsoft.WindowsDesktop.App.Ref",
                "Microsoft.NETCore.App.Ref",
            };

            foreach (var pack in packNames)
            {
                var packDir = Path.Combine(packsRoot, pack);
                if (Directory.Exists(packDir)) yield return packDir;
            }
        }

        private static string? GetDefaultDotnetRoot()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(pf, "dotnet");
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "/usr/local/share/dotnet";
            return "/usr/share/dotnet";
        }
    }
}

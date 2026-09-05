namespace Naja.CodeGen;

/// <summary>
/// Maps Python stdlib module names to their .NET implementation types and assemblies.
/// Used for import resolution during code generation.
/// 
/// Supports three deployment scenarios:
/// - JIT: Modular assemblies (Naja.StdLib.Core, Text, IO, Data, Time) loaded on-demand
/// - AOT: Trimmed minimal stdlib via IL trimming metadata
/// - WASM: Ultra-lightweight deployments with trimmed assemblies
/// </summary>
public static class StdLibResolver
{
    /// <summary>
    /// Maps module names to (Type name, Assembly name, Category).
    /// Used by compiler to:
    /// 1. Recognize stdlib imports
    /// 2. Generate correct assembly references
    /// 3. Emit proper IL for singleton access
    /// </summary>
    private static readonly Dictionary<string, (string TypeName, string AssemblyName, string Category)> StdLibMap = 
        new()
        {
            // ── CORE module: sys, math, unittest, signal ──────────────────────
            ["sys"] = ("Naja.StdLib.NajaSys", "Naja.StdLib", "Core"),
            ["math"] = ("Naja.StdLib.NajaMath", "Naja.StdLib", "Core"),
            ["unittest"] = ("Naja.StdLib.NajaUnittest", "Naja.StdLib", "Core"),
            ["signal"] = ("Naja.StdLib.NajaSignal", "Naja.StdLib", "Core"),
            
            // ── TIME module: datetime ─────────────────────────────────────────
            ["datetime"] = ("Naja.StdLib.NajaDateTime", "Naja.StdLib", "Time"),
            ["time"] = ("Naja.StdLib.Time.NajaTime", "Naja.StdLib.Time", "Time"),  // Placeholder for future
            
            // ── IO module: os, pathlib ───────────────────────────────────────
            ["os"] = ("Naja.StdLib.NajaOs", "Naja.StdLib", "IO"),
            ["pathlib"] = ("Naja.StdLib.IO.NajaPathlib", "Naja.StdLib.IO", "IO"),  // Placeholder for future
            
            // ── DATA module: json, csv ───────────────────────────────────────
            ["json"] = ("Naja.StdLib.NajaJson", "Naja.StdLib", "Data"),
            ["csv"] = ("Naja.StdLib.Data.NajaCsv", "Naja.StdLib.Data", "Data"),    // Placeholder
            
            // ── TEXT module: re, string ──────────────────────────────────────
            ["re"] = ("Naja.StdLib.NajaRe", "Naja.StdLib", "Text"),
            ["string"] = ("Naja.StdLib.Text.NajaString", "Naja.StdLib.Text", "Text"),  // Placeholder

            // ── IO streams: io ───────────────────────────────────────────────
            ["io"] = ("Naja.StdLib.NajaIo", "Naja.StdLib", "IO"),

            // ── Process management: subprocess ───────────────────────────────
            ["subprocess"] = ("Naja.StdLib.NajaSubprocess", "Naja.StdLib", "IO"),

            // ── Windows-specific: _winapi ────────────────────────────────────
            ["_winapi"] = ("Naja.StdLib.NajaWinapi", "Naja.StdLib", "IO"),

            // ── Phase 2 additions: mmap, uuid, fnmatch, msvcrt, stat ──────────
            ["mmap"] = ("Naja.StdLib.NajaMmap", "Naja.StdLib", "IO"),
            ["uuid"] = ("Naja.StdLib.NajaUuid", "Naja.StdLib", "IO"),
            ["fnmatch"] = ("Naja.StdLib.NajaFnmatch", "Naja.StdLib", "IO"),
            ["msvcrt"] = ("Naja.StdLib.NajaMsvcrt", "Naja.StdLib", "Core"),
            ["stat"] = ("Naja.StdLib.NajaStat", "Naja.StdLib", "Core"),

            // ── Phase 3 additions: shutil, textwrap, ctypes, test.support ──────
            ["shutil"] = ("Naja.StdLib.NajaShutil", "Naja.StdLib", "IO"),
            ["textwrap"] = ("Naja.StdLib.NajaTextwrap", "Naja.StdLib", "Text"),
            ["tempfile"] = ("Naja.StdLib.NajaTempfile", "Naja.StdLib", "IO"),
            ["ctypes"] = ("Naja.StdLib.NajaCTypesModule", "Naja.StdLib", "Core"),
            ["test.support"] = ("Naja.StdLib.NajaTestSupport", "Naja.StdLib", "Core"),
            ["test.support.os_helper"] = ("Naja.StdLib.NajaOsHelper", "Naja.StdLib", "Core"),
            ["test.support.import_helper"] = ("Naja.StdLib.NajaImportHelper", "Naja.StdLib", "Core"),
        };

    /// <summary>
    /// Check if a module name is part of the Naja stdlib.
    /// </summary>
    public static bool IsStdLib(string moduleName)
    {
        return StdLibMap.ContainsKey(moduleName);
    }

    /// <summary>
    /// Resolve the CLR Type for a stdlib module at COMPILE time (emitter-side helper).
    /// Single source of truth shared by NameEmitters, StatementEmitter import binding,
    /// and DynamicCall — one resolution rule for every stdlib module access.
    /// </summary>
    public static System.Type? ResolveModuleType(string moduleName)
    {
        if (!StdLibMap.TryGetValue(moduleName, out var res))
            return null;

        return AppDomain.CurrentDomain.GetAssemblies()
                   .Select(a => a.GetType(res.TypeName, throwOnError: false))
                   .FirstOrDefault(t => t is not null)
               ?? System.Type.GetType($"{res.TypeName}, {res.AssemblyName}", throwOnError: false)
               ?? System.Type.GetType(res.TypeName, throwOnError: false);
    }

    /// <summary>
    /// Resolve the runtime VALUE bound to a stdlib module import:
    /// the singleton Instance when present (os, sys, signal, ...), else the Type
    /// itself for static-only modules (tempfile, test.support). Returns null when
    /// the module's CLR type cannot be loaded — callers treat that as ImportError.
    /// </summary>
    public static object? ResolveModuleValue(string moduleName)
    {
        var t = ResolveModuleType(moduleName);
        if (t is null) return null;
        return t.GetField("Instance",
                   System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
               ?.GetValue(null)
               ?? (object)t;
    }

    /// <summary>
    /// Resolve a stdlib module to its type information.
    /// Returns true if found, false if not a stdlib module.
    /// </summary>
    public static bool TryResolve(
        string moduleName,
        out (string TypeName, string AssemblyName, string Category) result)
    {
        return StdLibMap.TryGetValue(moduleName, out result);
    }

    /// <summary>
    /// Get the fully-qualified type name for a stdlib module.
    /// Throws KeyNotFoundException if not a stdlib module.
    /// </summary>
    public static string GetTypeName(string moduleName)
    {
        return StdLibMap[moduleName].TypeName;
    }

    /// <summary>
    /// Get the assembly name for a stdlib module.
    /// Throws KeyNotFoundException if not a stdlib module.
    /// </summary>
    public static string GetAssemblyName(string moduleName)
    {
        return StdLibMap[moduleName].AssemblyName;
    }

    /// <summary>
    /// Get the category (Core, Time, IO, Data, Text) for a stdlib module.
    /// Used for grouping related modules and managing deployment size.
    /// </summary>
    public static string GetCategory(string moduleName)
    {
        return StdLibMap[moduleName].Category;
    }

    /// <summary>
    /// Get all modules in a specific category.
    /// Useful for bundling assemblies by category in different deployment scenarios.
    /// </summary>
    public static IEnumerable<string> GetModulesByCategory(string category)
    {
        return StdLibMap
            .Where(kvp => kvp.Value.Category == category)
            .Select(kvp => kvp.Key);
    }

    /// <summary>
    /// Get unique assembly names for all stdlib modules.
    /// Used by compiler to determine which assemblies need to be referenced.
    /// </summary>
    public static IEnumerable<string> GetAllAssemblyNames()
    {
        return StdLibMap.Values.Select(v => v.AssemblyName).Distinct();
    }

    /// <summary>
    /// Get unique assembly names for modules in a specific category.
    /// Useful for selective assembly loading in JIT deployments.
    /// </summary>
    public static IEnumerable<string> GetAssembliesByCategory(string category)
    {
        return StdLibMap
            .Where(kvp => kvp.Value.Category == category)
            .Select(kvp => kvp.Value.AssemblyName)
            .Distinct();
    }

    /// <summary>
    /// Get all categories used in the stdlib.
    /// Useful for organizing deployment strategies.
    /// </summary>
    public static IEnumerable<string> GetAllCategories()
    {
        return StdLibMap.Values.Select(v => v.Category).Distinct();
    }

    /// <summary>
    /// Check if module is implemented (has a real implementation).
    /// Placeholder modules return false; real modules return true.
    /// </summary>
    public static bool IsImplemented(string moduleName)
    {
        // Real implementations (Phase 1: sys, math, unittest, signal, subprocess, datetime, os)
        // Phase 2: mmap, uuid, fnmatch, msvcrt, stat
        var implemented = new[] 
        { 
            "sys", "math", "unittest", "signal", "subprocess", "datetime", "os",
            "mmap", "uuid", "fnmatch", "msvcrt", "stat", "_winapi"
        };
        return implemented.Contains(moduleName);
    }

    /// <summary>
    /// Get implementation status for all modules.
    /// Useful for progress tracking and documentation.
    /// </summary>
    public static Dictionary<string, (bool Implemented, string Category)> GetImplementationStatus()
    {
        var result = new Dictionary<string, (bool, string)>();
        foreach (var (module, (_, _, category)) in StdLibMap)
        {
            result[module] = (IsImplemented(module), category);
        }
        return result;
    }
}

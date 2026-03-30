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
            // ── CORE module: sys, math, unittest ──────────────────────────────
            ["sys"] = ("Naja.StdLib.NajaSys", "Naja.StdLib", "Core"),
            ["math"] = ("Naja.StdLib.NajaMath", "Naja.StdLib", "Core"),
            ["unittest"] = ("Naja.StdLib.NajaUnittest", "Naja.StdLib", "Core"),
            
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

            // ── Windows-specific: _winapi ────────────────────────────────────
            ["_winapi"] = ("Naja.StdLib.NajaWinapi", "Naja.StdLib", "IO"),
        };

    /// <summary>
    /// Check if a module name is part of the Naja stdlib.
    /// </summary>
    public static bool IsStdLib(string moduleName)
    {
        return StdLibMap.ContainsKey(moduleName);
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
        // Real implementations
        var implemented = new[] { "sys", "math", "unittest", "datetime", "os" };
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

using System.Reflection;

namespace Naja.CPythonTests.Infrastructure;

/// <summary>
/// Result of compiling a Python file through NajaEngine.TryCompile().
/// On success, <see cref="Assembly"/> contains the in-memory compiled assembly.
/// On failure, <see cref="Errors"/> contains the diagnostic messages.
/// </summary>
public sealed class CompilationResult
{
    public bool                     Success      { get; init; }
    public Assembly?                Assembly     { get; init; }
    public string                   AssemblyName { get; init; } = "";
    public IReadOnlyList<string>    Errors       { get; init; } = [];

    public static CompilationResult Ok(Assembly assembly, string name) =>
        new() { Success = true, Assembly = assembly, AssemblyName = name };

    public static CompilationResult Fail(IEnumerable<string> errors) =>
        new() { Success = false, Errors = errors.ToList() };

    public static CompilationResult Fail(string error) =>
        Fail([error]);
}

using System.Reflection.Emit;
using Naja.Semantics;

namespace Naja.CodeGen;

/// <summary>
/// Manages local variable slots in a single method frame.
///
/// In IL, locals are accessed by slot index (ldloc.0, ldloc.1, ...).
/// This manager maps Python variable names to their slot indices and
/// handles declaring them upfront via ILGenerator.DeclareLocal().
///
/// Python scoping rules:
///   - All locals for a function are declared at the top of the frame
///   - A name is local if it's assigned anywhere in the function body
///   - global/nonlocal declarations override this
/// </summary>
public sealed class LocalsManager
{
    private readonly ILGenerator _il;
    private readonly Dictionary<string, LocalBuilder> _locals = new();

    public LocalsManager(ILGenerator il)
    {
        _il = il;
    }

    /// <summary>
    /// Declare a local variable and return its LocalBuilder.
    /// If already declared, returns the existing one (re-assignment).
    /// </summary>
    public LocalBuilder Declare(string name, Type clrType)
    {
        if (_locals.TryGetValue(name, out var existing))
            return existing;

        var local = _il.DeclareLocal(clrType);
        _locals[name] = local;
        return local;
    }

    /// <summary>
    /// Get an already-declared local. Throws if not found.
    /// </summary>
    public LocalBuilder Get(string name)
    {
        if (_locals.TryGetValue(name, out var local))
            return local;
        throw new CodeGenException($"Local variable '{name}' not declared in this frame");
    }

    /// <summary>
    /// Try to get a local — returns null if not in this frame.
    /// </summary>
    public LocalBuilder? TryGet(string name) =>
        _locals.TryGetValue(name, out var local) ? local : null;

    public bool Contains(string name) => _locals.ContainsKey(name);

    /// <summary>
    /// Emit ldloc for a named local.
    /// </summary>
    public void EmitLoad(string name)
    {
        var local = Get(name);
        _il.Emit(OpCodes.Ldloc, local);
    }

    /// <summary>
    /// Emit stloc for a named local.
    /// </summary>
    public void EmitStore(string name)
    {
        var local = Get(name);
        _il.Emit(OpCodes.Stloc, local);
    }

    public IEnumerable<KeyValuePair<string, LocalBuilder>> All => _locals;

    /// <summary>
    /// Get all local variable names in this frame.
    /// </summary>
    public IEnumerable<string> GetAllNames() => _locals.Keys;
}

namespace Naja.Inference;

/// <summary>
/// The runtime platform this compilation targets.
/// AOT, WASM, and IL2CPP targets automatically force <see cref="CompilationMode.Strict"/>
/// regardless of the user-specified mode, because DynamicCall / reflection cannot
/// exist in the emitted output on those platforms.
/// </summary>
public enum CompilationTarget
{
    /// <summary>
    /// Full .NET JIT (desktop / server / Android via Mono).
    /// DynamicCall and reflection are allowed at runtime.
    /// </summary>
    Jit,

    /// <summary>
    /// .NET NativeAOT.
    /// No Reflection.Emit, no DynamicCall, no open generics at runtime.
    /// Forces <see cref="CompilationMode.Strict"/>.
    /// </summary>
    Aot,

    /// <summary>
    /// Browser WASM or WASI.
    /// No Reflection.Emit, no threading, no raw sockets.
    /// Forces <see cref="CompilationMode.Strict"/>.
    /// </summary>
    Wasm,

    /// <summary>
    /// Unity IL2CPP.
    /// No Reflection.Emit, no open generics, no object[] tuples.
    /// Forces <see cref="CompilationMode.Strict"/>.
    /// </summary>
    Il2Cpp
}

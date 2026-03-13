namespace Naja.Inference;

/// <summary>
/// Compilation-wide settings passed from the CLI into the inference pipeline
/// and forwarded into AssemblyEmitter via EmitContext.
///
/// Construct once per compilation unit and pass through the pipeline unchanged.
/// </summary>
public sealed class CompilationOptions
{
    /// <summary>
    /// The default options: Permissive mode, JIT target.
    /// Produces identical behaviour to the compiler before the mode system was added.
    /// </summary>
    public static readonly CompilationOptions Default = new();

    /// <summary>
    /// The mode explicitly requested by the user (via --strict / --balanced / --permissive).
    /// May be overridden by <see cref="Target"/> — use <see cref="EffectiveMode"/> for decisions.
    /// Default: Permissive (current behaviour, unchanged).
    /// </summary>
    public CompilationMode Mode { get; init; } = CompilationMode.Permissive;

    /// <summary>
    /// The runtime platform being targeted.
    /// Default: Jit.
    /// </summary>
    public CompilationTarget Target { get; init; } = CompilationTarget.Jit;

    /// <summary>
    /// Source file path — included in every diagnostic message so the user
    /// knows which file the error or warning came from.
    /// </summary>
    public string FilePath { get; init; } = "<source>";

    /// <summary>
    /// The mode that actually governs compilation after platform constraints are applied.
    ///
    /// AOT, WASM, and IL2CPP targets force Strict regardless of <see cref="Mode"/>,
    /// because DynamicCall and reflection cannot exist in the output on those platforms.
    /// JIT targets use whatever the user specified.
    /// </summary>
    public CompilationMode EffectiveMode =>
        Target is CompilationTarget.Aot
                or CompilationTarget.Wasm
                or CompilationTarget.Il2Cpp
            ? CompilationMode.Strict
            : Mode;
}

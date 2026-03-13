namespace Naja.Inference;

/// <summary>
/// Controls how the compiler handles expressions that cannot be statically typed
/// by the inference engine.
///
///  Strict      — Unknown type → hard compile error.
///                Zero DynamicCall emitted. Required for AOT, WASM, Unity, iOS.
///                Developer must annotate, use cast(), or use dynamic() explicitly.
///
///  Balanced    — Unknown type → warning + DynamicCall emitted.
///                Output runs on JIT. Not AOT-safe. Default for JIT builds.
///
///  Permissive  — Unknown type → silent DynamicCall. No warnings.
///                Matches current compiler behaviour. Maximum Python compatibility.
/// </summary>
public enum CompilationMode
{
    Strict,
    Balanced,
    Permissive
}

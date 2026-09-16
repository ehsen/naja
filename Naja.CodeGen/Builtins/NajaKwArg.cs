using System.Reflection;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Name-tagged wrapper for a keyword argument passed through the dynamic
/// runtime call paths (DynamicCall / StaticCall). The call emitters wrap
/// Python keyword arguments in this so the runtime binder can bind by
/// parameter name (matching CPython's kwarg semantics) instead of silently
/// dropping the name and binding positionally.
/// </summary>
public sealed class NajaKwArg
{
    public string Name { get; }
    public object? Value { get; }

    public NajaKwArg(string name, object? value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Informational ctor used by the binder for error messages.</summary>
    public override string ToString() => $"{Name}={Value ?? "None"}";

    public static readonly ConstructorInfo Ctor =
        typeof(NajaKwArg).GetConstructor(new[] { typeof(string), typeof(object) })!;

    /// <summary>Split an args array into positional prefix + kwargs.</summary>
    public static (object?[] positional, List<NajaKwArg> kwargs) Split(object?[] args)
    {
        var positional = new List<object?>();
        var kwargs = new List<NajaKwArg>();
        foreach (var a in args)
        {
            if (a is NajaKwArg kw) kwargs.Add(kw);
            else positional.Add(a);
        }
        return (positional.ToArray(), kwargs);
    }
}
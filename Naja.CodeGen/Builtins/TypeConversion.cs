using System.Reflection;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Type conversion utilities for converting Python values to strings, ints, floats, and booleans.
/// These methods match Python's conversion semantics.
/// </summary>
public static class TypeConversion
{
    /// <summary>Convert an object to a Python string representation.</summary>
    public static string ToStr(object? obj) => NajaBuiltins.ToStr(obj);

    /// <summary>Convert an object to a Python boolean value.</summary>
    public static bool ToBool(object? obj) => NajaBuiltins.ToBool(obj);

    /// <summary>Convert an object to a Python integer (long).</summary>
    public static long ToInt(object obj) => NajaBuiltins.ToInt(obj);

    /// <summary>Convert an object to a Python float (double).</summary>
    public static double ToFloat(object obj) => NajaBuiltins.ToFloat(obj);

    /// <summary>Get the repr() string of an object for debugging.</summary>
    public static string Repr(object? obj) => NajaBuiltins.Repr(obj);
}

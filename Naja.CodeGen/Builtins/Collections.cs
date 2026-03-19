using System.Collections;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Collection builtin functions: len, range, enumerate, zip, map, filter, sorted, etc.
/// These mostly delegate to the implementations in NajaBuiltins for backward compatibility,
/// but serve as a documentation of the available collection operations.
/// </summary>
public static class Collections
{
    /// <summary>Return the length of a sequence or collection.</summary>
    public static long Len(object obj) => NajaBuiltins.Len(obj);

    /// <summary>Return an immutable sequence of integers from start to stop.</summary>
    public static System.Collections.Generic.List<object> Range(object[] args) => NajaBuiltins.Range(args);

    /// <summary>Return an enumerate object that yields (index, value) tuples.</summary>
    public static System.Collections.Generic.List<object> Enumerate(object[] args) => NajaBuiltins.Enumerate(args);

    /// <summary>Zip multiple iterables into tuples.</summary>
    public static System.Collections.Generic.List<object> Zip(object[] args) => NajaBuiltins.Zip(args);

    /// <summary>Apply a function to every item of an iterable.</summary>
    public static System.Collections.Generic.List<object> Map(object func, object iterable) => NajaBuiltins.Map(func, iterable);

    /// <summary>Filter an iterable with a function that returns true/false.</summary>
    public static System.Collections.Generic.List<object> Filter(object func, object iterable) => NajaBuiltins.Filter(func, iterable);

    /// <summary>Return True if any element of the iterable is true.</summary>
    public static bool Any(object iterable) => NajaBuiltins.Any(iterable);

    /// <summary>Return True if all elements of the iterable are true.</summary>
    public static bool All(object iterable) => NajaBuiltins.All(iterable);

    /// <summary>Return a sorted list.</summary>
    public static System.Collections.Generic.List<object> Sorted(object obj) => NajaBuiltins.Sorted(obj);

    /// <summary>Create a list from an iterable or arguments.</summary>
    public static System.Collections.Generic.List<object> MakeList(object[] args) => NajaBuiltins.MakeList(args);

    /// <summary>Create a dict from key-value pairs or another dict.</summary>
    public static System.Collections.Generic.Dictionary<object, object> MakeDict(object[] args) => NajaBuiltins.MakeDict(args);
}

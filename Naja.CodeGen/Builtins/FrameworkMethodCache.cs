using System.Reflection;

namespace Naja.CodeGen;

/// <summary>
/// Cached method lookups for frequently-used .NET Framework methods.
/// Eliminates repeated reflection calls for standard CLR methods used in IL generation.
/// This complements NajaBuiltinsMethodCache for system library methods.
/// </summary>
internal static class FrameworkMethodCache
{
    // ── Object methods ────────────────────────────────────────────────────────
    /// <summary>object.ToString() - called for implicit string conversions.</summary>
    public static readonly MethodInfo Object_ToString_Method =
        typeof(object).GetMethod("ToString")!;

    // ── String methods ────────────────────────────────────────────────────────
    /// <summary>string.Concat() - called for string concatenation.</summary>
    public static readonly MethodInfo String_Concat_Method =
        typeof(string).GetMethod("Concat", [typeof(object[])]) ??
        typeof(string).GetMethod("Concat", [typeof(string[])])!;

    // ── Enumerable methods ────────────────────────────────────────────────────
    /// <summary>System.Linq.Enumerable.ToList() - converts IEnumerable to List.</summary>
    public static readonly MethodInfo Enumerable_ToList_Method =
        typeof(System.Linq.Enumerable).GetMethod("ToList")
            ?.MakeGenericMethod(typeof(object))!;

    /// <summary>System.Linq.Enumerable.ToArray() - converts IEnumerable to array.</summary>
    public static readonly MethodInfo Enumerable_ToArray_Method =
        typeof(System.Linq.Enumerable).GetMethod("ToArray")
            ?.MakeGenericMethod(typeof(object))!;

    /// <summary>System.Linq.Enumerable.GetEnumerator() wrapper for iteration.</summary>
    public static readonly MethodInfo Enumerable_GetEnumerator_Method =
        typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
}

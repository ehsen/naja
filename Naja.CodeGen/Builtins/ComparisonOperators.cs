using System.Linq;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Dynamic comparison operators: ==, !=, <, >, <=, >=.
/// Handles equality for containers and dynamic objects.
/// </summary>
public static class ComparisonOperators
{
    /// <summary>
    /// Dynamic equality comparison.
    /// </summary>
    public static bool DynamicEq(object a, object b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;

        // String equality
        if (a is string && b is string) return (string)a == (string)b;

        // List equality (element-wise)
        if (a is System.Collections.Generic.List<object> listA && b is System.Collections.Generic.List<object> listB)
        {
            if (listA.Count != listB.Count) return false;
            for (int i = 0; i < listA.Count; i++)
            {
                if (!DynamicEq(listA[i], listB[i])) return false;
            }
            return true;
        }

        // Tuple equality
        if (a is object[] arrA && b is object[] arrB)
        {
            if (arrA.Length != arrB.Length) return false;
            for (int i = 0; i < arrA.Length; i++)
            {
                if (!DynamicEq(arrA[i], arrB[i])) return false;
            }
            return true;
        }

        // Bytes equality (structural, like Python)
        if (a is byte[] bytesA && b is byte[] bytesB)
            return bytesA.SequenceEqual(bytesB);

        // BigInteger equality (arbitrary-precision ints; not IConvertible)
        if (a is System.Numerics.BigInteger biA)
            return b is System.Numerics.BigInteger biB ? biA == biB : biA == ToBigIntSafe(b);
        if (b is System.Numerics.BigInteger biB2)
            return biB2 == ToBigIntSafe(a);

        // Dict equality
        if (a is System.Collections.Generic.Dictionary<object, object> dictA && 
            b is System.Collections.Generic.Dictionary<object, object> dictB)
        {
            if (dictA.Count != dictB.Count) return false;
            foreach (var key in dictA.Keys)
            {
                if (!dictB.ContainsKey(key)) return false;
                if (!DynamicEq(dictA[key], dictB[key])) return false;
            }
            return true;
        }

        // Set equality
        if (a is System.Collections.Generic.HashSet<object> setA &&
            b is System.Collections.Generic.HashSet<object> setB)
            return setA.SetEquals(setB);

        // Numeric equality
        if (a is IConvertible && b is IConvertible)
        {
            try { return System.Convert.ToDouble(a) == System.Convert.ToDouble(b); }
            catch { }
        }

        // Custom class equality via overridden Equals (delegates to __eq__)
        return a.Equals(b);
    }

    /// <summary>
    /// Dynamic inequality comparison.
    /// </summary>
    public static bool DynamicNotEq(object a, object b)
    {
        return !DynamicEq(a, b);
    }

    /// <summary>
    /// Dynamic less-than comparison.
    /// </summary>
    public static bool DynamicLt(object a, object b)
    {
        if ((a is IConvertible) && (b is IConvertible))
        {
            try { return System.Convert.ToDouble(a) < System.Convert.ToDouble(b); }
            catch { }
        }
        if (a is string sa && b is string sb) return sa.CompareTo(sb) < 0;
        var ltM = a?.GetType().GetMethod("__lt__",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (ltM is not null) return System.Convert.ToBoolean(ltM.Invoke(a, new object[] { b }));
        throw new TypeError($"'<' not supported between instances of '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    /// <summary>
    /// Dynamic less-than-or-equal comparison.
    /// </summary>
    public static bool DynamicLtEq(object a, object b)
    {
        return DynamicLt(a, b) || DynamicEq(a, b);
    }

    /// <summary>
    /// Dynamic greater-than comparison.
    /// </summary>
    public static bool DynamicGt(object a, object b)
    {
        if ((a is IConvertible) && (b is IConvertible))
        {
            try { return System.Convert.ToDouble(a) > System.Convert.ToDouble(b); }
            catch { }
        }
        if (a is string sa && b is string sb) return sa.CompareTo(sb) > 0;
        var gtM = a?.GetType().GetMethod("__gt__",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (gtM is not null) return System.Convert.ToBoolean(gtM.Invoke(a, new object[] { b }));
        throw new TypeError($"'>' not supported between instances of '{a?.GetType().Name}' and '{b?.GetType().Name}'");
    }

    /// <summary>
    /// Dynamic greater-than-or-equal comparison.
    /// </summary>
    public static bool DynamicGtEq(object a, object b)
    {
        return DynamicGt(a, b) || DynamicEq(a, b);
    }

    /// <summary>
    /// Convert an integer-like value to BigInteger for exact comparison.
    /// Returns null when the value is not integer-like.
    /// </summary>
    private static System.Numerics.BigInteger? ToBigIntSafe(object? v) => v switch
    {
        null               => null,
        long l             => (System.Numerics.BigInteger)l,
        int i              => (System.Numerics.BigInteger)i,
        bool bo            => (System.Numerics.BigInteger)(bo ? 1L : 0L),
        double d           => (System.Numerics.BigInteger)d,
        System.Numerics.BigInteger bi => bi,
        _                  => null
    };
}

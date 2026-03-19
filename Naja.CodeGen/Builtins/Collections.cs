using System.Collections;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Collection builtin functions: len, range, enumerate, zip, map, filter, etc.
/// </summary>
public static class Collections
{
    /// <summary>
    /// Return the length of a sequence or collection.
    /// </summary>
    public static long Len(object obj)
    {
        if (obj is null) throw new TypeError("object of type 'NoneType' has no len()");
        if (obj is string s) return s.Length;
        if (obj is System.Collections.Generic.List<object> list) return list.Count;
        if (obj is System.Collections.Generic.Dictionary<object, object> dict) return dict.Count;
        if (obj is System.Collections.Generic.HashSet<object> set) return set.Count;
        if (obj is object[] arr) return arr.Length;
        if (obj is ICollection coll) return coll.Count;

        throw new TypeError($"object of type '{obj.GetType().Name}' has no len()");
    }

    /// <summary>
    /// Return an immutable sequence of integers from start to stop.
    /// </summary>
    public static object Range(params object[] args)
    {
        long start = 0, stop = 0, step = 1;

        if (args.Length == 1)
            stop = Convert.ToInt64(args[0]);
        else if (args.Length == 2)
        {
            start = Convert.ToInt64(args[0]);
            stop = Convert.ToInt64(args[1]);
        }
        else if (args.Length == 3)
        {
            start = Convert.ToInt64(args[0]);
            stop = Convert.ToInt64(args[1]);
            step = Convert.ToInt64(args[2]);
        }
        else
            throw new TypeError($"range expected at most 3 arguments, got {args.Length}");

        if (step == 0) throw new ValueError("range() arg 3 must not be zero");

        var result = new System.Collections.Generic.List<object>();
        if (step > 0)
        {
            for (long i = start; i < stop; i += step)
                result.Add(i);
        }
        else
        {
            for (long i = start; i > stop; i += step)
                result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Return an enumerate object that yields (index, value) tuples.
    /// </summary>
    public static object Enumerate(object iterable, long start = 0)
    {
        var result = new System.Collections.Generic.List<object>();

        if (iterable is string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                var tuple = new object[] { start + i, s[i].ToString() };
                result.Add(tuple);
            }
        }
        else if (iterable is System.Collections.Generic.List<object> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var tuple = new object[] { start + i, list[i] };
                result.Add(tuple);
            }
        }
        else if (iterable is object[] arr)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var tuple = new object[] { start + i, arr[i] };
                result.Add(tuple);
            }
        }
        else if (iterable is IEnumerable ie)
        {
            int i = 0;
            foreach (var item in ie)
            {
                var tuple = new object[] { start + i, item };
                result.Add(tuple);
                i++;
            }
        }

        return result;
    }

    /// <summary>
    /// Zip multiple iterables into tuples.
    /// </summary>
    public static object Zip(params object[] iterables)
    {
        if (iterables.Length == 0) return new System.Collections.Generic.List<object>();

        var enumerators = new IEnumerator[iterables.Length];
        for (int i = 0; i < iterables.Length; i++)
        {
            if (iterables[i] is IEnumerable ie)
                enumerators[i] = ie.GetEnumerator();
            else
                throw new TypeError($"zip argument {i} must be an iterable");
        }

        var result = new System.Collections.Generic.List<object>();
        while (true)
        {
            var values = new object[iterables.Length];
            for (int i = 0; i < enumerators.Length; i++)
            {
                if (!enumerators[i].MoveNext())
                    return result;  // Stop when any iterable is exhausted
                values[i] = enumerators[i].Current;
            }
            result.Add(values);
        }
    }

    /// <summary>
    /// Apply a function to every item of an iterable.
    /// </summary>
    public static object Map(object func, object iterable)
    {
        var result = new System.Collections.Generic.List<object>();

        if (iterable is IEnumerable ie)
        {
            foreach (var item in ie)
            {
                // Call the function with the item
                var mapped = NajaBuiltins.CallCallable(func, new[] { item });
                result.Add(mapped);
            }
        }

        return result;
    }

    /// <summary>
    /// Filter an iterable with a function that returns true/false.
    /// </summary>
    public static object Filter(object func, object iterable)
    {
        var result = new System.Collections.Generic.List<object>();

        if (iterable is IEnumerable ie)
        {
            foreach (var item in ie)
            {
                bool keep;
                if (func is null)
                    keep = NajaBuiltins.ToBool(item);
                else
                    keep = NajaBuiltins.ToBool(NajaBuiltins.CallCallable(func, new[] { item }));

                if (keep) result.Add(item);
            }
        }

        return result;
    }

    /// <summary>
    /// Return True if any element of the iterable is true.
    /// </summary>
    public static bool Any(object iterable)
    {
        if (iterable is IEnumerable ie)
        {
            foreach (var item in ie)
            {
                if (NajaBuiltins.ToBool(item)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Return True if all elements of the iterable are true.
    /// </summary>
    public static bool All(object iterable)
    {
        if (iterable is IEnumerable ie)
        {
            foreach (var item in ie)
            {
                if (!NajaBuiltins.ToBool(item)) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Return a reversed list.
    /// </summary>
    public static object Reversed(object iterable)
    {
        if (iterable is System.Collections.Generic.List<object> list)
        {
            var result = new System.Collections.Generic.List<object>(list);
            result.Reverse();
            return result;
        }
        else if (iterable is object[] arr)
        {
            var result = new System.Collections.Generic.List<object>(arr);
            result.Reverse();
            return result;
        }
        else if (iterable is string s)
        {
            var chars = s.ToCharArray();
            System.Array.Reverse(chars);
            return new string(chars);
        }

        throw new TypeError($"argument to reversed() must be a sequence");
    }

    /// <summary>
    /// Return a sorted list.
    /// </summary>
    public static object Sorted(object iterable, object key = null, object reverse = null)
    {
        var list = new System.Collections.Generic.List<object>();

        if (iterable is IEnumerable ie)
        {
            foreach (var item in ie)
                list.Add(item);
        }

        bool isReverse = reverse != null && NajaBuiltins.ToBool(reverse);
        
        if (key != null)
        {
            // Sort with key function
            list.Sort((a, b) =>
            {
                var keyA = NajaBuiltins.CallCallable(key, new[] { a });
                var keyB = NajaBuiltins.CallCallable(key, new[] { b });
                return Comparer<object>.Default.Compare(keyA, keyB);
            });
        }
        else
        {
            // Default sort
            list.Sort(Comparer<object>.Default);
        }

        if (isReverse) list.Reverse();
        return list;
    }
}

/// <summary>
/// ValueError exception.
/// </summary>
public class ValueError : Exception
{
    public ValueError(string message) : base(message) { }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Collection builtin functions: len, range, enumerate, zip, map, filter, sorted, etc.
/// Independent implementations extracted from NajaBuiltins in Phase 3.
/// </summary>
public static class Collections
{
    // ── len() ─────────────────────────────────────────────────────────────────

    public static long Len(object obj)
    {
        if (obj is null)
            throw new Exception($"object of type 'NoneType' has no len()");

        // Check for Count property first (handles __len__ dunder method)
        var countProp = obj.GetType().GetProperty("Count",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (countProp is not null && countProp.PropertyType == typeof(int))
        {
            return (int)countProp.GetValue(obj)!;
        }

        // Check for __len__ method
        var lenMethod = obj.GetType().GetMethod("__len__",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (lenMethod is not null)
        {
            var result = lenMethod.Invoke(obj, null);
            return Convert.ToInt64(result);
        }

        return obj switch
        {
            string s => s.Length,
            List<object> l => l.Count,
            System.Collections.ICollection c => c.Count,
            System.Collections.IEnumerable e => e.Cast<object>().LongCount(),
            _ => throw new Exception($"object of type '{obj?.GetType().Name}' has no len()")
        };
    }

    // ── range() ───────────────────────────────────────────────────────────────

    public static List<object> Range(object[] args)
    {
        long start = 0, stop, step = 1;

        if (args.Length == 1) stop = Convert.ToInt64(args[0]);
        else if (args.Length == 2) { start = Convert.ToInt64(args[0]); stop = Convert.ToInt64(args[1]); }
        else if (args.Length == 3) { start = Convert.ToInt64(args[0]); stop = Convert.ToInt64(args[1]); step = Convert.ToInt64(args[2]); }
        else throw new Exception("range expected 1-3 arguments");

        var result = new List<object>();
        for (long i = start; step > 0 ? i < stop : i > stop; i += step)
            result.Add((object)i);
        return result;
    }

    // ── Collection constructors ───────────────────────────────────────────────

    public static List<object> MakeList(object[] args)
    {
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e && !(args[0] is string))
            return e.Cast<object>().ToList();
        return args.ToList();
    }

    public static Dictionary<object, object> MakeDict(object[] args)
    {
        var d = new Dictionary<object, object>();
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e)
        {
            if (e is Dictionary<object, object> otherDict)
                foreach (var kv in otherDict) d[kv.Key] = kv.Value;
            else
                foreach (var item in e)
                {
                    if (item is object[] pair && pair.Length == 2)
                        d[pair[0]] = pair[1];
                    else if (item is List<object> listPair && listPair.Count == 2)
                        d[listPair[0]] = listPair[1];
                }
        }
        return d;
    }

    public static System.Collections.Generic.HashSet<object> MakeSet(object[] args)
    {
        var s = new System.Collections.Generic.HashSet<object>();
        if (args.Length == 1 && args[0] is System.Collections.IEnumerable e && !(args[0] is string))
        {
            foreach (var item in e)
                s.Add(item);
        }
        else
        {
            foreach (var item in args)
                s.Add(item);
        }
        return s;
    }

    // ── List methods ──────────────────────────────────────────────────────────

    public static void ListAppend(List<object> l, object item)
        => l.Add(item);

    public static void ListExtend(List<object> l, object items)
        => l.AddRange(((System.Collections.IEnumerable)items).Cast<object>());

    public static void ListInsert(List<object> l, long idx, object item)
        => l.Insert((int)idx, item);

    public static object ListPop(List<object> l, object? idx = null)
    {
        long index = idx is null ? -1 : Convert.ToInt64(idx);
        int i = index < 0 ? l.Count + (int)index : (int)index;
        var v = l[i]; l.RemoveAt(i); return v;
    }

    public static void ListRemove(List<object> l, object item)
        => l.Remove(item);

    public static void ListReverse(List<object> l)
        => l.Reverse();

    public static void ListSort(List<object> l)
        => l.Sort(Comparer<object>.Default);

    public static long ListIndex(List<object> l, object item)
        => l.IndexOf(item);

    public static long ListCount(List<object> l, object item)
        => l.Count(x => NajaBuiltins.Equals(x, item));

    public static List<object> ListCopy(List<object> l)
        => new(l);

    public static void ListClear(List<object> l)
        => l.Clear();

    // ── Dict methods ──────────────────────────────────────────────────────────

    public static List<object> DictKeys(Dictionary<object, object> d)
        => d.Keys.ToList<object>();

    public static List<object> DictValues(Dictionary<object, object> d)
        => d.Values.ToList<object>();

    public static List<object> DictItems(Dictionary<object, object> d)
        => d.Select(kv => (object)new object[] { kv.Key, kv.Value }).ToList();

    public static object? DictGet(Dictionary<object, object> d, object key, object? def = null)
        => d.TryGetValue(key, out var v) ? v : def;

    public static object DictPop(Dictionary<object, object> d, object key, object? def = null)
    {
        if (d.TryGetValue(key, out var v)) { d.Remove(key); return v; }
        if (def is not null) return def;
        throw new Exception($"KeyError: {NajaBuiltins.Repr(key)}");
    }

    public static void DictUpdate(Dictionary<object, object> d, object other)
    {
        if (other is Dictionary<object, object> od)
            foreach (var kv in od) d[kv.Key] = kv.Value;
    }

    public static void DictClear(Dictionary<object, object> d)
        => d.Clear();

    public static Dictionary<object, object> DictCopy(Dictionary<object, object> d)
        => new(d);

    // ── Remaining collection operation facades and helpers ──────────────────────

    /// <summary>Return an enumerate object that yields (index, value) tuples.</summary>
    public static List<object> Enumerate(object[] args)
    {
        var iterable = args[0];
        long start = args.Length > 1 ? Convert.ToInt64(args[1]) : 0;
        var result = new List<object>();
        long i = start;
        foreach (var item in (System.Collections.IEnumerable)iterable)
            result.Add(new object[] { (object)i++, item });
        return result;
    }

    /// <summary>Zip multiple iterables into tuples.</summary>
    public static List<object> Zip(object[] args)
    {
        var iters = args.Select(a =>
            ((System.Collections.IEnumerable)a).GetEnumerator()).ToArray();
        var result = new List<object>();
        while (iters.All(e => e.MoveNext()))
            result.Add(iters.Select(e => e.Current).ToArray());
        return result;
    }

    /// <summary>Convert any iterable to an object list for unpacking (e.g., *args).</summary>
    public static List<object?> UnpackIterable(object? obj)
    {
        if (obj is null) throw new Exception("TypeError: cannot unpack non-iterable None");
        return ((System.Collections.IEnumerable)obj).Cast<object?>().ToList();
    }

    /// <summary>Apply a function to every item of an iterable.</summary>
    public static List<object> Map(object func, object iterable)
    {
        var result = new List<object>();
        var m = func?.GetType().GetMethod("Invoke");
        foreach (var item in (System.Collections.IEnumerable)iterable)
        {
            var r = m is not null
                ? m.Invoke(func, new[] { item })
                : item;
            result.Add(r!);
        }
        return result;
    }

    /// <summary>Filter an iterable with a function that returns true/false.</summary>
    public static List<object> Filter(object func, object iterable)
    {
        var result = new List<object>();
        var m = func?.GetType().GetMethod("Invoke");
        foreach (var item in (System.Collections.IEnumerable)iterable)
        {
            var keep = m is not null
                ? TypeConversion.ToBool(m.Invoke(func, new[] { item })!)
                : TypeConversion.ToBool(item!);
            if (keep) result.Add(item!);
        }
        return result;
    }

    /// <summary>Return True if any element of the iterable is true.</summary>
    public static bool Any(object iterable) =>
        ((System.Collections.IEnumerable)iterable).Cast<object>().Any(x => TypeConversion.ToBool(x!));

    /// <summary>Return True if all elements of the iterable are true.</summary>
    public static bool All(object iterable) =>
        ((System.Collections.IEnumerable)iterable).Cast<object>().All(x => TypeConversion.ToBool(x!));

    /// <summary>Sort an iterable, returning a new sorted list.</summary>
    public static List<object> Sorted(object obj)
    {
        var items = ((System.Collections.IEnumerable)obj).Cast<object>().ToList();
        items.Sort(Comparer<object>.Default);
        return items;
    }

    /// <summary>Reverse an iterable, returning a new reversed list.</summary>
    public static List<object> Reversed(object obj)
    {
        var items = ((System.Collections.IEnumerable)obj).Cast<object>().ToList();
        items.Reverse();
        return items;
    }

    /// <summary>Get a slice of a list for starred unpacking (e.g., a, *rest, b = items).</summary>
    public static List<object?> GetUnpackSlice(List<object?> lst, int start, int endFromEnd)
    {
        int end = lst.Count - endFromEnd;
        int count = end - start;
        if (count <= 0) return new List<object?>();
        return lst.GetRange(start, count);
    }
}

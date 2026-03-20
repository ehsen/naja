using System.Collections;
using System.Reflection;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Iterator protocol implementations and enumerator utilities.
/// Handles conversion of Python iterables to .NET iterators.
/// </summary>
public static class Iterators
{
    /// <summary>Gets an IEnumerator from an object supporting the iterator protocol.</summary>
    public static IEnumerator GetEnumerator(object obj)
    {
        if (obj is IEnumerator er) return er;
        if (obj is IEnumerable e) return e.GetEnumerator();
        throw new Exception($"TypeError: '{obj?.GetType().Name}' object is not iterable");
    }

    /// <summary>
    /// Converts the result of a Python __iter__() call to an IEnumerator.
    /// If the result doesn't implement IEnumerator, wraps it with NajaIteratorAdapter
    /// to call __next__() repeatedly.
    /// </summary>
    public static IEnumerator GetIteratorFromResult(object? iterResult, object self)
    {
        var target = iterResult ?? self;
        if (target is IEnumerator en) return en;
        // target may be a Naja object that has __next__ but doesn't yet implement IEnumerator at the
        // CLR level (e.g. TypeBuilder not finished).  Wrap it.
        return new NajaIteratorAdapter(target);
    }

    /// <summary>
    /// Adapter that wraps a Naja object with __next__() method and exposes it as IEnumerator.
    /// Used for generator/iterator objects whose types are still being built.
    /// </summary>
    private sealed class NajaIteratorAdapter : IEnumerator
    {
        private readonly object _target;
        private object? _current;
        private bool _exhausted;
        private MethodInfo? _nextMethod;

        public NajaIteratorAdapter(object target)
        {
            _target = target;
            _nextMethod = target.GetType().GetMethod("__next__",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
        }

        public object? Current => _current;

        public bool MoveNext()
        {
            if (_exhausted) return false;
            if (_nextMethod is null) { _exhausted = true; return false; }
            try
            {
                _current = _nextMethod.Invoke(_target, null);
                return true;
            }
            catch (TargetInvocationException tie)
            {
                var inner = tie.InnerException;
                if (inner is InvalidOperationException || (inner?.Message?.Contains("StopIteration") == true))
                { _exhausted = true; return false; }
                if (inner is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }
            catch (Exception ex) when (ex.Message.Contains("StopIteration"))
            {
                _exhausted = true; return false;
            }
        }

        public void Reset() => throw new NotSupportedException("Python iterators do not support Reset()");
    }

    /// <summary>Variadic next() entry point: next(iterator) or next(iterator, default).</summary>
    public static object? NextVararg(object[] args)
    {
        if (args.Length == 0)
            throw new Exception("TypeError: next expected at least 1 argument");
        var iterator = args[0];
        if (iterator is null)
            throw new Exception("TypeError: 'NoneType' object is not an iterator. Did you forget to return a generator from your function?");
        var e = (System.Collections.IEnumerator)iterator;
        if (e.MoveNext()) return e.Current;
        if (args.Length > 1) return args[1];
        throw new InvalidOperationException("StopIteration");
    }

    /// <summary>Helper for implementing IEnumerator.MoveNext() using Python __next__ method.</summary>
    public static bool IteratorMoveNext(Func<object> nextMethod, ref object currentValue, ref bool exhausted)
    {
        if (exhausted) return false;

        try
        {
            currentValue = nextMethod();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex.Message == "StopIteration")
        {
            exhausted = true;
            return false;
        }
    }
}

namespace Naja.StdLib;

/// <summary>
/// Python 'unittest.TestCase' base class emulation.
/// Test classes compiled from Python inherit this class when they write
/// 'class MyTest(unittest.TestCase)'.
/// </summary>
public class NajaTestCase
{
    // ── Lifecycle ─────────────────────────────────────────────────────────────
    /// <summary>Called before each test method. Override in subclass.</summary>
    public virtual void setUp() { }

    /// <summary>Called after each test method. Override in subclass.</summary>
    public virtual void tearDown() { }

    /// <summary>Called once before all tests in the class. Override in subclass.</summary>
    public static void setUpClass() { }

    /// <summary>Called once after all tests in the class. Override in subclass.</summary>
    public static void tearDownClass() { }

    // ── Equality ──────────────────────────────────────────────────────────────
    public void assertEqual(object? first, object? second) =>
        assertEqual(first, second, null);

    public void assertEqual(object? first, object? second, object? msg)
    {
        if (!AreEqual(first, second))
            Fail($"{Format(first)} != {Format(second)}", msg);
    }

    public void assertNotEqual(object? first, object? second) =>
        assertNotEqual(first, second, null);

    public void assertNotEqual(object? first, object? second, object? msg)
    {
        if (AreEqual(first, second))
            Fail($"{Format(first)} == {Format(second)}", msg);
    }

    // ── Boolean ───────────────────────────────────────────────────────────────
    public void assertTrue(object? expr) => assertTrue(expr, null);

    public void assertTrue(object? expr, object? msg)
    {
        if (!IsTruthy(expr))
            Fail($"False is not true", msg);
    }

    public void assertFalse(object? expr) => assertFalse(expr, null);

    public void assertFalse(object? expr, object? msg)
    {
        if (IsTruthy(expr))
            Fail($"True is not false", msg);
    }

    // ── Identity / None ───────────────────────────────────────────────────────
    public void assertIsNone(object? obj) => assertIsNone(obj, null);

    public void assertIsNone(object? obj, object? msg)
    {
        if (obj is not null)
            Fail($"{Format(obj)} is not None", msg);
    }

    public void assertIsNotNone(object? obj) => assertIsNotNone(obj, null);

    public void assertIsNotNone(object? obj, object? msg)
    {
        if (obj is null)
            Fail("unexpectedly None", msg);
    }

    public void assertIs(object? first, object? second) => assertIs(first, second, null);

    public void assertIs(object? first, object? second, object? msg)
    {
        if (!ReferenceEquals(first, second) && !AreEqual(first, second))
            Fail($"{Format(first)} is not {Format(second)}", msg);
    }

    public void assertIsNot(object? first, object? second) => assertIsNot(first, second, null);

    public void assertIsNot(object? first, object? second, object? msg)
    {
        if (ReferenceEquals(first, second))
            Fail($"unexpectedly identical: {Format(first)}", msg);
    }

    // ── Containment ───────────────────────────────────────────────────────────
    public void assertIn(object? member, object? container) => assertIn(member, container, null);

    public void assertIn(object? member, object? container, object? msg)
    {
        if (!Contains(container, member))
            Fail($"{Format(member)} not found in {Format(container)}", msg);
    }

    public void assertNotIn(object? member, object? container) => assertNotIn(member, container, null);

    public void assertNotIn(object? member, object? container, object? msg)
    {
        if (Contains(container, member))
            Fail($"{Format(member)} unexpectedly found in {Format(container)}", msg);
    }

    // ── Comparison ────────────────────────────────────────────────────────────
    public void assertGreater(object? a, object? b) => assertGreater(a, b, null);

    public void assertGreater(object? a, object? b, object? msg)
    {
        if (Compare(a, b) <= 0)
            Fail($"{Format(a)} not greater than {Format(b)}", msg);
    }

    public void assertGreaterEqual(object? a, object? b) => assertGreaterEqual(a, b, null);

    public void assertGreaterEqual(object? a, object? b, object? msg)
    {
        if (Compare(a, b) < 0)
            Fail($"{Format(a)} not greater than or equal to {Format(b)}", msg);
    }

    public void assertLess(object? a, object? b) => assertLess(a, b, null);

    public void assertLess(object? a, object? b, object? msg)
    {
        if (Compare(a, b) >= 0)
            Fail($"{Format(a)} not less than {Format(b)}", msg);
    }

    public void assertLessEqual(object? a, object? b) => assertLessEqual(a, b, null);

    public void assertLessEqual(object? a, object? b, object? msg)
    {
        if (Compare(a, b) > 0)
            Fail($"{Format(a)} not less than or equal to {Format(b)}", msg);
    }

    // ── String / sequence ─────────────────────────────────────────────────────
    public void assertAlmostEqual(object? first, object? second) =>
        assertAlmostEqual(first, second, null, null);

    public void assertAlmostEqual(object? first, object? second, object? places) =>
        assertAlmostEqual(first, second, places, null);

    public void assertAlmostEqual(object? first, object? second, object? places, object? msg)
    {
        int p = places is null ? 7 : Convert.ToInt32(places);
        double a = ToDouble(first), b = ToDouble(second);
        double tol = Math.Pow(10, -p);
        if (Math.Abs(a - b) >= tol)
            Fail($"{Format(first)} != {Format(second)} within {p} places", msg);
    }

    public void assertNotAlmostEqual(object? first, object? second) =>
        assertNotAlmostEqual(first, second, null, null);

    public void assertNotAlmostEqual(object? first, object? second, object? places) =>
        assertNotAlmostEqual(first, second, places, null);

    public void assertNotAlmostEqual(object? first, object? second, object? places, object? msg)
    {
        int p = places is null ? 7 : Convert.ToInt32(places);
        double a = ToDouble(first), b = ToDouble(second);
        double tol = Math.Pow(10, -p);
        if (Math.Abs(a - b) < tol)
            Fail($"{Format(first)} == {Format(second)} within {p} places", msg);
    }

    // ── Raises ────────────────────────────────────────────────────────────────
    /// <summary>
    /// assertRaises(ExcType, callable, *args) — synchronous form.
    /// Passes when callable(*args) raises an exception of the given type.
    /// </summary>
    public void assertRaises(object exceptionType, object callable)
        => assertRaises(exceptionType, callable, Array.Empty<object>());

    public void assertRaises(object exceptionType, object callable, params object[] args)
    {
        var expectedType = ResolveExceptionType(exceptionType);
        try
        {
            InvokeCallable(callable, args);
            Fail($"{expectedType?.Name ?? exceptionType?.ToString()} not raised", null);
        }
        catch (Exception ex) when (expectedType is not null && expectedType.IsAssignableFrom(ex.GetType()))
        {
            // Passed — expected exception was raised.
        }
        catch (Exception ex) when (expectedType is null)
        {
            // Any exception satisfies assertRaises(Exception, ...) 
            _ = ex;
        }
    }

    // ── Unconditional pass/fail ────────────────────────────────────────────────
    public void fail() => Fail("", null);
    public void fail(object? msg) => Fail("", msg);

    public void skipTest(object? reason)
        => throw new SkipTestException(reason?.ToString() ?? "");

    public void skipTest()
        => throw new SkipTestException("");

    // ── Internal helpers ──────────────────────────────────────────────────────
    private static bool AreEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        // Numeric cross-type comparison (int == float)
        if (a is long la && b is double db)  return (double)la == db;
        if (a is double da && b is long lb)  return da == (double)lb;
        if (a is long la2 && b is int ib)    return la2 == (long)ib;
        if (a is int ia && b is long lb2)    return (long)ia == lb2;
        return a.Equals(b);
    }

    private static bool IsTruthy(object? v) => v switch
    {
        null           => false,
        bool bo        => bo,
        long l         => l != 0,
        int i          => i != 0,
        double d       => d != 0.0,
        string s       => s.Length > 0,
        System.Collections.ICollection c => c.Count > 0,
        _              => true
    };

    private static bool Contains(object? container, object? member)
    {
        if (container is string s && member is string ms) return s.Contains(ms);
        if (container is System.Collections.IEnumerable e)
        {
            foreach (var item in e)
                if (AreEqual(item, member)) return true;
        }
        return false;
    }

    private static int Compare(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        if (a is long la && b is long lb) return la.CompareTo(lb);
        return ToDouble(a).CompareTo(ToDouble(b));
    }

    private static double ToDouble(object? o) => o switch
    {
        double d => d,
        long l   => (double)l,
        int i    => (double)i,
        float f  => (double)f,
        _        => Convert.ToDouble(o)
    };

    private static string Format(object? v) => v switch
    {
        null   => "None",
        string s => $"'{s}'",
        _      => v.ToString() ?? "None"
    };

    private static Type? ResolveExceptionType(object? exType)
    {
        if (exType is Type t) return t;
        if (exType is string name)
            return Type.GetType(name, throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                       .Select(a => a.GetType(name, false, true))
                       .FirstOrDefault(x => x is not null);
        return null;
    }

    private static object? InvokeCallable(object callable, object[] args)
    {
        if (callable is Delegate d)
            return d.DynamicInvoke(args.Length == 0 ? null : (object)args);
        var callMethod = callable.GetType().GetMethod("__call__")
            ?? callable.GetType().GetMethod("Invoke");
        return callMethod?.Invoke(callable, args);
    }

    private static void Fail(string reason, object? msg)
    {
        var message = msg is not null ? $"{msg}" : reason;
        throw new AssertionException(message);
    }
}

/// <summary>Thrown when a unittest assertion fails.</summary>
public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}

/// <summary>Thrown when a test is skipped via skipTest().</summary>
public sealed class SkipTestException : Exception
{
    public SkipTestException(string reason) : base(reason) { }
}

/// <summary>
/// Python 'unittest' module top-level singleton.
/// Exposes TestCase type reference and the test runner (main()).
/// </summary>
public sealed class NajaUnittest
{
    public static readonly NajaUnittest Instance = new();

    /// <summary>
    /// The NajaTestCase type — used when 'unittest.TestCase' is referenced as a value.
    /// </summary>
    public Type TestCase => typeof(NajaTestCase);

    /// <summary>
    /// unittest.main() — discovers and runs all test methods in the calling assembly.
    /// Prints results in a CPython-compatible format.
    /// </summary>
    public void main(object? module = null, object? exit = null)
    {
        bool shouldExit = exit is null ? true : Convert.ToBoolean(exit);

        int passed = 0, failed = 0, errors = 0, skipped = 0;
        var failures = new List<string>();

        // Discover all NajaTestCase subclasses in all loaded assemblies
        var testClasses = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => t.IsSubclassOf(typeof(NajaTestCase)) && !t.IsAbstract);

        foreach (var cls in testClasses)
        {
            var testMethods = cls.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(m => m.Name.StartsWith("test") && m.GetParameters().Length == 0)
                .OrderBy(m => m.Name);

            foreach (var method in testMethods)
            {
                NajaTestCase? instance = null;
                try
                {
                    instance = (NajaTestCase)Activator.CreateInstance(cls)!;
                    instance.setUp();
                    method.Invoke(instance, null);
                    instance.tearDown();
                    passed++;
                }
                catch (SkipTestException ex)
                {
                    skipped++;
                    Console.Error.WriteLine($"s");
                    _ = ex;
                }
                catch (System.Reflection.TargetInvocationException tie)
                    when (tie.InnerException is AssertionException ae)
                {
                    failed++;
                    failures.Add($"FAIL: {cls.Name}.{method.Name}\n  AssertionError: {ae.Message}");
                    Console.Error.Write("F");
                    try { instance?.tearDown(); } catch { }
                }
                catch (Exception ex)
                {
                    errors++;
                    var inner = ex is System.Reflection.TargetInvocationException tie2 ? tie2.InnerException ?? ex : ex;
                    failures.Add($"ERROR: {cls.Name}.{method.Name}\n  {inner.GetType().Name}: {inner.Message}");
                    Console.Error.Write("E");
                    try { instance?.tearDown(); } catch { }
                }
            }
        }

        int total = passed + failed + errors + skipped;
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Ran {total} test{(total == 1 ? "" : "s")}");
        Console.Error.WriteLine();

        if (failures.Count > 0)
        {
            Console.Error.WriteLine("FAILURES:");
            foreach (var f in failures)
                Console.Error.WriteLine(f);
        }

        bool ok = failed == 0 && errors == 0;
        Console.Error.WriteLine(ok ? "OK" : $"FAILED (failures={failed}, errors={errors})");

        if (shouldExit)
            System.Environment.Exit(ok ? 0 : 1);
    }
}

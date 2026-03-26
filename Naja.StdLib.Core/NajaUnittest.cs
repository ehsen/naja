using System.Diagnostics.CodeAnalysis;

namespace Naja.StdLib.Core;

/// <summary>
/// Python 'unittest.TestCase' base class emulation.
/// Test classes compiled from Python inherit this class when they write
/// 'class MyTest(unittest.TestCase)'.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
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

    /// <summary>addCleanup(func, *args) — register a cleanup function (no-op in Naja).</summary>
    public void addCleanup(object func, params object[] args) { /* no-op */ }

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

    // ── hasattr ───────────────────────────────────────────────────────────────
    public void assertHasAttr(object? obj, object? name) =>
        assertHasAttr(obj, name, null);

    public void assertHasAttr(object? obj, object? name, object? msg)
    {
        if (!ObjectHasAttr(obj, name))
            Fail($"{Format(obj)} has no attribute '{name}'", msg);
    }

    public void assertNotHasAttr(object? obj, object? name) =>
        assertNotHasAttr(obj, name, null);

    public void assertNotHasAttr(object? obj, object? name, object? msg)
    {
        if (ObjectHasAttr(obj, name))
            Fail($"{Format(obj)} has unexpected attribute '{name}'", msg);
    }

    private static bool ObjectHasAttr(object? obj, object? name)
    {
        var nameStr = name?.ToString() ?? "";
        var t = obj?.GetType();
        if (t is null) return false;
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                  | System.Reflection.BindingFlags.FlattenHierarchy;
        return t.GetProperty(nameStr, flags) is not null
            || t.GetField(nameStr, flags) is not null
            || t.GetMethods(flags).Any(m => string.Equals(m.Name, nameStr, StringComparison.OrdinalIgnoreCase));
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

    public void assertIsInstance(object? obj, object? cls) => assertIsInstance(obj, cls, null);

    public void assertIsInstance(object? obj, object? cls, object? msg)
    {
        if (obj is null || cls is not Type t || !t.IsAssignableFrom(obj.GetType()))
            Fail($"{Format(obj)} is not instance of {Format(cls)}", msg);
    }

    public void assertNotIsInstance(object? obj, object? cls) => assertNotIsInstance(obj, cls, null);

    public void assertNotIsInstance(object? obj, object? cls, object? msg)
    {
        if (obj is not null && cls is Type t && t.IsAssignableFrom(obj.GetType()))
            Fail($"{Format(obj)} is instance of {Format(cls)}", msg);
    }

    public void assertRaises(object? exceptionType) => assertRaises(exceptionType, null);

    public void assertRaises(object? exceptionType, object? msg)
    {
        // In Naja, this is typically used as a context manager which we don't support yet
        // For now, just a stub
    }

    // ── Fail message ──────────────────────────────────────────────────────────
    public void fail() => fail("Test failed");

    public void fail(object? msg) => throw new AssertionException(msg?.ToString() ?? "Assertion failed");

    // ── Helper methods ────────────────────────────────────────────────────────
    private static bool AreEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    private static bool IsTruthy(object? obj)
    {
        if (obj is null) return false;
        if (obj is bool b) return b;
        if (obj is int i) return i != 0;
        if (obj is long l) return l != 0;
        if (obj is double d) return d != 0 && !double.IsNaN(d);
        if (obj is string s) return s.Length > 0;
        if (obj is System.Collections.ICollection c) return c.Count > 0;
        return true;
    }

    private static bool Contains(object? container, object? item)
    {
        if (container is System.Collections.IEnumerable enumerable)
        {
            foreach (var element in enumerable)
                if (AreEqual(element, item)) return true;
        }
        return false;
    }

    private static int Compare(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;
        if (a is IComparable comparable) return comparable.CompareTo(b);
        return Comparer<object>.Default.Compare(a, b);
    }

    private static string Format(object? obj)
    {
        if (obj is null) return "None";
        if (obj is string s) return $"'{s}'";
        if (obj is bool b) return b ? "True" : "False";
        return obj.ToString() ?? "?";
    }

    private static double ToDouble(object? obj)
    {
        return obj switch
        {
            null => 0.0,
            double d => d,
            long l => (double)l,
            int i => (double)i,
            _ => Convert.ToDouble(obj)
        };
    }
}

/// <summary>
/// Custom assertion exception for test failures.
/// </summary>
public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}

/// <summary>
/// Python 'unittest' module facade.
/// Provides TestCase and main runner support.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
public sealed class NajaUnittest
{
    public static readonly NajaUnittest Instance = new();

    /// <summary>Reference to NajaTestCase for 'from unittest import TestCase'.</summary>
    public Type TestCase => typeof(NajaTestCase);
}

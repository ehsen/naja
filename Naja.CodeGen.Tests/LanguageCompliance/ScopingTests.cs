using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Pillar 1 — Scoping, Closures, and Variable Resolution.
///
/// Tests the LEGB rule (Local → Enclosing → Global → Builtin),
/// nonlocal/global declarations, closure cell semantics, and the
/// classic late-binding gotcha that differentiates correct Python
/// semantics from naive implementations.
/// </summary>
public sealed class ScopingTests
{
    private static readonly NajaEngine Engine = new();

    private static void Run(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_scoping_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"scope_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());
        Engine.Eval(path);
    }

    // ── LEGB Rule ─────────────────────────────────────────────────────────────

    [Fact]
    public void LEGB_Local_Shadows_Enclosing()
        => Run("""
            def outer():
                x = "enclosing"
                def inner():
                    x = "local"
                    return x
                return inner(), x  # inner sees local, outer still has enclosing
            a, b = outer()
            assert a == "local"
            assert b == "enclosing"
            """);

    [Fact]
    public void LEGB_Enclosing_Read_Without_Nonlocal()
        => Run("""
            def outer():
                x = "from outer"
                def inner():
                    return x  # read-only — no nonlocal needed
                return inner()
            assert outer() == "from outer"
            """);

    [Fact]
    public void LEGB_Global_Read_Without_Global_Keyword()
        => Run("""
            CONSTANT = 42
            def f():
                return CONSTANT  # reads global — no 'global' keyword needed for reads
            assert f() == 42
            """);

    [Fact]
    public void LEGB_Global_Write_Requires_Global_Keyword()
        => Run("""
            g = 0
            def increment():
                global g
                g = g + 1
            increment()
            increment()
            assert g == 2
            """);

    // ── Nonlocal ──────────────────────────────────────────────────────────────

    [Fact]
    public void Nonlocal_WritesPropagateToEnclosingScope()
        => Run("""
            def make_acc():
                total = 0
                def add(n):
                    nonlocal total
                    total = total + n
                    return total
                return add
            acc = make_acc()
            assert acc(5) == 5
            assert acc(3) == 8
            assert acc(2) == 10
            """);

    [Fact]
    public void Nonlocal_ThreeLevelsDeep()
        => Run("""
            def level1():
                x = 0
                def level2():
                    nonlocal x
                    x = 1
                    def level3():
                        nonlocal x
                        x = 2
                    level3()
                level2()
                return x
            assert level1() == 2
            """);

    [Fact]
    public void Nonlocal_IndependentClosureInstances()
        => Run("""
            def make_counter(start=0):
                n = start
                def inc():
                    nonlocal n
                    n = n + 1
                    return n
                return inc

            c1 = make_counter()
            c2 = make_counter(10)
            assert c1() == 1
            assert c1() == 2
            assert c2() == 11
            assert c1() == 3  # c1 is independent from c2
            assert c2() == 12
            """);

    // ── Late Binding (the classic Python gotcha) ──────────────────────────────

    [Fact]
    public void LateBinding_AllLambdasSeeLastValue()
        => Run("""
            funcs = [lambda: i for i in range(5)]
            # All functions close over the SAME 'i' variable
            # After the loop, i == 4
            results = [f() for f in funcs]
            assert results == [4, 4, 4, 4, 4], f"Got {results}"
            """);

    [Fact]
    public void LateBinding_FixedWithDefaultArg()
        => Run("""
            funcs = [lambda i=i: i for i in range(5)]
            # Default arg captures the VALUE at definition time
            results = [f() for f in funcs]
            assert results == [0, 1, 2, 3, 4], f"Got {results}"
            """);

    [Fact]
    public void LateBinding_FixedWithImmediatelyInvokedLambda()
        => Run("""
            # Another fix: wrap in an IIFE to force immediate capture
            funcs = [(lambda x: lambda: x)(i) for i in range(5)]
            results = [f() for f in funcs]
            assert results == [0, 1, 2, 3, 4], f"Got {results}"
            """);

    [Fact]
    public void LateBinding_NestedFunctionAlsoLate()
        => Run("""
            # The same late-binding applies to nested def, not just lambda
            fns = []
            for i in range(3):
                def f():
                    return i
                fns.append(f)
            # All three see i=2
            assert fns[0]() == 2
            assert fns[1]() == 2
            assert fns[2]() == 2
            """);

    // ── Comprehension Scope Isolation ─────────────────────────────────────────

    [Fact]
    public void Comprehension_VariableDoesNotLeak()
        => Run("""
            x = "outer"
            # In Python 3, the loop variable in a comprehension is scoped to the comprehension
            result = [x for x in range(5)]
            assert x == "outer", f"Comprehension leaked x={x}"
            """);

    [Fact]
    public void Comprehension_NestedScopesAreIndependent()
        => Run("""
            i = "outer_i"
            j = "outer_j"
            pairs = [(i, j) for i in range(3) for j in range(3)]
            assert i == "outer_i"
            assert j == "outer_j"
            assert len(pairs) == 9
            """);

    // ── Class Scope Does Not Extend to Methods ────────────────────────────────

    [Fact]
    public void ClassScope_MethodsDoNotSeeClassBody()
        => Run("""
            class Foo:
                x = 42  # class variable

                def get_x_wrong(self):
                    # 'x' here refers to a local or global 'x', NOT the class variable
                    # To access class variable, must use self.x or Foo.x
                    return self.x  # correct way

            f = Foo()
            assert f.get_x_wrong() == 42

            # Class variable is accessible via class name
            assert Foo.x == 42
            """);
}

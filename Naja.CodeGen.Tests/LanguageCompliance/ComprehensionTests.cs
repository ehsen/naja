using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// List, dict, and set comprehensions.
/// Covers: basic comprehensions, filtering, nesting, scope isolation,
/// walrus operator, comprehensions vs generator expressions, and
/// edge cases around variable leakage (Python 3 vs Python 2 semantics).
/// </summary>
public sealed class ComprehensionTests
{
    private static readonly NajaEngine Engine = new();

    private static void Run(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_comprehension_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"comp_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());
        Engine.Eval(path);
    }

    private static void RunFile(string fileName) =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "comprehensions", fileName));

    // ── Full script ───────────────────────────────────────────────────────────

    [Fact]
    public void Comprehensions_FullScript() => RunFile("comprehensions.naja");

    // ── List comprehensions ───────────────────────────────────────────────────

    [Fact]
    public void List_Basic()
        => Run("""
            result = [x * 2 for x in range(5)]
            assert result == [0, 2, 4, 6, 8]
            """);

    [Fact]
    public void List_WithFilter()
        => Run("""
            result = [x for x in range(10) if x % 2 == 0]
            assert result == [0, 2, 4, 6, 8]
            """);

    [Fact]
    public void List_WithMultipleFilters()
        => Run("""
            result = [x for x in range(30) if x % 3 != 0 if x % 5 != 0]
            assert 3 not in result
            assert 5 not in result
            assert 15 not in result
            assert 1 in result
            assert 7 in result
            """);

    [Fact]
    public void List_WithExpression()
        => Run("""
            words = ["hello", "world", "python"]
            result = [w.upper() for w in words]
            assert result == ["HELLO", "WORLD", "PYTHON"]
            """);

    [Fact]
    public void List_OverString()
        => Run("""
            result = [c for c in "hello"]
            assert result == ['h', 'e', 'l', 'l', 'o']
            """);

    [Fact]
    public void List_OverEmptyIterable()
        => Run("""
            result = [x for x in []]
            assert result == []
            """);

    [Fact]
    public void List_WithFunctionCall()
        => Run("""
            def transform(x): return x ** 2 + 1
            result = [transform(x) for x in range(5)]
            assert result == [1, 2, 5, 10, 17]
            """);

    // ── Nested list comprehensions ────────────────────────────────────────────

    [Fact]
    public void List_Nested_TwoLoops_Flat()
        => Run("""
            matrix = [[1, 2, 3], [4, 5, 6], [7, 8, 9]]
            flat = [x for row in matrix for x in row]
            assert flat == [1, 2, 3, 4, 5, 6, 7, 8, 9]
            """);

    [Fact]
    public void List_Nested_TwoLoops_WithFilter()
        => Run("""
            pairs = [(i, j) for i in range(4) for j in range(4) if i != j]
            assert (0, 0) not in pairs
            assert (0, 1) in pairs
            assert len(pairs) == 12  # 4*4 - 4 diagonal
            """);

    [Fact]
    public void List_Nested_ProducesMatrix()
        => Run("""
            grid = [[i * j for j in range(1, 4)] for i in range(1, 4)]
            assert grid[0] == [1, 2, 3]
            assert grid[1] == [2, 4, 6]
            assert grid[2] == [3, 6, 9]
            """);

    [Fact]
    public void List_Nested_ThreeLevels()
        => Run("""
            cube = [[[i + j + k for k in range(2)] for j in range(2)] for i in range(2)]
            assert cube[0][0][0] == 0
            assert cube[1][1][1] == 3
            """);

    // ── Dict comprehensions ───────────────────────────────────────────────────

    [Fact]
    public void Dict_Basic()
        => Run("""
            result = {x: x * x for x in range(5)}
            assert result[0] == 0
            assert result[4] == 16
            assert len(result) == 5
            """);

    [Fact]
    public void Dict_WordLengths()
        => Run("""
            words = ["hello", "world", "foo"]
            result = {w: len(w) for w in words}
            assert result["hello"] == 5
            assert result["foo"] == 3
            """);

    [Fact]
    public void Dict_Invert()
        => Run("""
            original = {"a": 1, "b": 2, "c": 3}
            inverted = {v: k for k, v in original.items()}
            assert inverted[1] == "a"
            assert inverted[3] == "c"
            """);

    [Fact]
    public void Dict_WithFilter()
        => Run("""
            result = {k: v for k, v in {"a": 1, "b": 2, "c": 3}.items() if v > 1}
            assert "a" not in result
            assert result["b"] == 2
            assert result["c"] == 3
            """);

    [Fact]
    public void Dict_Nested()
        => Run("""
            result = {i: {j: i * j for j in range(1, 4)} for i in range(1, 4)}
            assert result[2][3] == 6
            assert result[3][3] == 9
            """);

    [Fact]
    public void Dict_DuplicateKeys_LastWins()
        => Run("""
            # When source has duplicates, last value wins — same as dict()
            pairs = [("a", 1), ("b", 2), ("a", 3)]
            result = {k: v for k, v in pairs}
            assert result["a"] == 3  # last "a" wins
            """);

    // ── Set comprehensions ────────────────────────────────────────────────────

    [Fact]
    public void Set_Basic()
        => Run("""
            result = {x * x for x in range(-3, 4)}
            # squares of -3..3: 9,4,1,0,1,4,9 → unique: {0,1,4,9}
            assert result == {0, 1, 4, 9}
            """);

    [Fact]
    public void Set_DeduplicatesAutomatically()
        => Run("""
            words = ["hello", "world", "hello", "python", "world"]
            lengths = {len(w) for w in words}
            # "hello"=5, "world"=5, "python"=6 → {5, 6}
            assert lengths == {5, 6}
            """);

    [Fact]
    public void Set_WithFilter()
        => Run("""
            result = {x for x in range(20) if x % 3 == 0}
            assert 0 in result
            assert 3 in result
            assert 9 in result
            assert 1 not in result
            """);

    // ── Scope isolation ───────────────────────────────────────────────────────

    [Fact]
    public void Scope_LoopVarDoesNotLeak_List()
        => Run("""
            x = "outer"
            result = [x for x in range(5)]
            assert x == "outer", f"List comprehension leaked x={x!r}"
            assert result == [0, 1, 2, 3, 4]
            """);

    [Fact]
    public void Scope_LoopVarDoesNotLeak_Dict()
        => Run("""
            k = "outer_k"
            v = "outer_v"
            result = {k: v for k, v in [("a", 1), ("b", 2)]}
            assert k == "outer_k"
            assert v == "outer_v"
            """);

    [Fact]
    public void Scope_LoopVarDoesNotLeak_Set()
        => Run("""
            i = "outer"
            result = {i for i in range(3)}
            assert i == "outer"
            """);

    [Fact]
    public void Scope_NestedComprehensions_Independent()
        => Run("""
            i = "outer_i"
            j = "outer_j"
            pairs = [(i, j) for i in range(3) for j in range(3)]
            assert i == "outer_i"
            assert j == "outer_j"
            assert len(pairs) == 9
            """);

    [Fact]
    public void Scope_CanReadOuterVariable()
        => Run("""
            multiplier = 3
            result = [x * multiplier for x in range(5)]
            assert result == [0, 3, 6, 9, 12]
            # multiplier is read (not rebound), so it should be unchanged
            assert multiplier == 3
            """);

    // ── Walrus operator in comprehensions ─────────────────────────────────────

    [Fact]
    public void Walrus_InFilterCondition()
        => Run("""
            # := binds in the ENCLOSING scope, not the comprehension scope
            results = [y for x in range(10) if (y := x * 2) > 10]
            assert results == [12, 14, 16, 18]
            # y is bound in enclosing scope to its last value
            assert y == 18
            """);

    [Fact]
    public void Walrus_AvoidDoubleComputation()
        => Run("""
            # Classic use: compute once, filter, use result
            import math

            def expensive(x):
                return x * x + 1

            results = [y for x in range(10) if (y := expensive(x)) > 10]
            # All y values should be > 10
            assert all(v > 10 for v in results)
            """);

    // ── Comprehension vs generator expression ─────────────────────────────────

    [Fact]
    public void ListVsGenExpr_ListMaterializesImmediately()
        => Run("""
            side_effects = []
            def record(x):
                side_effects.append(x)
                return x

            # List comprehension: evaluates ALL elements immediately
            lst = [record(x) for x in range(3)]
            assert len(side_effects) == 3

            side_effects.clear()

            # Generator expression: evaluates lazily
            gen = (record(x) for x in range(3))
            assert len(side_effects) == 0  # nothing evaluated yet
            list(gen)                        # force evaluation
            assert len(side_effects) == 3
            """);

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Comprehension_OverDict_IteratesKeys()
        => Run("""
            d = {"a": 1, "b": 2, "c": 3}
            keys = [k for k in d]
            assert set(keys) == {"a", "b", "c"}
            """);

    [Fact]
    public void Comprehension_OverDictItems()
        => Run("""
            d = {"a": 1, "b": 2}
            pairs = [(k, v) for k, v in d.items()]
            assert ("a", 1) in pairs
            assert ("b", 2) in pairs
            """);

    [Fact]
    public void Comprehension_OverZip()
        => Run("""
            keys = ["a", "b", "c"]
            vals = [1, 2, 3]
            result = {k: v for k, v in zip(keys, vals)}
            assert result == {"a": 1, "b": 2, "c": 3}
            """);

    [Fact]
    public void Comprehension_ConditionalExpression_InValue()
        => Run("""
            result = [("even" if x % 2 == 0 else "odd") for x in range(5)]
            assert result == ["even", "odd", "even", "odd", "even"]
            """);
}

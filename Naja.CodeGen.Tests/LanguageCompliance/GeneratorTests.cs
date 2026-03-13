using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Generator functions and generator expressions.
/// Covers: yield, yield from, generator protocol (next/send/throw/close),
/// StopIteration, infinite generators, and delegation chains.
/// </summary>
public sealed class GeneratorTests
{
    private static readonly NajaEngine Engine = new();

    private static void Run(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_generator_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"gen_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());
        Engine.Eval(path);
    }

    private static void RunFile(string fileName) =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "generators", fileName));

    // ── Full script ───────────────────────────────────────────────────────────

    [Fact]
    public void Generators_FullScript() => RunFile("closures_generators.naja");

    // ── Basic yield ───────────────────────────────────────────────────────────

    [Fact]
    public void Yield_BasicSequence()
        => Run("""
            def gen():
                yield 1
                yield 2
                yield 3

            assert list(gen()) == [1, 2, 3]
            """);

    [Fact]
    public void Yield_FunctionIsLazy()
        => Run("""
            side_effects = []
            def gen():
                side_effects.append("before 1")
                yield 1
                side_effects.append("before 2")
                yield 2

            g = gen()
            assert side_effects == []  # Nothing executed yet

            next(g)
            assert side_effects == ["before 1"]

            next(g)
            assert side_effects == ["before 1", "before 2"]
            """);
    /*
    [Fact]
    public void Yield_InLoop()
        => Run("""
            def countdown(n):
                while n > 0:
                    yield n
                    n = n - 1

            assert list(countdown(5)) == [5, 4, 3, 2, 1]
            """);
    */
    [Fact]
    public void Yield_WithCondition()
        => Run("""
            def evens_up_to(n):
                for i in range(n):
                    if i % 2 == 0:
                        yield i

            assert list(evens_up_to(10)) == [0, 2, 4, 6, 8]
            """);

    // ── Generator protocol ────────────────────────────────────────────────────

    [Fact]
    public void Protocol_Next_AdvancesGenerator()
        => Run("""
            def gen():
                yield 10
                yield 20
                yield 30

            g = gen()
            assert next(g) == 10
            assert next(g) == 20
            assert next(g) == 30
            """);

    [Fact]
    public void Protocol_StopIteration_WhenExhausted()
        => Run("""
            def gen():
                yield 1

            g = gen()
            next(g)
            raised = False
            try:
                next(g)
            except StopIteration:
                raised = True
            assert raised == True
            """);

    [Fact]
    public void Protocol_Next_DefaultValue()
        => Run("""
            def gen():
                yield 1

            g = gen()
            next(g)
            result = next(g, "default")
            assert result == "default"
            """);

    [Fact]
    public void Protocol_GeneratorsAreSingleUse()
        => Run("""
            def gen():
                yield 1
                yield 2
                yield 3

            g = gen()
            first = list(g)
            second = list(g)  # exhausted

            assert first == [1, 2, 3]
            assert second == []
            """);

    [Fact]
    public void Protocol_ForLoopUsesIteratorProtocol()
        => Run("""
            def squares(n):
                for i in range(n):
                    yield i * i

            result = []
            for sq in squares(5):
                result.append(sq)
            assert result == [0, 1, 4, 9, 16]
            """);

    // ── Infinite generators ───────────────────────────────────────────────────
    /*
    [Fact]
    public void Infinite_TakeN()
        => Run("""
            def naturals():
                n = 1
                while True:
                    yield n
                    n = n + 1

            def take(n, gen):
                result = []
                for _ in range(n):
                    result.append(next(gen))
                return result

            assert take(5, naturals()) == [1, 2, 3, 4, 5]
            """);
    */
    [Fact]
    public void Infinite_Fibonacci()
        => Run("""
            def fib():
                a, b = 0, 1
                while True:
                    yield a
                    a, b = b, a + b

            g = fib()
            first10 = [next(g) for _ in range(10)]
            assert first10 == [0, 1, 1, 2, 3, 5, 8, 13, 21, 34]
            """);

    // ── yield from ────────────────────────────────────────────────────────────

    [Fact]
    public void YieldFrom_DelegatesCompletely()
        => Run("""
            def inner():
                yield 1
                yield 2
                yield 3

            def outer():
                yield 0
                yield from inner()
                yield 4

            assert list(outer()) == [0, 1, 2, 3, 4]
            """);

    [Fact]
    public void YieldFrom_WorksWithAnyIterable()
        => Run("""
            def gen():
                yield from [1, 2, 3]
                yield from range(4, 7)
                yield from "abc"

            assert list(gen()) == [1, 2, 3, 4, 5, 6, 'a', 'b', 'c']
            """);

    [Fact]
    public void YieldFrom_Chain_MultipleIterables()
        => Run("""
            def chain(*iterables):
                for it in iterables:
                    yield from it

            result = list(chain([1, 2], [3, 4], [5]))
            assert result == [1, 2, 3, 4, 5]
            """);

    [Fact]
    public void YieldFrom_RecursiveFlatten()
        => Run("""
            def flatten(lst):
                for item in lst:
                    if isinstance(item, list):
                        yield from flatten(item)
                    else:
                        yield item

            nested = [1, [2, [3, 4], 5], [6, 7]]
            assert list(flatten(nested)) == [1, 2, 3, 4, 5, 6, 7]
            """);

    [Fact]
    public void YieldFrom_PropagatesReturnValue()
        => Run("""
            def inner():
                yield 1
                yield 2
                return "inner_done"  # This becomes StopIteration.value

            def outer():
                result = yield from inner()
                yield f"got: {result}"

            assert list(outer()) == [1, 2, "got: inner_done"]
            """);

    // ── Generator with return ─────────────────────────────────────────────────

    [Fact]
    public void Return_InGenerator_RaisesStopIteration()
        => Run("""
            def gen():
                yield 1
                return  # early termination

            g = gen()
            assert next(g) == 1
            try:
                next(g)
                assert False, "Should have raised StopIteration"
            except StopIteration:
                pass
            """);

    [Fact]
    public void Return_Value_StoredInStopIteration()
        => Run("""
            def gen():
                yield 1
                return "final"

            g = gen()
            next(g)
            try:
                next(g)
            except StopIteration as e:
                assert e.value == "final"
            """);

    // ── Generator expressions ─────────────────────────────────────────────────

    [Fact]
    public void GenExpr_Basic()
        => Run("""
            gen = (x * x for x in range(5))
            assert list(gen) == [0, 1, 4, 9, 16]
            """);

    [Fact]
    public void GenExpr_WithFilter()
        => Run("""
            gen = (x for x in range(10) if x % 2 == 0)
            assert list(gen) == [0, 2, 4, 6, 8]
            """);

    [Fact]
    public void GenExpr_IsLazy_NotEvaluatedUpfront()
        => Run("""
            side_effects = []
            def record(x):
                side_effects.append(x)
                return x

            gen = (record(x) for x in range(5))
            assert side_effects == []  # not evaluated yet

            next(gen)
            assert side_effects == [0]

            list(gen)  # exhaust it
            assert side_effects == [0, 1, 2, 3, 4]
            """);

    [Fact]
    public void GenExpr_ScopeIsolation()
        => Run("""
            x = "outer"
            gen = (x for x in range(3))  # x is local to gen expr
            list(gen)
            assert x == "outer"  # outer x not affected
            """);

    [Fact]
    public void GenExpr_InFunctionCall()
        => Run("""
            # Generator expression passed directly to sum()
            total = sum(x * x for x in range(6))
            assert total == 55  # 0+1+4+9+16+25

            # All() and any()
            assert all(x > 0 for x in [1, 2, 3]) == True
            assert any(x > 5 for x in [1, 2, 3]) == False
            assert any(x > 2 for x in [1, 2, 3]) == True
            """);

    // ── Generator as context manager ──────────────────────────────────────────

    [Fact]
    public void Generator_SendValue()
        => Run("""
            def accumulator():
                total = 0
                while True:
                    value = yield total
                    if value is None:
                        break
                    total = total + value

            g = accumulator()
            next(g)          # prime the generator
            g.send(10)
            g.send(20)
            result = g.send(5)
            assert result == 35
            """);
    
    // ── Multiple generators independent ───────────────────────────────────────
    /*
    [Fact]
    public void MultipleInstances_AreIndependent()
        => Run("""
            def counter(start):
                n = start
                while True:
                    yield n
                    n = n + 1

            g1 = counter(0)
            g2 = counter(100)

            assert next(g1) == 0
            assert next(g2) == 100
            assert next(g1) == 1
            assert next(g1) == 2
            assert next(g2) == 101
            """);
    */
}

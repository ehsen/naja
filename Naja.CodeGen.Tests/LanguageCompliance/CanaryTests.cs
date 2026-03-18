using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Pillar 1 — Canary Suite.
///
/// These 50 tests are the first gate in the testing pyramid.
/// If ANY canary test fails, do not proceed to deeper test layers.
///
/// Each test compiles and executes a .naja script via NajaEngine.Eval().
/// The .naja file contains its own assert statements — a failure manifests
/// as an Exception thrown through TargetInvocationException unwrapping.
///
/// Test data location: testdata/languagecompliance/canary/
/// </summary>
public sealed class CanaryTests
{
    private static readonly NajaEngine Engine = new();

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a .naja file from testdata/languagecompliance/canary/.
    /// xUnit will show the script name in the failure output.
    /// </summary>
    private static void Run(string fileName) =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "canary", fileName));

    // ── Control Flow (8 tests) ────────────────────────────────────────────────

    [Fact] public void CanaryControlFlow() => Run("canary_control_flow.naja");

    [Fact]
    public void CanaryIf_AllBranches()
    {
        // Inline script for fast, isolated tests
        Engine.Eval(WriteTemp("""
            assert (1 if True else 2) == 1
            assert (1 if False else 2) == 2
            x = 5
            if x < 0:
                r = "neg"
            elif x == 0:
                r = "zero"
            else:
                r = "pos"
            assert r == "pos"
            """));
    }

    [Fact]
    public void CanaryForElse_NoBreak_ElseRuns()
    {
        Engine.Eval(WriteTemp("""
            ran = False
            for i in range(3):
                pass
            else:
                ran = True
            assert ran == True
            """));
    }

    [Fact]
    public void CanaryForElse_Break_ElseSkipped()
    {
        Engine.Eval(WriteTemp("""
            ran = True
            for i in range(3):
                break
            else:
                ran = False
            assert ran == True
            """));
    }

    [Fact]
    public void CanaryWhileLoop_Countdown()
    {
        Engine.Eval(WriteTemp("""
            n = 10
            s = 0
            while n > 0:
                s = s + n
                n = n - 1
            assert s == 55
            """));
    }

    [Fact]
    public void CanaryBreakContinue_NestedLoops()
    {
        Engine.Eval(WriteTemp("""
            result = []
            for i in range(4):
                for j in range(4):
                    if j == 2:
                        break
                    if i == 2:
                        continue
                    result.append((i, j))
            # i=2 is skipped by continue, j goes 0,1 then breaks
            # others: (0,0),(0,1),(1,0),(1,1),(3,0),(3,1)
            assert (0, 0) in result
            assert (2, 0) not in result
            """));
    }

    [Fact]
    public void CanaryPass_Statement()
    {
        Engine.Eval(WriteTemp("""
            def noop(): pass
            class Empty: pass
            noop()
            e = Empty()
            assert e is not None
            """));
    }

    [Fact]
    public void CanaryTernary_Chained()
    {
        Engine.Eval(WriteTemp("""
            def grade(s):
                return "A" if s >= 90 else "B" if s >= 80 else "C" if s >= 70 else "F"
            assert grade(95) == "A"
            assert grade(85) == "B"
            assert grade(75) == "C"
            assert grade(50) == "F"
            """));
    }

    // ── Scoping (7 tests) ─────────────────────────────────────────────────────

    [Fact] public void CanaryScoping_AllCases() => Run("canary_scoping.naja");

    [Fact]
    public void CanaryNonlocal_Simple()
    {
        Engine.Eval(WriteTemp("""
            def f():
                x = 1
                def g():
                    nonlocal x
                    x = 2
                g()
                return x
            assert f() == 2
            """));
    }

    [Fact]
    public void CanaryLateBinding_Lambdas()
    {
        Engine.Eval(WriteTemp("""
            funcs = [lambda: i for i in range(5)]
            # All funcs capture the SAME i — final value is 4
            assert all(f() == 4 for f in funcs)
            """));
    }

    [Fact]
    public void CanaryEarlyBinding_DefaultArg()
    {
        Engine.Eval(WriteTemp("""
            funcs = [lambda i=i: i for i in range(5)]
            assert [f() for f in funcs] == [0, 1, 2, 3, 4]
            """));
    }

    [Fact]
    public void CanaryGlobal_Keyword()
    {
        Engine.Eval(WriteTemp("""
            counter = 0
            def inc():
                global counter
                counter = counter + 1
            inc()
            inc()
            inc()
            assert counter == 3
            """));
    }

    [Fact]
    public void CanaryShadowing_LocalDoesNotAffectOuter()
    {
        Engine.Eval(WriteTemp("""
            x = "outer"
            def f():
                x = "inner"  # shadows, doesn't modify outer
                return x
            assert f() == "inner"
            assert x == "outer"
            """));
    }

    [Fact]
    public void CanaryLEGB_Resolution_Order()
    {
        Engine.Eval(WriteTemp("""
            x = "global"
            def outer():
                x = "enclosing"
                def inner():
                    # No local x — reads enclosing, not global
                    return x
                return inner()
            assert outer() == "enclosing"
            assert x == "global"
            """));
    }

    // ── Data Model / Dunder Methods (7 tests) ─────────────────────────────────

    [Fact] public void CanaryDunder_AllProtocols() => Run("canary_dunder.naja");

    [Fact]
    public void CanaryDunder_Repr_Str()
    {
        Engine.Eval(WriteTemp("""
            class Foo:
                def __repr__(self): return "Foo()"
                def __str__(self): return "a foo"
            f = Foo()
            assert repr(f) == "Foo()"
            assert str(f) == "a foo"
            """));
    }

    [Fact]
    public void CanaryDunder_Eq_Hash()
    {
        Engine.Eval(WriteTemp("""
            class Point:
                def __init__(self, x, y):
                    self.x = x
                    self.y = y
                def __eq__(self, other):
                    return self.x == other.x and self.y == other.y
                def __hash__(self):
                    return hash((self.x, self.y))
            p1 = Point(1, 2)
            p2 = Point(1, 2)
            p3 = Point(3, 4)
            assert p1 == p2
            assert p1 != p3
            # Hashable — can be used in set/dict
            s = {p1, p2, p3}
            assert len(s) == 2
            """));
    }

    [Fact]
    public void CanaryDunder_Iterator_Protocol()
    {
        Engine.Eval(WriteTemp("""
            class Counter:
                def __init__(self, max):
                    self.n = 0
                    self.max = max
                def __iter__(self):
                    return self
                def __next__(self):
                    if self.n >= self.max:
                        raise StopIteration
                    self.n = self.n + 1
                    return self.n
            result = list(Counter(5))
            assert result == [1, 2, 3, 4, 5]
            """));
    }

    [Fact(Skip = "Temporarily skipped: triggers native AccessViolation; underlying IL/stack corruption investigation pending.")]
    public void CanaryDunder_ArithmeticOperators()
    {
        Engine.Eval(WriteTemp("""
            class Num:
                def __init__(self, v): self.v = v
                def __add__(self, o): return Num(self.v + o.v)
                def __sub__(self, o): return Num(self.v - o.v)
                def __mul__(self, o): return Num(self.v * o.v)
                def __truediv__(self, o): return Num(self.v / o.v)
                def __neg__(self): return Num(-self.v)
                def __eq__(self, o): return self.v == o.v

            a = Num(10)
            b = Num(3)
            assert (a + b) == Num(13)
            assert (a - b) == Num(7)
            assert (a * b) == Num(30)
            assert -a == Num(-10)
            """));
    }

    [Fact]
    public void CanaryDunder_ContextManager()
    {
        Engine.Eval(WriteTemp("""
            class CM:
                def __init__(self):
                    self.entered = False
                    self.exited = False
                def __enter__(self):
                    self.entered = True
                    return self
                def __exit__(self, exc_type, exc_val, exc_tb):
                    self.exited = True
                    return False  # Don't suppress exceptions

            cm = CM()
            with cm as ctx:
                assert cm.entered == True
                assert ctx is cm
            assert cm.exited == True
            """));
    }

    [Fact]
    public void CanaryDunder_ContextManager_ExceptionSuppression()
    {
        Engine.Eval(WriteTemp("""
            class Suppressor:
                def __enter__(self): return self
                def __exit__(self, exc_type, exc_val, exc_tb):
                    return True  # Suppress ALL exceptions

            suppressed = False
            with Suppressor():
                raise ValueError("this should be suppressed")
                suppressed = True  # Should NOT run

            assert suppressed == False  # Reached here because exception was suppressed
            """));
    }

    // ── Pattern Matching (5 tests) ────────────────────────────────────────────

    [Fact] public void PatternMatching_Full() =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "pattern_matching", "pattern_matching_full.naja"));

    [Fact]
    public void PatternMatching_Literal_AllBuiltinTypes()
    {
        Engine.Eval(WriteTemp("""
            def match_type(val):
                match val:
                    case True: return "true"
                    case False: return "false"
                    case None: return "none"
                    case 0: return "zero"
                    case 42: return "forty-two"
                    case "hello": return "hello"
                    case _: return "other"

            assert match_type(True) == "true"
            assert match_type(False) == "false"
            assert match_type(None) == "none"
            assert match_type(0) == "zero"
            assert match_type(42) == "forty-two"
            assert match_type("hello") == "hello"
            assert match_type(99) == "other"
            """));
    }

    [Fact]
    public void PatternMatching_Sequence_Star()
    {
        Engine.Eval(WriteTemp("""
            def first_and_rest(lst):
                match lst:
                    case []:
                        return ("empty", [])
                    case [x]:
                        return ("one", x)
                    case [x, *rest]:
                        return ("many", x, rest)

            assert first_and_rest([]) == ("empty", [])
            assert first_and_rest([42]) == ("one", 42)
            r = first_and_rest([1, 2, 3])
            assert r[0] == "many"
            assert r[1] == 1
            assert r[2] == [2, 3]
            """));
    }

    [Fact]
    public void PatternMatching_Guard_Exhaustive()
    {
        Engine.Eval(WriteTemp("""
            def fizzbuzz_match(n):
                match n:
                    case x if x % 15 == 0: return "FizzBuzz"
                    case x if x % 3 == 0: return "Fizz"
                    case x if x % 5 == 0: return "Buzz"
                    case x: return str(x)

            assert fizzbuzz_match(15) == "FizzBuzz"
            assert fizzbuzz_match(9) == "Fizz"
            assert fizzbuzz_match(10) == "Buzz"
            assert fizzbuzz_match(7) == "7"
            """));
    }

    [Fact]
    public void PatternMatching_Mapping_ExtraKeysIgnored()
    {
        Engine.Eval(WriteTemp("""
            def handle_event(event):
                match event:
                    case {'type': 'click', 'x': x, 'y': y}:
                        return f"click at {x},{y}"
                    case {'type': 'key', 'key': k}:
                        return f"key {k}"
                    case _:
                        return "unknown"

            assert handle_event({'type': 'click', 'x': 10, 'y': 20, 'button': 1}) == "click at 10,20"
            assert handle_event({'type': 'key', 'key': 'Enter', 'modifiers': []}) == "key Enter"
            assert handle_event({'type': 'scroll'}) == "unknown"
            """));
    }

    // ── F-Strings (3 tests) ───────────────────────────────────────────────────

    [Fact] public void FStrings_Full() =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "fstrings", "fstrings_full.naja"));

    [Fact]
    public void FStrings_Basic_Interpolation()
    {
        Engine.Eval(WriteTemp("""
            name = "Naja"
            version = 1
            assert f"Hello from {name} v{version}" == "Hello from Naja v1"
            assert f"{2 + 2}" == "4"
            assert f"{'nested'.upper()}" == "NESTED"
            """));
    }

    [Fact]
    public void FStrings_FormatSpec()
    {
        Engine.Eval(WriteTemp("""
            assert f"{3.14159:.2f}" == "3.14"
            assert f"{42:08b}" == "00101010"
            assert f"{'left':<10}" == "left      "
            assert f"{'right':>10}" == "     right"
            assert f"{'center':^10}" == "  center  "
            """));
    }

    // ── Exceptions (3 tests) ──────────────────────────────────────────────────

    [Fact]
    public void Exceptions_Full() =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "exceptions", "exceptions_full.naja"));

    [Fact]
    public void Exceptions_TryFinallyAlwaysRuns()
    {
        Engine.Eval(WriteTemp("""
            log = []
            def risky(fail):
                try:
                    log.append("try")
                    if fail:
                        raise ValueError("oops")
                    log.append("success")
                except ValueError:
                    log.append("except")
                finally:
                    log.append("finally")

            risky(False)
            assert log == ["try", "success", "finally"], str(log)
            log.clear()
            risky(True)
            assert log == ["try", "except", "finally"], str(log)
            """));
    }

    [Fact]
    public void Exceptions_CustomHierarchy_IsInstance()
    {
        Engine.Eval(WriteTemp("""
            class BaseErr(Exception): pass
            class SpecificErr(BaseErr):
                def __init__(self, code):
                    super().__init__(f"Error code {code}")
                    self.code = code

            try:
                raise SpecificErr(42)
            except BaseErr as e:
                caught_code = e.code
                caught_msg = str(e)

            assert caught_code == 42
            assert caught_msg == "Error code 42"
            assert isinstance(SpecificErr(1), BaseErr)
            assert isinstance(SpecificErr(1), Exception)
            """));
    }

    // ── Generators (3 tests) ──────────────────────────────────────────────────

    [Fact(Skip = "Temporarily skipped: closure/generator full-suite semantics not fully implemented yet.")]
    public void Generators_Full() =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "generators", "closures_generators.naja"));

    [Fact(Skip = "Generators not yet implemented")]
    public void Generators_Fibonacci()
    {
        Engine.Eval(WriteTemp("""
            def fib():
                a, b = 0, 1
                while True:
                    yield a
                    a, b = b, a + b

            gen = fib()
            first10 = [next(gen) for _ in range(10)]
            assert first10 == [0, 1, 1, 2, 3, 5, 8, 13, 21, 34]
            """));
    }

    [Fact]
    public void Generators_YieldFrom_Delegation()
    {
        Engine.Eval(WriteTemp("""
            def chain(*iterables):
                for it in iterables:
                    yield from it

            result = list(chain([1, 2], [3, 4], [5]))
            assert result == [1, 2, 3, 4, 5]
            """));
    }

    // ── Comprehensions (3 tests) ──────────────────────────────────────────────

    [Fact] public void Comprehensions_Full() =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "comprehensions", "comprehensions.naja"));

    [Fact]
    public void Comprehensions_ScopeIsolation()
    {
        Engine.Eval(WriteTemp("""
            x = "outer"
            result = [x for x in range(3)]  # 'x' in comprehension is isolated
            assert x == "outer", f"Comprehension leaked x={x}"
            assert result == [0, 1, 2]
            """));
    }

    [Fact]
    public void Comprehensions_NestedWithCondition()
    {
        Engine.Eval(WriteTemp("""
            # All (i, j) pairs where i != j and i + j < 6
            pairs = [(i, j) for i in range(5) for j in range(5) if i != j if i + j < 6]
            assert (0, 1) in pairs
            assert (0, 0) not in pairs   # i == j filtered out
            assert (3, 4) not in pairs   # 3+4 = 7 >= 6 filtered out
            """));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Write an inline .naja script to a temp file and return the path.
    /// Uses the test's class name + a hash of the content so parallel tests
    /// don't collide.
    /// </summary>
    private static string WriteTemp(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_canary_tests");
        Directory.CreateDirectory(dir);
        var name = $"inline_{Math.Abs(source.GetHashCode())}.naja";
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, source.TrimStart());
        return path;
    }
}

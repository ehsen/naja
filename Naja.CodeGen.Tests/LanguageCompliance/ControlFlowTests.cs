using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Control flow — if/elif/else, for, while, break, continue, pass,
/// for/else, while/else, nested loops, and the walrus operator in conditions.
/// </summary>
public sealed class ControlFlowTests
{
    private static readonly NajaEngine Engine = new();

    private static void Run(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_controlflow_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"cf_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());
        Engine.Eval(path);
    }

    private static void RunFile(string fileName) =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "canary", fileName));

    // ── Full canary script ────────────────────────────────────────────────────

    [Fact]
    public void ControlFlow_CanaryScript() => RunFile("canary_control_flow.naja");

    // ── if / elif / else ──────────────────────────────────────────────────────

    [Fact]
    public void If_TrueBranchExecutes()
        => Run("""
            if True:
                result = "true"
            else:
                result = "false"
            assert result == "true"
            """);

    [Fact]
    public void If_FalseBranchExecutes()
        => Run("""
            if False:
                result = "true"
            else:
                result = "false"
            assert result == "false"
            """);

    [Fact]
    public void If_ElseIf_Chain()
        => Run("""
            def classify(n):
                if n < 0:   return "negative"
                elif n == 0: return "zero"
                elif n < 10: return "small"
                elif n < 100: return "medium"
                else:        return "large"

            assert classify(-5)  == "negative"
            assert classify(0)   == "zero"
            assert classify(5)   == "small"
            assert classify(50)  == "medium"
            assert classify(500) == "large"
            """);

    [Fact]
    public void If_NoBranchTaken_NoSideEffect()
        => Run("""
            side = False
            if False:
                side = True
            assert side == False
            """);

    [Fact]
    public void If_Truthiness_NonZeroInt()
        => Run("""
            assert (1 if 42 else 0) == 1
            assert (1 if 0 else 0) == 0
            assert (1 if -1 else 0) == 1
            """);

    [Fact]
    public void If_Truthiness_String()
        => Run("""
            assert (1 if "hello" else 0) == 1
            assert (1 if "" else 0) == 0
            """);

    [Fact]
    public void If_Truthiness_List()
        => Run("""
            assert (1 if [1, 2] else 0) == 1
            assert (1 if [] else 0) == 0
            """);

    [Fact]
    public void If_Truthiness_None()
        => Run("""
            assert (1 if None else 0) == 0
            """);

    [Fact]
    public void Ternary_Basic()
        => Run("""
            x = 10
            result = "yes" if x > 5 else "no"
            assert result == "yes"
            """);

    [Fact]
    public void Ternary_Chained()
        => Run("""
            def sign(n):
                return "pos" if n > 0 else "neg" if n < 0 else "zero"
            assert sign(5)  == "pos"
            assert sign(-3) == "neg"
            assert sign(0)  == "zero"
            """);

    [Fact]
    public void Ternary_InExpression()
        => Run("""
            items = [1, 2, 3]
            msg = f"{len(items)} item{'s' if len(items) != 1 else ''}"
            assert msg == "3 items"
            """);

    // ── for loops ─────────────────────────────────────────────────────────────

    [Fact]
    public void For_Range_Basic()
        => Run("""
            total = 0
            for i in range(5):
                total = total + i
            assert total == 10
            """);

    [Fact]
    public void For_Range_StartStop()
        => Run("""
            result = []
            for i in range(2, 7):
                result.append(i)
            assert result == [2, 3, 4, 5, 6]
            """);

    [Fact]
    public void For_Range_WithStep()
        => Run("""
            result = []
            for i in range(0, 10, 2):
                result.append(i)
            assert result == [0, 2, 4, 6, 8]
            """);

    [Fact]
    public void For_Range_Negative_Step()
        => Run("""
            result = []
            for i in range(5, 0, -1):
                result.append(i)
            assert result == [5, 4, 3, 2, 1]
            """);

    [Fact]
    public void For_OverList()
        => Run("""
            items = [10, 20, 30]
            result = []
            for x in items:
                result.append(x * 2)
            assert result == [20, 40, 60]
            """);

    [Fact]
    public void For_OverString()
        => Run("""
            chars = []
            for c in "hello":
                chars.append(c)
            assert chars == ['h', 'e', 'l', 'l', 'o']
            """);

    [Fact]
    public void For_OverDict_IteratesKeys()
        => Run("""
            d = {"a": 1, "b": 2}
            keys = []
            for k in d:
                keys.append(k)
            assert set(keys) == {"a", "b"}
            """);

    [Fact]
    public void For_UnpackingTuple()
        => Run("""
            pairs = [(1, 'a'), (2, 'b'), (3, 'c')]
            result = []
            for num, letter in pairs:
                result.append(f"{num}{letter}")
            assert result == ["1a", "2b", "3c"]
            """);

    [Fact]
    public void For_Enumerate()
        => Run("""
            words = ["foo", "bar", "baz"]
            result = []
            for i, w in enumerate(words):
                result.append((i, w))
            assert result == [(0, "foo"), (1, "bar"), (2, "baz")]
            """);

    [Fact]
    public void For_Zip()
        => Run("""
            keys = ["a", "b", "c"]
            vals = [1, 2, 3]
            result = {}
            for k, v in zip(keys, vals):
                result[k] = v
            assert result == {"a": 1, "b": 2, "c": 3}
            """);

    // ── while loops ───────────────────────────────────────────────────────────

    [Fact]
    public void While_Basic()
        => Run("""
            n = 5
            total = 0
            while n > 0:
                total = total + n
                n = n - 1
            assert total == 15
            assert n == 0
            """);

    [Fact]
    public void While_FalseCondition_NeverRuns()
        => Run("""
            ran = False
            while False:
                ran = True
            assert ran == False
            """);

    [Fact]
    public void While_MutatesConditionVariable()
        => Run("""
            i = 0
            while i < 5:
                i = i + 1
            assert i == 5
            """);

    // ── break ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Break_ExitsImmediately()
        => Run("""
            result = []
            for i in range(10):
                if i == 5:
                    break
                result.append(i)
            assert result == [0, 1, 2, 3, 4]
            assert i == 5
            """);

    [Fact]
    public void Break_InNestedLoop_OnlyInnerLoop()
        => Run("""
            outer_count = 0
            for i in range(3):
                outer_count = outer_count + 1
                for j in range(10):
                    if j == 2:
                        break  # only breaks inner
            assert outer_count == 3  # outer ran all 3 iterations
            """);

    [Fact]
    public void Break_InWhile()
        => Run("""
            i = 0
            while True:
                i = i + 1
                if i == 7:
                    break
            assert i == 7
            """);

    // ── continue ──────────────────────────────────────────────────────────────

    [Fact]
    public void Continue_SkipsRestOfBody()
        => Run("""
            result = []
            for i in range(10):
                if i % 2 != 0:
                    continue
                result.append(i)
            assert result == [0, 2, 4, 6, 8]
            """);

    [Fact]
    public void Continue_InWhile()
        => Run("""
            i = 0
            result = []
            while i < 10:
                i = i + 1
                if i % 3 == 0:
                    continue
                result.append(i)
            # 1..10 excluding multiples of 3 (3,6,9)
            assert result == [1, 2, 4, 5, 7, 8, 10]
            """);

    // ── for/else and while/else ───────────────────────────────────────────────

    [Fact]
    public void ForElse_ElseRunsWhenNoBreak()
        => Run("""
            found = True
            for i in range(5):
                pass  # no break
            else:
                found = False
            assert found == False
            """);

    [Fact]
    public void ForElse_ElseSkippedWhenBreak()
        => Run("""
            else_ran = False
            for i in range(5):
                if i == 3:
                    break
            else:
                else_ran = True
            assert else_ran == False
            """);

    [Fact]
    public void ForElse_PrimeDetection()
        => Run("""
            def is_prime(n):
                if n < 2: return False
                for i in range(2, n):
                    if n % i == 0:
                        return False
                        break
                else:
                    return True
                return False

            primes = [x for x in range(2, 20) if is_prime(x)]
            assert primes == [2, 3, 5, 7, 11, 13, 17, 19]
            """);

    [Fact]
    public void WhileElse_ElseRunsOnNormalExit()
        => Run("""
            n = 3
            else_ran = False
            while n > 0:
                n = n - 1
            else:
                else_ran = True
            assert else_ran == True
            """);

    [Fact]
    public void WhileElse_ElseSkippedOnBreak()
        => Run("""
            else_ran = False
            i = 0
            while i < 10:
                if i == 5:
                    break
                i = i + 1
            else:
                else_ran = True
            assert else_ran == False
            """);

    // ── pass ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Pass_InIf()
        => Run("""
            if True:
                pass
            assert True  # just reaches here without error
            """);

    [Fact]
    public void Pass_InFor()
        => Run("""
            for i in range(3):
                pass
            assert i == 2
            """);

    [Fact]
    public void Pass_InFunctionAndClass()
        => Run("""
            def noop(): pass
            class Empty: pass

            noop()
            e = Empty()
            assert e is not None
            """);

    // ── Walrus operator in conditions ─────────────────────────────────────────

    [Fact]
    public void Walrus_InWhileCondition()
        => Run("""
            data = [1, 2, 3, 0, 4]
            index = 0
            result = []
            while (val := data[index]) != 0:
                result.append(val)
                index = index + 1
            assert result == [1, 2, 3]
            assert val == 0  # walrus bound in enclosing scope
            """);

    [Fact]
    public void Walrus_InIfCondition()
        => Run("""
            import re
            text = "price: 42 dollars"
            if (m := re.search(r'\d+', text)):
                found = m.group()
            else:
                found = None
            assert found == "42"
            """);

    // ── Nested loops ──────────────────────────────────────────────────────────

    [Fact]
    public void Nested_ThreeLevels()
        => Run("""
            count = 0
            for i in range(3):
                for j in range(3):
                    for k in range(3):
                        count = count + 1
            assert count == 27
            """);

    [Fact]
    public void Nested_EarlyExitWithFlag()
        => Run("""
            # Python has no labeled break — use flag pattern
            found = False
            target = (2, 3)
            for i in range(5):
                if found: break
                for j in range(5):
                    if (i, j) == target:
                        found = True
                        break
            assert found == True
            """);
}

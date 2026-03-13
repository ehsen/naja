using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// PEP 634 — Structural Pattern Matching.
/// Covers all pattern types: literal, capture, wildcard, OR, AS, class,
/// sequence (with star), mapping, nested, and guards.
/// </summary>
public sealed class PatternMatchingTests
{
    private static readonly NajaEngine Engine = new();

    private static void Run(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_pattern_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"pm_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());
        Engine.Eval(path);
    }

    private static void RunFile(string fileName) =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "pattern_matching", fileName));

    // ── Full script ───────────────────────────────────────────────────────────

    [Fact]
    public void PatternMatching_FullScript() => RunFile("pattern_matching_full.naja");

    // ── Literal patterns ──────────────────────────────────────────────────────

    [Fact]
    public void Literal_Integer()
        => Run("""
            match 42:
                case 42: result = "hit"
                case _:  result = "miss"
            assert result == "hit"
            """);

    [Fact]
    public void Literal_String()
        => Run("""
            match "hello":
                case "hello": result = "hit"
                case _:       result = "miss"
            assert result == "hit"
            """);

    [Fact]
    public void Literal_None_True_False_AreNotCaptures()
        => Run("""
            # None/True/False are literal patterns, NOT capture variables
            match None:
                case None: r = "none"
                case _:    r = "other"
            assert r == "none"

            match True:
                case True:  r = "true"
                case False: r = "false"
            assert r == "true"

            match False:
                case True:  r = "true"
                case False: r = "false"
            assert r == "false"
            """);

    [Fact]
    public void Literal_NoMatch_FallsThrough()
        => Run("""
            matched = False
            match "xyz":
                case "abc": matched = True
            assert matched == False
            """);

    [Fact]
    public void Literal_FirstMatchWins()
        => Run("""
            match 1:
                case 1: result = "first"
                case 1: result = "second"  # unreachable but valid syntax
            assert result == "first"
            """);

    // ── Wildcard ──────────────────────────────────────────────────────────────

    [Fact]
    public void Wildcard_MatchesAnything()
        => Run("""
            for val in [1, "str", None, [], True, 3.14]:
                match val:
                    case _: hit = True
                assert hit == True
            """);

    [Fact]
    public void Wildcard_DoesNotBind()
        => Run("""
            _ = "before"
            match 42:
                case _: pass
            # _ should remain "before" — wildcard does not rebind _
            assert _ == "before"
            """);

    // ── Capture patterns ──────────────────────────────────────────────────────

    [Fact]
    public void Capture_BindsValue()
        => Run("""
            match 99:
                case x: captured = x
            assert captured == 99
            """);

    [Fact]
    public void Capture_InSequence()
        => Run("""
            match [1, 2, 3]:
                case [a, b, c]:
                    assert a == 1
                    assert b == 2
                    assert c == 3
            """);

    // ── OR patterns ───────────────────────────────────────────────────────────

    [Fact]
    public void Or_MatchesAnyAlternative()
        => Run("""
            def vowel(c):
                match c:
                    case 'a' | 'e' | 'i' | 'o' | 'u': return True
                    case _: return False

            assert vowel('a') == True
            assert vowel('e') == True
            assert vowel('b') == False
            """);

    [Fact]
    public void Or_WithNumbers()
        => Run("""
            def is_weekend(day):
                match day:
                    case 6 | 7: return True
                    case _: return False

            assert is_weekend(6) == True
            assert is_weekend(7) == True
            assert is_weekend(1) == False
            """);

    [Fact]
    public void Or_AllAlternativesMustBindSameNames()
        => Run("""
            # When OR pattern has captures, all alternatives must bind the same name
            match (0, 5):
                case (0, x) | (x, 0): zero_pos = x
                case _: zero_pos = -1
            assert zero_pos == 5
            """);

    // ── AS patterns ───────────────────────────────────────────────────────────

    [Fact]
    public void As_BindsWholeMatch()
        => Run("""
            match [1, 2, 3]:
                case [_, _, _] as lst:
                    bound = lst
            assert bound == [1, 2, 3]
            """);

    [Fact]
    public void As_WithOrPattern()
        => Run("""
            match 5:
                case (1 | 2 | 3 | 4 | 5) as n:
                    captured = n
            assert captured == 5
            """);

    // ── Guard (if clause) ─────────────────────────────────────────────────────

    [Fact]
    public void Guard_PassesWhenTrue()
        => Run("""
            match 15:
                case x if x > 10: result = "big"
                case _:           result = "small"
            assert result == "big"
            """);

    [Fact]
    public void Guard_FailsWhenFalse_TriesNextCase()
        => Run("""
            match 5:
                case x if x > 10: result = "big"
                case x:           result = f"small:{x}"
            assert result == "small:5"
            """);

    [Fact]
    public void Guard_FizzBuzz()
        => Run("""
            def fizzbuzz(n):
                match n:
                    case x if x % 15 == 0: return "FizzBuzz"
                    case x if x % 3 == 0:  return "Fizz"
                    case x if x % 5 == 0:  return "Buzz"
                    case x:                return str(x)

            assert fizzbuzz(15) == "FizzBuzz"
            assert fizzbuzz(9)  == "Fizz"
            assert fizzbuzz(10) == "Buzz"
            assert fizzbuzz(7)  == "7"
            """);

    // ── Sequence patterns ─────────────────────────────────────────────────────

    [Fact]
    public void Sequence_ExactLength()
        => Run("""
            match [1, 2]:
                case [a, b]: result = (a, b)
                case _:      result = None
            assert result == (1, 2)
            """);

    [Fact]
    public void Sequence_StarCapture_Head()
        => Run("""
            match [1, 2, 3, 4, 5]:
                case [first, *rest]:
                    assert first == 1
                    assert rest == [2, 3, 4, 5]
            """);

    [Fact]
    public void Sequence_StarCapture_Tail()
        => Run("""
            match [1, 2, 3, 4, 5]:
                case [*head, last]:
                    assert head == [1, 2, 3, 4]
                    assert last == 5
            """);

    [Fact]
    public void Sequence_StarCapture_Middle()
        => Run("""
            match [1, 2, 3, 4, 5]:
                case [first, *middle, last]:
                    assert first == 1
                    assert middle == [2, 3, 4]
                    assert last == 5
            """);

    [Fact]
    public void Sequence_StarDiscard()
        => Run("""
            match [1, 2, 3, 4, 5]:
                case [first, *_]:
                    assert first == 1
            """);

    [Fact]
    public void Sequence_EmptyList()
        => Run("""
            match []:
                case []: result = "empty"
                case _:  result = "nonempty"
            assert result == "empty"
            """);

    [Fact]
    public void Sequence_SingleElement()
        => Run("""
            match [42]:
                case [x]: result = x
                case _:   result = -1
            assert result == 42
            """);

    [Fact]
    public void Sequence_LengthMismatch_NoMatch()
        => Run("""
            match [1, 2, 3]:
                case [a, b]: result = "two"
                case _:      result = "other"
            assert result == "other"
            """);

    // ── Mapping patterns ──────────────────────────────────────────────────────

    [Fact]
    public void Mapping_ExactKeys()
        => Run("""
            match {'x': 1, 'y': 2}:
                case {'x': a, 'y': b}:
                    assert a == 1
                    assert b == 2
            """);

    [Fact]
    public void Mapping_ExtraKeysIgnored()
        => Run("""
            # Mapping pattern is open — extra keys are fine
            match {'a': 1, 'b': 2, 'c': 3}:
                case {'a': val}: assert val == 1
            """);

    [Fact]
    public void Mapping_NoMatch_MissingKey()
        => Run("""
            match {'x': 1}:
                case {'x': a, 'y': b}: result = "matched"
                case _:                result = "no match"
            assert result == "no match"
            """);

    [Fact]
    public void Mapping_EventDispatch()
        => Run("""
            def handle(event):
                match event:
                    case {'type': 'click', 'x': x, 'y': y}:
                        return f"click@{x},{y}"
                    case {'type': 'key', 'key': k}:
                        return f"key:{k}"
                    case {'type': t}:
                        return f"unknown:{t}"
                    case _:
                        return "no type"

            assert handle({'type': 'click', 'x': 10, 'y': 20}) == "click@10,20"
            assert handle({'type': 'key', 'key': 'Enter'})      == "key:Enter"
            assert handle({'type': 'scroll'})                   == "unknown:scroll"
            assert handle({})                                   == "no type"
            """);

    // ── Class patterns ────────────────────────────────────────────────────────

    [Fact]
    public void Class_KeywordPattern()
        => Run("""
            class Point:
                def __init__(self, x, y):
                    self.x = x
                    self.y = y

            p = Point(3, 4)
            match p:
                case Point(x=px, y=py):
                    assert px == 3
                    assert py == 4
            """);

    [Fact]
    public void Class_PositionalPattern_MatchArgs()
        => Run("""
            class Point:
                def __init__(self, x, y):
                    self.x = x
                    self.y = y
                __match_args__ = ('x', 'y')

            match Point(1, 2):
                case Point(1, py): assert py == 2
            """);

    [Fact]
    public void Class_InheritanceCheck()
        => Run("""
            class Animal:
                def __init__(self, name): self.name = name

            class Dog(Animal):
                def __init__(self, name, breed):
                    super().__init__(name)
                    self.breed = breed

            d = Dog("Rex", "Lab")
            match d:
                case Animal(name=n): animal_name = n
            assert animal_name == "Rex"
            """);

    // ── Nested patterns ───────────────────────────────────────────────────────

    [Fact]
    public void Nested_DictOfDict()
        => Run("""
            match {'user': {'name': 'Alice', 'role': 'admin'}}:
                case {'user': {'name': name, 'role': 'admin'}}:
                    admin_name = name
                case _:
                    admin_name = None
            assert admin_name == "Alice"
            """);

    [Fact]
    public void Nested_SequenceOfSequence()
        => Run("""
            matrix = [[1, 2], [3, 4]]
            match matrix:
                case [[a, b], [c, d]]:
                    assert a == 1
                    assert d == 4
            """);

    [Fact]
    public void Nested_MixedPatterns()
        => Run("""
            cmd = {'action': 'move', 'args': [10, 20]}
            match cmd:
                case {'action': 'move', 'args': [dx, dy]}:
                    assert dx == 10
                    assert dy == 20
            """);

    // ── Match statement exhaustiveness ────────────────────────────────────────

    [Fact]
    public void Match_NoArmMatches_NoEffect()
        => Run("""
            side_effect = False
            match "unmatched_value":
                case "something_else":
                    side_effect = True
            assert side_effect == False
            """);

    [Fact]
    public void Match_ReturnsFromFunction()
        => Run("""
            def describe(val):
                match val:
                    case 0:       return "zero"
                    case int():   return "int"
                    case str():   return "str"
                    case list():  return "list"
                    case _:       return "other"

            assert describe(0)      == "zero"
            assert describe(42)     == "int"
            assert describe("hi")   == "str"
            assert describe([])     == "list"
            assert describe(3.14)   == "other"
            """);
}

using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// F-string tests — PEP 498 (basic), PEP 701 (nested f-strings, Python 3.12+).
/// Covers: basic interpolation, format specs, conversion flags (!r !s !a),
/// nested f-strings up to 3 levels, dict access, conditional expressions,
/// and edge cases around braces and empty strings.
/// </summary>
public sealed class FStringTests
{
    private static readonly NajaEngine Engine = new();

    private static void Run(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_fstring_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fstr_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());
        Engine.Eval(path);
    }

    private static void RunFile(string fileName) =>
        Engine.Eval(Path.Combine("testdata", "languagecompliance", "fstrings", fileName));

    // ── Full script ───────────────────────────────────────────────────────────

    [Fact]
    public void FStrings_FullScript() => RunFile("fstrings_full.naja");

    // ── Basic interpolation ───────────────────────────────────────────────────

    [Fact]
    public void Basic_VariableInterpolation()
        => Run("""
            name = "Naja"
            assert f"Hello, {name}!" == "Hello, Naja!"
            """);

    [Fact]
    public void Basic_ExpressionInterpolation()
        => Run("""
            assert f"{2 + 2}" == "4"
            assert f"{10 * 3.14:.2f}" == "31.40"
            assert f"{'hello'.upper()}" == "HELLO"
            """);

    [Fact]
    public void Basic_MultipleExpressions()
        => Run("""
            x, y = 3, 4
            assert f"{x} + {y} = {x + y}" == "3 + 4 = 7"
            """);

    [Fact]
    public void Basic_IntegerTypes()
        => Run("""
            n = 42
            assert f"{n}" == "42"
            assert f"{-n}" == "-42"
            assert f"{0}" == "0"
            """);

    [Fact]
    public void Basic_FloatTypes()
        => Run("""
            assert f"{3.14}" == "3.14"
            assert f"{-2.5}" == "-2.5"
            """);

    [Fact]
    public void Basic_BoolInterpolation()
        => Run("""
            assert f"{True}" == "True"
            assert f"{False}" == "False"
            """);

    [Fact]
    public void Basic_NoneInterpolation()
        => Run("""
            val = None
            assert f"{val}" == "None"
            """);

    [Fact]
    public void Basic_EmptyFString()
        => Run("""
            assert f"" == ""
            """);

    [Fact]
    public void Basic_NoInterpolation()
        => Run("""
            assert f"just a string" == "just a string"
            """);

    // ── Literal braces ────────────────────────────────────────────────────────

    [Fact]
    public void Braces_EscapedLiteralBraces()
        => Run("""
            assert f"{{not interpolated}}" == "{not interpolated}"
            assert f"{{" == "{"
            assert f"}}" == "}"
            assert f"{{{42}}}" == "{42}"
            """);

    // ── Format specifications ─────────────────────────────────────────────────

    [Fact]
    public void FormatSpec_FloatPrecision()
        => Run("""
            assert f"{3.14159:.2f}" == "3.14"
            assert f"{3.14159:.4f}" == "3.1416"
            assert f"{0.1 + 0.2:.1f}" == "0.3"
            """);

    [Fact]
    public void FormatSpec_IntegerWidth()
        => Run("""
            assert f"{42:5d}" == "   42"
            assert f"{42:05d}" == "00042"
            assert f"{-42:06d}" == "-00042"
            """);

    [Fact]
    public void FormatSpec_Alignment()
        => Run("""
            assert f"{'left':<10}"  == "left      "
            assert f"{'right':>10}" == "      right"
            assert f"{'mid':^10}"   == "   mid    "
            assert f"{'x':*^5}"    == "**x**"
            """);

    [Fact]
    public void FormatSpec_IntegerBases()
        => Run("""
            assert f"{255:x}"   == "ff"
            assert f"{255:X}"   == "FF"
            assert f"{255:#x}"  == "0xff"
            assert f"{255:#b}"  == "0b11111111"
            assert f"{255:#o}"  == "0o377"
            assert f"{8:08b}"   == "00001000"
            """);

    [Fact]
    public void FormatSpec_ScientificNotation()
        => Run("""
            assert f"{12345.6789:.2e}" == "1.23e+04"
            assert f"{0.000123:.2e}"   == "1.23e-04"
            """);

    [Fact]
    public void FormatSpec_WidthFromVariable()
        => Run("""
            width = 10
            assert f"{'hi':{width}}" == "hi        "
            assert f"{'hi':>{width}}" == "        hi"
            """);

    [Fact]
    public void FormatSpec_PrecisionFromVariable()
        => Run("""
            prec = 3
            assert f"{3.14159:.{prec}f}" == "3.142"
            """);

    [Fact]
    public void FormatSpec_BothWidthAndPrecisionFromVariables()
        => Run("""
            width = 10
            prec = 2
            assert f"{3.14159:{width}.{prec}f}" == "      3.14"
            """);

    // ── Conversion flags ──────────────────────────────────────────────────────

    [Fact]
    public void Conversion_S_CallsStr()
        => Run("""
            class Foo:
                def __str__(self): return "str_rep"
                def __repr__(self): return "repr_rep"

            obj = Foo()
            assert f"{obj!s}" == "str_rep"
            """);

    [Fact]
    public void Conversion_R_CallsRepr()
        => Run("""
            class Foo:
                def __str__(self): return "str_rep"
                def __repr__(self): return "repr_rep"

            obj = Foo()
            assert f"{obj!r}" == "repr_rep"
            """);

    [Fact]
    public void Conversion_R_OnString_AddsQuotes()
        => Run("""
            s = "hello"
            assert f"{s!r}" == "'hello'"
            """);

    [Fact]
    public void Conversion_S_And_FormatSpec_Combined()
        => Run("""
            class Foo:
                def __str__(self): return "abc"
            obj = Foo()
            assert f"{obj!s:>6}" == "   abc"
            """);

    // ── Complex expressions inside f-strings ──────────────────────────────────

    [Fact]
    public void Complex_DictAccess()
        => Run("""
            d = {"key": "value", "n": 42}
            assert f"{d['key']}" == "value"
            assert f"{d['n'] * 2}" == "84"
            """);

    [Fact]
    public void Complex_ListIndex()
        => Run("""
            lst = [10, 20, 30]
            assert f"{lst[0]}" == "10"
            assert f"{lst[-1]}" == "30"
            """);

    [Fact]
    public void Complex_ConditionalExpression()
        => Run("""
            score = 85
            assert f"{'Pass' if score >= 60 else 'Fail'}" == "Pass"
            assert f"Grade: {'A' if score >= 90 else 'B' if score >= 80 else 'C'}" == "Grade: B"
            """);

    [Fact]
    public void Complex_ListComprehension()
        => Run("""
            nums = [1, 2, 3]
            assert f"{[x*2 for x in nums]}" == "[2, 4, 6]"
            """);

    [Fact]
    public void Complex_FunctionCall()
        => Run("""
            def greet(name): return f"Hi {name}"
            assert f"{greet('World')}" == "Hi World"
            """);

    [Fact]
    public void Complex_MethodChaining()
        => Run("""
            assert f"{'  hello  '.strip().upper()}" == "HELLO"
            """);

    [Fact]
    public void Complex_Lambda()
        => Run("""
            double = lambda x: x * 2
            assert f"{double(21)}" == "42"
            """);

    // ── Nested f-strings (PEP 701, Python 3.12+) ─────────────────────────────

    [Fact]
    public void Nested_TwoLevels()
        => Run("""
            inner = 42
            outer = f"outer {f'inner {inner}'} done"
            assert outer == "outer inner 42 done"
            """);

    [Fact]
    public void Nested_ThreeLevels()
        => Run("""
            a = 1
            b = 2
            deep = f"{f'{f'{a + b}'}'}"
            assert deep == "3"
            """);

    [Fact]
    public void Nested_WithFormatSpec()
        => Run("""
            val = 3.14159
            prec = 2
            # Inner f-string produces the format spec
            assert f"{val:{f'.{prec}f'}}" == "3.14"
            """);

    // ── Multi-line f-strings ──────────────────────────────────────────────────

    [Fact]
    public void Multiline_Concatenation()
        => Run("""
            first = "Alice"
            last = "Smith"
            full = (
                f"First: {first}, "
                f"Last: {last}"
            )
            assert full == "First: Alice, Last: Smith"
            """);

    // ── F-strings with special characters ────────────────────────────────────

    [Fact]
    public void Special_UnicodeInFString()
        => Run("""
            emoji = "🐍"
            assert f"Python {emoji}" == "Python 🐍"
            """);

    [Fact]
    public void Special_NewlineInExpression()
        => Run("""
            items = [1, 2, 3]
            assert f"count: {len(items)}" == "count: 3"
            """);

    [Fact]
    public void Special_BackslashNotAllowedInExpression_UseVariable()
        => Run("""
            # Backslash not allowed inside f-string expression — use variable instead
            newline = "\n"
            parts = ["a", "b", "c"]
            result = f"joined: {newline.join(parts)}"
            assert result == "joined: a\nb\nc"
            """);
}

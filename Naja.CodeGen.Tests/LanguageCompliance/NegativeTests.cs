using Xunit;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Pillar 1.2 — Negative Tests: What Must FAIL.
///
/// Every test here asserts that the Naja compiler REJECTS a syntactically or
/// semantically invalid .naja file with a meaningful error message.
///
/// Three things are verified per test:
///   1. The compiler throws (does not silently succeed or crash with a CLR internal error)
///   2. The exception is a CodeGenException (not NullReferenceException etc.)
///   3. The error message contains the expected diagnostic fragment
///
/// Per the testing strategy: "The compiler must NEVER emit a .NET stack trace
/// for a Python syntax error."
///
/// Test data: testdata/languagecompliance/should_fail/
/// </summary>
public sealed class NegativeTests
{
    private static readonly NajaEngine Engine = new();

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Assert that compiling <paramref name="fileName"/> throws a
    /// <see cref="CodeGenException"/> whose message contains
    /// <paramref name="expectedFragment"/>.
    /// </summary>
    private static void MustReject(string fileName, string? expectedFragment = null)
    {
        var path = Path.Combine("testdata", "languagecompliance", "should_fail", fileName);

        var ex = Assert.Throws<CodeGenException>(() => Engine.Eval(path));

        if (expectedFragment is not null)
        {
            Assert.True(
                ex.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase),
                $"Expected error to contain '{expectedFragment}' but got:\n{ex.Message}");
        }
    }

    /// <summary>
    /// Assert that an inline source snippet is rejected.
    /// </summary>
    private static void MustRejectSource(string source, string? expectedFragment = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_negative_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"neg_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());

        var ex = Assert.Throws<CodeGenException>(() => Engine.Eval(path));

        if (expectedFragment is not null)
        {
            Assert.True(
                ex.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase),
                $"Expected error fragment '{expectedFragment}' not found in:\n{ex.Message}");
        }
    }

    // ── Syntax Errors (file-based) ────────────────────────────────────────────

    [Fact]
    public void Reject_MismatchedBrackets()
        => MustReject("mismatched_brackets.naja", "syntax");

    [Fact]
    public void Reject_ReturnOutsideFunction()
        => MustReject("return_outside_func.naja", "return");

    [Fact]
    public void Reject_BreakOutsideLoop()
        => MustReject("break_outside_loop.naja", "break");

    [Fact]
    public void Reject_DuplicateKeywordArgument()
        => MustReject("duplicate_kwarg.naja", "keyword");

    [Fact]
    public void Reject_InvalidParamOrder_KwargsBeforePositional()
        => MustReject("invalid_param_order.naja", "syntax");

    [Fact]
    public void Reject_NonlocalAndGlobalConflict()
        => MustReject("nonlocal_global_conflict.naja");

    [Fact]
    public void Reject_StarOutsideAssignment()
        => MustReject("star_outside_assign.naja", "starred");

    // ── Semantic / Scope Errors (inline) ─────────────────────────────────────

    [Fact]
    public void Reject_ContinueOutsideLoop()
        => MustRejectSource("continue", "continue");

    [Fact]
    public void Reject_YieldOutsideFunction()
        => MustRejectSource("yield 1", "yield");

    [Fact]
    public void Reject_DeleteUndefinedName()
        => MustRejectSource("""
            del nonexistent_variable_xyz
            """);

    [Fact]
    public void Reject_DuplicateParameterName()
        => MustRejectSource("""
            def f(x, x): pass
            """, "duplicate");

    // ── Error Quality Assertions ──────────────────────────────────────────────
    // The strategy requires errors to include file + line + suggestion.
    // These tests verify the error message is human-friendly.

    [Fact]
    public void ErrorMessage_ContainsLineNumber_ForSyntaxError()
    {
        var path = Path.Combine("testdata", "languagecompliance", "should_fail", "return_outside_func.naja");
        var ex = Assert.Throws<CodeGenException>(() => Engine.Eval(path));

        // CodeGenException format is "[L{line}:C{col}] message"
        // A line number MUST be present for syntax errors (not just "parse failed")
        Assert.True(
            ex.Line > 0 || ex.Message.Contains("[L"),
            $"Expected line info in error message. Got: {ex.Message}");
    }

    [Fact]
    public void ErrorMessage_NeverContains_CLRInternalTypes()
    {
        // The error surface must be Pythonic — no CLR namespace leakage
        var path = Path.Combine("testdata", "languagecompliance", "should_fail", "mismatched_brackets.naja");
        var ex = Assert.Throws<CodeGenException>(() => Engine.Eval(path));

        // These strings should NOT appear in a Python syntax error message
        Assert.DoesNotContain("System.Reflection", ex.Message);
        Assert.DoesNotContain("NullReferenceException", ex.Message);
        Assert.DoesNotContain("StackOverflowException", ex.Message);
    }

    [Fact]
    public void Reject_NotCodeGenException_NeverSilentlySucceeds()
    {
        // Verify that a clearly invalid file does NOT compile and run.
        // The point: we must throw, not silently emit broken IL.
        var path = Path.Combine("testdata", "languagecompliance", "should_fail", "break_outside_loop.naja");
        bool threw = false;
        try
        {
            Engine.Eval(path);
        }
        catch (CodeGenException)
        {
            threw = true;
        }
        catch (Exception ex)
        {
            // Any exception is acceptable — but CodeGenException is strongly preferred.
            // Log what we got to help improve the compiler error surface.
            threw = true;
            Assert.True(false,
                $"Expected CodeGenException but got {ex.GetType().Name}: {ex.Message}");
        }
        Assert.True(threw, "Invalid source was silently accepted — must be rejected");
    }
}

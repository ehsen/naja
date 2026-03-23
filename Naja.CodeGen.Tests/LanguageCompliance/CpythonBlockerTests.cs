using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// CPython Blocker Tests — Features Blocked by Missing Compiler Support.
///
/// These tests are INTENTIONALLY SKIPPED (not xfail) to avoid false pass reporting.
/// Each represents a known blocker that prevents a test file from passing.
///
/// NOT COUNTED TOWARD COMPLIANCE METRICS.
/// 
/// When a blocker is fixed, move the test to the appropriate Phase class.
/// </summary>
[Collection("SerialConsole")]
public sealed class CpythonBlockerTests
{
    private readonly ITestOutputHelper _out;
    private static readonly NajaEngine Engine = new();

    private static readonly string? CpythonRoot =
        Environment.GetEnvironmentVariable("CPYTHON_REPO")
        ?? TryDefault("C:/dev/cpython")
        ?? TryDefault("~/dev/cpython");

    private static string? TryDefault(string path)
    {
        var expanded = path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        return Directory.Exists(expanded) ? expanded : null;
    }

    public CpythonBlockerTests(ITestOutputHelper output) => _out = output;

    private void RunCpythonTest(string testFile, string reason)
    {
        if (CpythonRoot is null)
        {
            _out.WriteLine("SKIPPED: set CPYTHON_REPO env var to your CPython clone path.");
            return;
        }

        var testPath = Path.Combine(CpythonRoot, "Lib", "test", testFile);
        if (!File.Exists(testPath))
        {
            _out.WriteLine($"SKIPPED: {reason} — file not found: {testFile}");
            return;
        }

        _out.WriteLine($"SKIPPED: {reason}");
        _out.WriteLine($"File: {testPath}");
        _out.WriteLine("This test is blocked by a known limitation and intentionally skipped.");
        _out.WriteLine("It will be promoted to Phase 1/2 when the blocker is fixed.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // BLOCKERS — INTENTIONALLY SKIPPED (NOT XFAIL)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact(Skip = "BLOCKER: Nested classes not implemented. EmitClassDef is no-op for nested classes.")]
    public void Blocked_AugAssign_NestedClasses()
        => RunCpythonTest("test_augassign.py", "Nested classes (class definition inside functions)");

    [Fact(Skip = "BLOCKER: Parser doesn't support implicit string concatenation (f'text' 'more').")]
    public void Blocked_KeywordOnlyArg_ImplicitStringConcat()
        => RunCpythonTest("test_keywordonlyarg.py", "Implicit string concatenation across lines");

    [Fact(Skip = "BLOCKER: BigInteger support needed for 0xffffffffffffffff and similar literals.")]
    public void Blocked_IntLiteral_BigInteger()
        => RunCpythonTest("test_int_literal.py", "Integer overflow (BigInteger needed)");

    [Fact(Skip = "BLOCKER: exec() full evaluation not yet implemented for syntax validation.")]
    public void Blocked_NamedExpressions_ExecEval()
        => RunCpythonTest("test_named_expressions.py", "Full exec() evaluation for walrus operator tests");

    [Fact(Skip = "BLOCKER: eval() string expression evaluation not yet implemented.")]
    public void Blocked_Unary_EvalStringExpressions()
        => RunCpythonTest("test_unary.py", "eval() for boundary check expressions");

    [Fact(Skip = "BLOCKER: Very deeply nested expressions may hit stack depth or IL limits.")]
    public void Blocked_LongExp_StackDepth()
        => RunCpythonTest("test_longexp.py", "Stack depth / IL size limits on deeply nested expressions");

    [Fact(Skip = "BLOCKER: Complex decorator attribute chains not yet fully supported.")]
    public void Blocked_Decorators_ComplexChains()
        => RunCpythonTest("test_decorators.py", "Complex stacked decorator attribute resolution");

    [Fact(Skip = "BLOCKER: Metaclass support not yet implemented.")]
    public void Blocked_TypeChecks_Metaclass()
        => RunCpythonTest("test_typechecks.py", "Metaclass support for isinstance/issubclass");

    [Fact(Skip = "BLOCKER: getattr() / dir() reflection not yet wired up.")]
    public void Blocked_UnicodeIdentifiers_Reflection()
        => RunCpythonTest("test_unicode_identifiers.py", "getattr/dir reflection for non-ASCII identifiers");

    [Fact(Skip = "BLOCKER: Generator support (yield expression tracking) incomplete.")]
    public void Blocked_Generators_YieldTracking()
        => RunCpythonTest("test_generators.py", "Generator yield expression tracking");

    [Fact(Skip = "BLOCKER: Coroutine async/await not yet implemented.")]
    public void Blocked_Coroutines_AsyncAwait()
        => RunCpythonTest("test_coroutines.py", "Async/await coroutine support");

    [Fact(Skip = "BLOCKER: F-string expressions not yet fully supported.")]
    public void Blocked_FString_Expressions()
        => RunCpythonTest("test_fstring.py", "F-string complex expression evaluation");

    [Fact(Skip = "BLOCKER: Pattern matching (match/case) not yet implemented.")]
    public void Blocked_PatternMatching_MatchCase()
        => RunCpythonTest("test_patternmatching.py", "Pattern matching (PEP 634)");

    [Fact(Skip = "BLOCKER: Exception groups (except*) not yet implemented.")]
    public void Blocked_ExceptionGroup_ExceptStar()
        => RunCpythonTest("test_exceptiongroup.py", "Exception groups (PEP 654) - except*");

    [Fact(Skip = "BLOCKER: Type annotations not yet processed.")]
    public void Blocked_TypeAnnotations_Processing()
        => RunCpythonTest("test_typeannotations.py", "Type annotation processing");

    [Fact(Skip = "BLOCKER: Grammar (eval mode) not yet fully supported.")]
    public void Blocked_Grammar_EvalMode()
        => RunCpythonTest("test_grammar.py", "Grammar in eval/single modes");

    [Fact(Skip = "BLOCKER: Context manager (with statement) edge cases incomplete.")]
    public void Blocked_Contextlib_WithEdgeCases()
        => RunCpythonTest("test_contextlib.py", "Context manager edge cases");

    [Fact(Skip = "BLOCKER: Iterator protocol incomplete.")]
    public void Blocked_Itertools_Protocol()
        => RunCpythonTest("test_itertools.py", "Iterator protocol completeness");
}

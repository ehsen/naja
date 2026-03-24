using Xunit.Abstractions;
using Naja.CPythonTests.Infrastructure;

namespace Naja.CPythonTests.CompilerCorrectness;

/// <summary>
/// CPython files that use only the unittest module but have a known blocker
/// in Naja today (missing eval/exec, metaclass emit, compile(), etc.).
///
/// Each test is <c>[Fact(Skip="...")]</c> — it shows as Skipped in Test Explorer.
/// When a blocker is fixed, remove the Skip and verify it passes, then
/// move it to <see cref="ConfirmedPassingTests"/>.
/// </summary>
[Collection("SerialConsole")]
[Trait("Category", "CompilerCorrectness")]
[Trait("XFail", "true")]
[Trait("StdlibOnly", "true")]
public sealed class PureUnittestXFailTests : CPythonTestFixture
{
    public PureUnittestXFailTests(ITestOutputHelper output) : base(output) { }

    [Fact(Skip = "nested class inside function; compile() not implemented")]
    public void CPython_AugAssign() { }

    [Fact(Skip = "eval() used for BigInteger boundary and bad-type checks")]
    public void CPython_Unary() { }

    [Fact(Skip = "metaclass= keyword not yet emitted")]
    public void CPython_TypeChecks() { }

    [Fact(Skip = "compile() / __code__.co_kwonlyargcount / __kwdefaults__ not implemented")]
    public void CPython_KeywordOnlyArg() { }

    [Fact(Skip = "getattr() / dir() reflection not wired")]
    public void CPython_UnicodeIdentifiers() { }

    [Fact(Skip = "compile()/exec() and str.encode() not implemented")]
    public void CPython_Utf8Source() { }

    [Fact(Skip = "compile() with __future__ flags not implemented")]
    public void CPython_Flufl() { }

    [Fact(Skip = "exec() of dynamic source strings not implemented")]
    public void CPython_NamedExpressions() { }

    [Fact(Skip = "eval() of dynamically-built source strings not implemented")]
    public void CPython_LongExp() { }

    [Fact(Skip = "compile()/eval()/exec() and getattr() reflection not implemented")]
    public void CPython_Decorators() { }
}


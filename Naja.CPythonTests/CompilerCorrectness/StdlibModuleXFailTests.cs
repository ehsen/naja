using Xunit.Abstractions;
using Naja.CPythonTests.Infrastructure;

namespace Naja.CPythonTests.CompilerCorrectness;

/// <summary>
/// CPython files that require stdlib modules beyond bare unittest
/// (math, types, abc, operator, itertools, collections.abc) and have a known
/// blocker in Naja today.
///
/// Each test is <c>[Fact(Skip="...")]</c> — shown as Skipped in Test Explorer.
/// When the required module is wired up, remove the Skip and verify it passes,
/// then move it to <see cref="ConfirmedPassingTests"/>.
/// </summary>
[Collection("SerialConsole")]
[Trait("Category", "CompilerCorrectness")]
[Trait("XFail", "true")]
public sealed class StdlibModuleXFailTests : CPythonTestFixture
{
    public StdlibModuleXFailTests(ITestOutputHelper output) : base(output) { }

    [Fact(Skip = "import math module wiring incomplete; range() with float step")]
    [Trait("StdlibModule", "math")]
    public void CPython_Pow() { }

    [Fact(Skip = "__init_subclass__ emit and types module missing")]
    [Trait("StdlibModule", "types")]
    public void CPython_SubclassInit() { }

    [Fact(Skip = "abc.ABC, @abstractmethod, operator module missing")]
    [Trait("StdlibModule", "abc,operator")]
    public void CPython_BinOp() { }

    [Fact(Skip = "operator, itertools, collections.abc modules missing")]
    [Trait("StdlibModule", "operator,itertools,collections.abc")]
    public void CPython_IterLen() { }

    [Fact(Skip = "types.DynamicClassAttribute, abc metaclass, sys.exc_info() missing")]
    [Trait("StdlibModule", "types,abc,sys")]
    public void CPython_DynamicClassAttribute() { }
}


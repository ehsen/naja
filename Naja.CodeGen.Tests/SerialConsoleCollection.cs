using Xunit;

namespace Naja.CodeGen.Tests;

/// <summary>
/// xUnit collection definition that forces NajaEngineTests and CodeGenTests to
/// run sequentially. Both classes redirect Console.Out during test execution and
/// would race with each other under default parallel test execution.
/// </summary>
[CollectionDefinition("SerialConsole", DisableParallelization = true)]
public class SerialConsoleCollection { }

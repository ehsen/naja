using Xunit;

namespace Naja.CPythonTests;

/// <summary>
/// xUnit collection definition that forces all CPython test classes to run
/// sequentially. Every class in this project redirects Console.Out and
/// Console.Error during test execution via NajaTestExecutor, which would race
/// under default parallel execution.
/// </summary>
[CollectionDefinition("SerialConsole", DisableParallelization = true)]
public sealed class SerialTestCollection { }

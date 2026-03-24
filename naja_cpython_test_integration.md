# Naja Compiler — CPython Test Integration with xUnit
> Strategy for running CPython's test suite as first-class xUnit tests

---

## Table of Contents

1. [The Problem with Console Running](#1-the-problem-with-console-running)
2. [Architecture Overview](#2-architecture-overview)
3. [Test Categories](#3-test-categories)
4. [Project Structure](#4-project-structure)
5. [The Base Fixture](#5-the-base-fixture)
6. [Category 1 — Compiler Correctness Tests](#6-category-1--compiler-correctness-tests)
7. [Category 2 — CPython Full Suite Tests](#7-category-2--cpython-full-suite-tests)
8. [Category 3 — Granular Per-Method Tests](#8-category-3--granular-per-method-tests)
9. [XFAIL and Trait Tracking](#9-xfail-and-trait-tracking)
10. [Output Capture Strategy](#10-output-capture-strategy)
11. [CPython Test Discovery](#11-cpython-test-discovery)
12. [CI Pipeline Integration](#12-ci-pipeline-integration)
13. [Test Naming Conventions](#13-test-naming-conventions)
14. [Migration Path from Current Tests](#14-migration-path-from-current-tests)

---

## 1. The Problem with Console Running

Running CPython tests directly against the compiler outside xUnit creates several problems that compound over time:

**Output swallowing.** Console output from hundreds of test methods merges into an unreadable stream. A failure in `test_math` on line 847 scrolls off screen before you can read it.

**No history.** You cannot diff this run against the last run. You cannot tell whether you regressed something that was previously passing.

**No CI integration.** A CI pipeline cannot parse console output to determine pass/fail at the individual test level. It can only say the whole run passed or failed.

**No categorization.** You cannot filter to "show me only stdlib failures" or "show me only XFAIL tests that started passing." That information is not tracked anywhere.

**Mixed concerns.** Your existing .NET-side xUnit tests and your CPython tests are in different runners, producing different outputs, with no unified view.

The fix is to make CPython tests first-class xUnit citizens. The CPython test suite becomes input data. The xUnit framework becomes the runner. Every CPython test method gets a green or red dot in the test explorer just like your .NET tests.

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        xUnit Test Runner                        │
│                   (dotnet test / Rider / VS)                    │
└────────────────────────────┬────────────────────────────────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
     ┌────────▼──────┐ ┌─────▼──────┐ ┌────▼────────────┐
     │  .NET Side    │ │  Compiler  │ │  CPython Suite  │
     │  Unit Tests   │ │  Tests     │ │  Tests          │
     │  (existing)   │ │  (Phase 1) │ │  (new)          │
     └───────────────┘ └────────────┘ └────────┬────────┘
                                               │
                              ┌────────────────┼─────────────────┐
                              │                │                 │
                     ┌────────▼──────┐ ┌───────▼──────┐ ┌───────▼──────┐
                     │  Full Suite   │ │  Per-Method  │ │  Stdlib      │
                     │  (one Fact    │ │  (one Theory │ │  Compile     │
                     │  per module)  │ │  per method) │ │  Tests       │
                     └───────────────┘ └──────────────┘ └──────────────┘
```

The compiler is invoked as a library from within xUnit tests. Output from the compiled Python process is fully captured and attached to the xUnit test result. Every failure includes the full Python traceback, visible in the test explorer.

---

## 3. Test Categories

There are four distinct test categories. Each serves a different purpose and runs at a different granularity.

### Category 1 — Compiler Correctness Tests
**What:** Your existing Phase 1/2/3 tests. Small, self-contained Python snippets that test a specific compiler feature.
**Granularity:** One xUnit `[Fact]` per Python feature being tested.
**When they run:** Every commit. Fast — each test is a tiny snippet.
**Current state:** This is what you already have. The goal is to ensure these follow the same fixture pattern as everything else.

### Category 2 — CPython Full Suite Tests
**What:** One xUnit test per CPython test file (e.g. `test_math.py` → one `[Fact]`).
**Granularity:** Pass/fail at the module level. Failure message shows which Python test methods failed.
**When they run:** Every commit. Moderate speed.
**Purpose:** Quick signal — is this whole module green or not.

### Category 3 — Granular Per-Method Tests
**What:** One xUnit `[Theory]` data point per CPython test method (e.g. `MathTests.test_sqrt` → one row).
**Granularity:** Individual Python test methods visible as individual xUnit results.
**When they run:** On demand, nightly, or per-module when actively working on that module.
**Purpose:** Surgical — see exactly which test methods are failing inside a module.

### Category 4 — Stdlib Compilation Tests
**What:** Tests that verify a stdlib module compiles without errors, before even running tests.
**Granularity:** One `[Fact]` per stdlib module.
**When they run:** Every commit.
**Purpose:** Catch compilation regressions early, separate from behavioral failures.

---

## 4. Project Structure

```
Naja.sln
├── Naja.Compiler/                  ← compiler source
├── Naja.Runtime/                   ← runtime source
├── Naja.Tests/                     ← .NET unit tests (existing)
│   ├── CompilerTests/
│   └── RuntimeTests/
└── Naja.CPythonTests/              ← NEW — CPython integration tests
    ├── Naja.CPythonTests.csproj
    ├── Infrastructure/
    │   ├── CPythonTestFixture.cs       ← base fixture all tests inherit
    │   ├── CPythonTestResult.cs        ← result model
    │   ├── CPythonTestDiscovery.cs     ← discovers test methods from .py files
    │   ├── UnittestOutputParser.cs     ← parses Python unittest output
    │   └── NajaTestExecutor.cs         ← compiles + runs Python through Naja
    ├── CompilerCorrectness/         ← Category 1
    │   ├── Phase1_Exceptions.cs
    │   ├── Phase2_AugAssign.cs
    │   ├── Phase2_Generators.cs
    │   └── ...
    ├── StdlibCompilation/           ← Category 4
    │   ├── Tier1CompilationTests.cs
    │   └── Tier2CompilationTests.cs
    ├── FullSuite/                   ← Category 2
    │   ├── CPythonMathTests.cs
    │   ├── CPythonOperatorTests.cs
    │   ├── CPythonCollectionsTests.cs
    │   └── ...
    ├── Granular/                    ← Category 3
    │   ├── CPythonMathGranular.cs
    │   └── ...
    ├── cpython/                     ← git submodule or symlink
    │   └── Lib/test/               ← CPython test files live here
    └── fixtures/                   ← small Python snippets for Category 1
        ├── exceptions/
        ├── generators/
        └── ...
```

Keep `Naja.CPythonTests` as a separate project from `Naja.Tests`. The CPython tests have different dependencies, different run times, and different trait filters. Mixing them pollutes the fast unit test suite with slow integration tests.

---

## 5. The Base Fixture

Everything inherits from this. It handles compilation, execution, output capture, and result parsing in one place.

```csharp
// Infrastructure/CPythonTestFixture.cs

namespace Naja.CPythonTests.Infrastructure;

public abstract class CPythonTestFixture
{
    protected readonly ITestOutputHelper Output;
    
    // Path to CPython test directory — configure via environment or appsettings
    protected static readonly string CPythonTestRoot = 
        Environment.GetEnvironmentVariable("CPYTHON_TEST_ROOT") 
        ?? Path.Combine(AppContext.BaseDirectory, "cpython", "Lib", "test");

    // Path to Naja stdlib directory
    protected static readonly string NajaStdlibRoot =
        Environment.GetEnvironmentVariable("NAJA_STDLIB_ROOT")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Naja.Stdlib");

    protected CPythonTestFixture(ITestOutputHelper output)
    {
        Output = output;
    }

    // ── Core execution methods ────────────────────────────────────────────────

    /// <summary>
    /// Compile and run an entire CPython test file.
    /// Used by Category 2 (full suite) tests.
    /// </summary>
    protected CPythonTestResult RunTestFile(string testFileName, int timeoutMs = 60_000)
    {
        var testFilePath = Path.Combine(CPythonTestRoot, testFileName);
        return RunTestFileAtPath(testFilePath, null, timeoutMs);
    }

    /// <summary>
    /// Compile and run a specific test class and method from a CPython test file.
    /// Used by Category 3 (granular) tests.
    /// </summary>
    protected CPythonTestResult RunTestMethod(
        string testFileName, 
        string className, 
        string methodName,
        int timeoutMs = 30_000)
    {
        var testFilePath = Path.Combine(CPythonTestRoot, testFileName);
        return RunTestFileAtPath(testFilePath, $"{className}.{methodName}", timeoutMs);
    }

    /// <summary>
    /// Compile a Python source file and assert it compiles without errors.
    /// Used by Category 4 (compilation) tests.
    /// </summary>
    protected CompilationResult AssertCompiles(string pythonFilePath)
    {
        var executor = new NajaTestExecutor();
        var result = executor.Compile(pythonFilePath);

        Output.WriteLine($"Compiling: {pythonFilePath}");
        if (!result.Success)
        {
            foreach (var error in result.Errors)
                Output.WriteLine($"  ERROR: {error}");
        }

        return result;
    }

    /// <summary>
    /// Run a small inline Python snippet.
    /// Used by Category 1 (compiler correctness) tests.
    /// </summary>
    protected CPythonTestResult RunSnippet(string pythonSource, int timeoutMs = 10_000)
    {
        var executor = new NajaTestExecutor();
        return executor.RunInline(pythonSource, timeoutMs);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private CPythonTestResult RunTestFileAtPath(
        string fullPath, 
        string specificMethod, 
        int timeoutMs)
    {
        Output.WriteLine($"Test file : {fullPath}");
        Output.WriteLine($"Method    : {specificMethod ?? "(all)"}");
        Output.WriteLine(new string('─', 60));

        var executor = new NajaTestExecutor();

        // Step 1 — compile
        var compilation = executor.Compile(fullPath);
        if (!compilation.Success)
        {
            var result = CPythonTestResult.CompilationFailure(compilation.Errors);
            WriteResult(result);
            return result;
        }

        // Step 2 — execute
        var execution = executor.Run(compilation.Assembly, specificMethod, timeoutMs);

        // Step 3 — parse unittest output
        var parsed = UnittestOutputParser.Parse(execution);
        WriteResult(parsed);
        return parsed;
    }

    private void WriteResult(CPythonTestResult result)
    {
        Output.WriteLine($"Exit code : {result.ExitCode}");
        Output.WriteLine($"Passed    : {result.PassCount}");
        Output.WriteLine($"Failed    : {result.FailCount}");
        Output.WriteLine($"Errors    : {result.ErrorCount}");
        Output.WriteLine(new string('─', 60));

        if (!string.IsNullOrWhiteSpace(result.Stdout))
        {
            Output.WriteLine("STDOUT:");
            Output.WriteLine(result.Stdout);
        }

        if (!string.IsNullOrWhiteSpace(result.Stderr))
        {
            Output.WriteLine("STDERR:");
            Output.WriteLine(result.Stderr);
        }

        if (result.FailureSummary != null)
        {
            Output.WriteLine("FAILURES:");
            Output.WriteLine(result.FailureSummary);
        }
    }
}
```

---

## 6. Category 1 — Compiler Correctness Tests

These are your existing Phase 1/2/3 tests, migrated into the fixture pattern. Each tests one specific compiler feature with a small Python snippet or fixture file. They do not depend on stdlib being present.

```csharp
// CompilerCorrectness/Phase1_Exceptions.cs

namespace Naja.CPythonTests.CompilerCorrectness;

[Trait("Category", "CompilerCorrectness")]
[Trait("Phase", "1")]
public class Phase1_ExceptionTests : CPythonTestFixture
{
    public Phase1_ExceptionTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void CPython_ExceptionVariations_AllPass()
    {
        var result = RunTestFile("test_exception_variations.py");
        Assert.True(result.Passed, result.FailureSummary);
    }
}
```

```csharp
// CompilerCorrectness/Phase2_Generators.cs

[Trait("Category", "CompilerCorrectness")]
[Trait("Phase", "2")]
public class Phase2_GeneratorTests : CPythonTestFixture
{
    public Phase2_GeneratorTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    [Trait("XFail", "generator_send_throw")]
    public void CPython_GeneratorStop_SendAndThrow()
    {
        var result = RunTestFile("test_generatorstop.py");

        // XFAIL — known missing: generator send() and throw()
        // When this starts passing, the trait above becomes evidence
        // the feature is implemented. Change to Assert.True at that point.
        if (result.Passed)
            Output.WriteLine("XFAIL NOW PASSING — remove XFail trait and make this Assert.True");

        Assert.False(result.Passed,
            "This test is expected to fail until generator send/throw is implemented. " +
            "If it is now passing, promote it to a regular passing test.");
    }

    [Fact]
    public void CPython_GeneratorBasics_YieldAndIteration()
    {
        var result = RunSnippet(@"
def counter(n):
    for i in range(n):
        yield i

result = list(counter(5))
assert result == [0, 1, 2, 3, 4], f'Expected [0..4] got {result}'

# Generator expression
squares = list(x*x for x in range(4))
assert squares == [0, 1, 4, 9]
");
        Assert.True(result.Passed, result.FailureSummary);
    }
}
```

### Snippet vs File Decision

Use **inline snippets** (`RunSnippet`) for:
- Testing a single language construct in isolation
- Cases where you write the test yourself to probe a specific bug
- Quick validation that a compiler fix worked

Use **fixture files** (`RunTestFile` against `fixtures/`) for:
- Anything more than ~20 lines
- Tests you want to version control and diff
- Anything that will be expanded over time

Use **CPython test files** (`RunTestFile` against `cpython/`) for:
- Stdlib module validation
- Language feature validation against CPython's own suite

---

## 7. Category 2 — CPython Full Suite Tests

One xUnit `[Fact]` per CPython test file. This is the fast signal — is this entire module passing or not.

```csharp
// FullSuite/Math_FullSuite.cs

namespace Naja.CPythonTests.FullSuite;

[Trait("Category", "CPythonFullSuite")]
[Trait("StdlibModule", "math")]
public class MathFullSuiteTests : CPythonTestFixture
{
    public MathFullSuiteTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void test_math_full_suite()
    {
        var result = RunTestFile("test_math.py");
        Assert.True(result.Passed, result.FailureSummary);
    }
}
```

```csharp
// FullSuite/Collections_FullSuite.cs

[Trait("Category", "CPythonFullSuite")]
[Trait("StdlibModule", "collections")]
public class CollectionsFullSuiteTests : CPythonTestFixture
{
    public CollectionsFullSuiteTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void test_collections_full_suite()
    {
        var result = RunTestFile("test_collections.py", timeoutMs: 120_000);
        Assert.True(result.Passed, result.FailureSummary);
    }

    [Fact]
    public void test_deque_full_suite()
    {
        var result = RunTestFile("test_deque.py");
        Assert.True(result.Passed, result.FailureSummary);
    }

    [Fact]
    public void test_ordered_dict_full_suite()
    {
        var result = RunTestFile("test_ordered_dict.py");
        Assert.True(result.Passed, result.FailureSummary);
    }
}
```

### One File Per Module

Keep one `.cs` file per CPython stdlib module. Do not put multiple unrelated modules in the same class. The file naming convention is `<ModuleName>_FullSuite.cs`.

---

## 8. Category 3 — Granular Per-Method Tests

This is the most powerful mode. Each CPython test method becomes an individual xUnit Theory data point. You see exactly `MathTests.test_sqrt ✅` and `MathTests.test_log_domain_error ❌` as separate rows.

```csharp
// Granular/Math_Granular.cs

namespace Naja.CPythonTests.Granular;

[Trait("Category", "CPythonGranular")]
[Trait("StdlibModule", "math")]
public class MathGranularTests : CPythonTestFixture
{
    public MathGranularTests(ITestOutputHelper output) : base(output) { }

    public static IEnumerable<object[]> MathTestMethods =>
        CPythonTestDiscovery.GetTestMethods("test_math.py");

    [Theory]
    [MemberData(nameof(MathTestMethods))]
    public void math_test(string className, string methodName)
    {
        var result = RunTestMethod("test_math.py", className, methodName);

        Output.WriteLine($"[{className}.{methodName}]");

        Assert.True(result.Passed,
            $"{className}.{methodName} failed:\n{result.FailureSummary}");
    }
}
```

The test explorer shows:

```
Naja.CPythonTests
  Granular
    MathGranularTests
      math_test(MathTests, test_sqrt)                    ✅
      math_test(MathTests, test_floor)                   ✅
      math_test(MathTests, test_ceil)                    ✅
      math_test(MathTests, test_log)                     ❌  ValueError not raised
      math_test(MathTests, test_log_domain_error)        ❌  wrong exception message
      math_test(MathTests, test_factorial)               ✅
      math_test(MathTests, test_factorial_bool)          ❌  TypeError expected
      math_test(MathTests, test_gcd)                     ✅
```

### When to Use Granular vs Full Suite

Granular tests are slower because each method is a separate compile+run cycle. The right usage pattern:

- **Full suite tests** run on every commit. They tell you if a module is green or red.
- **Granular tests** run when you are actively working on a module and need to see exactly which methods are failing. Run them on demand or nightly.

You can control this with traits and dotnet test filters:

```bash
# Run only full suite tests on every commit
dotnet test --filter "Category=CPythonFullSuite"

# Run granular for math when working on math module
dotnet test --filter "Category=CPythonGranular&StdlibModule=math"

# Run everything nightly
dotnet test
```

---

## 9. XFAIL and Trait Tracking

Your current Phase 2/3 XFAIL tracking maps directly to xUnit traits. This preserves the information you already have while making it queryable.

### The XFAIL Pattern

```csharp
[Fact]
[Trait("Category", "CompilerCorrectness")]
[Trait("Phase", "2")]
[Trait("XFail", "true")]
[Trait("XFailReason", "eval_exec_not_implemented")]
[Trait("XFailTracking", "https://github.com/your-repo/issues/47")]
public void CPython_Decorators_CompileAndExec()
{
    var result = RunTestFile("test_decorators.py");

    if (result.Passed)
    {
        // This is an XPASS — the feature now works
        // This line will fail the test intentionally, forcing you to
        // promote it to a proper passing test and remove the XFail traits
        Assert.Fail(
            "XPASS: CPython_Decorators now passes. " +
            "Remove XFail traits and change Assert.False to Assert.True."
        );
    }

    // Document exactly what is failing and why
    Assert.Contains("eval", result.FailureSummary ?? result.Stderr,
        StringComparison.OrdinalIgnoreCase);
}
```

### The XPASS Detection — Why It Matters

The XPASS check (`if (result.Passed) Assert.Fail(...)`) forces you to actively promote tests when the compiler improves. Without it, an XFAIL test that starts passing silently stays in XFAIL state, and you lose visibility into compiler progress. The test runner surfaces it as a failure with the message "remove XFail traits" — you cannot miss it.

### Trait Taxonomy

Use these traits consistently across all test classes:

| Trait Key | Values | Purpose |
|-----------|--------|---------|
| `Category` | `CompilerCorrectness`, `CPythonFullSuite`, `CPythonGranular`, `StdlibCompilation` | Primary filter for CI |
| `Phase` | `1`, `2`, `3`, `4` | Compiler development phase |
| `StdlibModule` | `math`, `collections`, `os`, etc. | Filter by module |
| `XFail` | `true` | Mark known failures |
| `XFailReason` | short identifier | Queryable reason |
| `Tier` | `1`, `2`, `3` | Stdlib tier from strategy doc |
| `Priority` | `high`, `medium`, `low` | Implementation priority |

### Querying Traits

```bash
# How many tests are currently XFAIL?
dotnet test --filter "XFail=true" --list-tests | wc -l

# Run only high priority failing tests
dotnet test --filter "XFail=true&Priority=high"

# Run all math-related tests
dotnet test --filter "StdlibModule=math"

# Run everything except known failures for clean CI output
dotnet test --filter "XFail!=true"
```

---

## 10. Output Capture Strategy

Output from Python test runs must be fully captured and attached to the xUnit result. Nothing goes to console.

```csharp
// Infrastructure/NajaTestExecutor.cs

public class NajaTestExecutor
{
    public ExecutionResult Run(
        CompiledAssembly assembly,
        string specificMethod,
        int timeoutMs)
    {
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        // Redirect all output — nothing escapes to console
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        try
        {
            Console.SetOut(new StringWriter(stdoutBuilder));
            Console.SetError(new StringWriter(stderrBuilder));

            var exitCode = assembly.Execute(specificMethod, timeoutMs);

            return new ExecutionResult
            {
                ExitCode = exitCode,
                Stdout = stdoutBuilder.ToString(),
                Stderr = stderrBuilder.ToString(),
            };
        }
        catch (TimeoutException)
        {
            return new ExecutionResult
            {
                ExitCode = -1,
                Stdout = stdoutBuilder.ToString(),
                Stderr = stderrBuilder.ToString() + "\n[TIMEOUT]",
                TimedOut = true,
            };
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }
}
```

### What Gets Attached to Each xUnit Test Result

The fixture's `WriteResult` method (shown in Section 5) uses `ITestOutputHelper` to attach all of this to the individual test:

- The Python test file path and method name being run
- Compilation errors if compilation failed
- Full stdout from the Python test run
- Full stderr (where Python unittest writes its output)
- Parsed failure summary — which Python test methods failed and why
- Exit code

This means when you click a failed test in Rider or VS, you see the complete Python traceback inline. Nothing to hunt for in console logs.

---

## 11. CPython Test Discovery

The discovery class parses Python test files to extract class and method names. This drives the granular Theory tests.

```csharp
// Infrastructure/CPythonTestDiscovery.cs

public static class CPythonTestDiscovery
{
    private static readonly string TestRoot =
        Environment.GetEnvironmentVariable("CPYTHON_TEST_ROOT")
        ?? Path.Combine(AppContext.BaseDirectory, "cpython", "Lib", "test");

    /// <summary>
    /// Returns all (className, methodName) pairs from a CPython test file.
    /// Suitable for use as [MemberData] in Theory tests.
    /// </summary>
    public static IEnumerable<object[]> GetTestMethods(string testFileName)
    {
        var path = Path.Combine(TestRoot, testFileName);
        if (!File.Exists(path))
            return Enumerable.Empty<object[]>();

        return ParseTestMethods(File.ReadAllText(path));
    }

    private static IEnumerable<object[]> ParseTestMethods(string source)
    {
        var results = new List<object[]>();
        var lines = source.Split('\n');

        // Detect class definitions that inherit from unittest.TestCase
        // Handles: class FooTests(unittest.TestCase):
        //          class FooTests(TestCase):
        //          class FooTests(unittest.TestCase, SomeMixin):
        var classPattern = new Regex(
            @"^class\s+(\w+)\s*\(.*(?:unittest\.TestCase|TestCase).*\)\s*:",
            RegexOptions.Compiled);

        // Detect test methods: def test_xxx(self):
        var methodPattern = new Regex(
            @"^\s{4}def\s+(test_\w+)\s*\(self",
            RegexOptions.Compiled);

        string currentClass = null;

        foreach (var line in lines)
        {
            var classMatch = classPattern.Match(line);
            if (classMatch.Success)
            {
                currentClass = classMatch.Groups[1].Value;
                continue;
            }

            if (currentClass != null)
            {
                var methodMatch = methodPattern.Match(line);
                if (methodMatch.Success)
                {
                    results.Add(new object[]
                    {
                        currentClass,
                        methodMatch.Groups[1].Value
                    });
                }

                // Reset class context if we hit a new top-level definition
                if (line.Length > 0 && line[0] != ' ' && line[0] != '\t'
                    && !line.StartsWith("class ") && !line.StartsWith("#"))
                {
                    currentClass = null;
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Returns all CPython test files for a given stdlib module.
    /// Some modules have multiple test files (e.g. collections has test_collections,
    /// test_deque, test_ordered_dict).
    /// </summary>
    public static IEnumerable<string> GetTestFilesForModule(string moduleName)
    {
        return Directory
            .GetFiles(TestRoot, $"test_{moduleName}*.py")
            .Select(Path.GetFileName);
    }
}
```

---

## 12. CI Pipeline Integration

Structure the CI pipeline as three gates. Each gate only runs if the previous one is green.

```yaml
# .github/workflows/naja-tests.yml  (or equivalent for your CI)

jobs:
  gate1_compiler_correctness:
    name: "Gate 1 — Compiler Correctness"
    steps:
      - run: dotnet test Naja.CPythonTests 
               --filter "Category=CompilerCorrectness&XFail!=true"
               --logger "trx;LogFileName=gate1.trx"
    # Must be green before Gate 2 runs

  gate2_stdlib_compilation:
    name: "Gate 2 — Stdlib Compiles"
    needs: gate1_compiler_correctness
    steps:
      - run: dotnet test Naja.CPythonTests
               --filter "Category=StdlibCompilation"
               --logger "trx;LogFileName=gate2.trx"
    # Verifies stdlib modules compile before running behavioral tests

  gate3_cpython_suite:
    name: "Gate 3 — CPython Behavioral Suite"
    needs: gate2_stdlib_compilation
    steps:
      - run: dotnet test Naja.CPythonTests
               --filter "Category=CPythonFullSuite&XFail!=true"
               --logger "trx;LogFileName=gate3.trx"

  nightly_granular:
    name: "Nightly — Granular Per-Method"
    if: github.event_name == 'schedule'
    steps:
      - run: dotnet test Naja.CPythonTests
               --filter "Category=CPythonGranular"
               --logger "trx;LogFileName=granular.trx"
```

### TRX Output for Test History

The `--logger "trx"` flag produces TRX files that most CI systems (GitHub Actions, Azure DevOps, TeamCity) can parse to show test trends over time. You get a graph of passing tests per run, making compiler progress visible without any extra tooling.

---

## 13. Test Naming Conventions

Consistent naming makes filtering and searching reliable.

### xUnit Class Names

```
// Category 1
Phase{N}_{FeatureName}Tests
  e.g. Phase2_GeneratorTests
       Phase2_EvalExecTests
       Phase3_MetaclassTests

// Category 2
{ModuleName}FullSuiteTests
  e.g. MathFullSuiteTests
       CollectionsFullSuiteTests
       OsPathFullSuiteTests

// Category 3
{ModuleName}GranularTests
  e.g. MathGranularTests

// Category 4
Tier{N}CompilationTests
  e.g. Tier1CompilationTests
```

### xUnit Method Names

```
// Category 1 — mirror CPython test naming
CPython_{TestName}_{ShortDescription}
  e.g. CPython_GeneratorStop_SendAndThrow
       CPython_Decorators_CompileAndExec

// Category 2 — always test_{module}_full_suite
test_{module}_full_suite
  e.g. test_math_full_suite
       test_collections_full_suite

// Category 3 — always {module}_test with Theory parameters
{module}_test(string className, string methodName)
  e.g. math_test(MathTests, test_sqrt)

// Category 4 — always {module}_compiles_without_errors
{module}_compiles_without_errors
  e.g. math_compiles_without_errors
       collections_compiles_without_errors
```

---

## 14. Migration Path from Current Tests

You already have tests running. Here is how to migrate without disrupting what works.

### Step 1 — Create the infrastructure (½ day)

Create `Naja.CPythonTests` project. Copy `CPythonTestFixture`, `NajaTestExecutor`, `UnittestOutputParser`, `CPythonTestDiscovery` from this document. Wire up the path configuration via environment variables so it works on both local and CI machines.

### Step 2 — Migrate existing Phase 1/2/3 tests (1 day)

Move your existing passing and XFAIL tests into the `CompilerCorrectness/` folder following the Category 1 pattern. Assign traits. The XFAIL pattern with XPASS detection is the key addition — apply it to every known failure.

At the end of this step your existing test results are reproducible inside xUnit with full output capture.

### Step 3 — Add Category 4 compilation tests for implemented stdlib (½ day)

For every stdlib module you have already implemented (even partially), add a compilation test:

```csharp
[Fact]
[Trait("Category", "StdlibCompilation")]
[Trait("Tier", "1")]
[Trait("StdlibModule", "math")]
public void math_compiles_without_errors()
{
    var result = AssertCompiles(Path.Combine(NajaStdlibRoot, "math.py"));
    Assert.True(result.Success, string.Join("\n", result.Errors));
}
```

This gives you a regression safety net before you even run the behavioral tests.

### Step 4 — Add Category 2 full suite tests one module at a time

As each stdlib module reaches "should be passing" state, add the full suite test. Start with the modules you are most confident about. The test either goes green immediately or tells you exactly what is broken.

### Step 5 — Add Category 3 granular tests for active modules

When you are actively working on a module and need surgical failure visibility, add the granular test class. You do not need granular tests for modules that are fully green — the full suite test is sufficient for regression detection.

---

## Summary

| Category | Class suffix | Trait | Run frequency | Granularity |
|----------|-------------|-------|--------------|-------------|
| 1 — Compiler correctness | `Tests` | `CompilerCorrectness` | Every commit | One Fact per feature |
| 2 — CPython full suite | `FullSuiteTests` | `CPythonFullSuite` | Every commit | One Fact per .py file |
| 3 — Granular per-method | `GranularTests` | `CPythonGranular` | Nightly / on demand | One Theory row per test method |
| 4 — Stdlib compilation | `CompilationTests` | `StdlibCompilation` | Every commit | One Fact per module |

The `ITestOutputHelper` is the key to solving the swallowed output problem. Every test attaches its full Python stdout and stderr to the xUnit result. Failures show the complete Python traceback in the test explorer with no console hunting required.

XFAIL tests use the XPASS detection pattern so compiler improvements are automatically surfaced. A test that was expected to fail but now passes fails the build intentionally, forcing promotion to a proper passing test. Progress is never silent.

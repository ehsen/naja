# Naja Compiler

**Ehsen Siraj** — [linkedin.com/in/ehsen-siraj](https://www.linkedin.com/in/ehsen-siraj/)
*An accountant and ERP builder who decided to build a compiler.*

---

> **Python syntax. .NET runtime.**

> ⚠️ **Active development — not suitable for testing yet. Things will break. A lot. Expect rough edges at every level until a stable release is announced.**

Naja is a compiler that takes **Python 3.x source code** and compiles it directly to **.NET 10 IL** via `System.Reflection.Emit` — no C# intermediate step, no interpreter, no runtime bridge. Write Python, get a genuine .NET assembly that can reference WinForms, WPF, NuGet packages, and any .NET API.

The compiler does extensive dynamic dispatch (boxing, runtime type resolution, reflection-based calls), which is the same model C# uses when you write dynamic code. An AOT publish target is planned but not yet present — until it is, AOT compilation will break the output because the dynamic dispatch paths are not yet AOT-annotated.

*This is the vision — what Naja is being built toward:*

```python
# hello_winforms.naja
from System.Windows.Forms import Form, Button, Application
from System.Drawing import Size

class MainForm(Form):
    def __init__(self):
        self.Text = "Built with Naja"
        self.Size = Size(400, 300)
        btn = Button()
        btn.Text = "Hello from Python IL"
        btn.Click += lambda s, e: print("clicked!")
        self.Controls.Add(btn)

Application.Run(MainForm())
```

```bash
naja run hello_winforms.naja       # compile to memory + run immediately
naja publish hello_winforms.naja   # build self-contained .exe
```

---

## Table of Contents

- [What is Naja?](#what-is-naja)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Implementation Status](#implementation-status)
- [Known Limitations and Bugs](#known-limitations-and-bugs)
- [Open Challenges](#open-challenges)
- [Getting Started](#getting-started)
- [CLI Reference](#cli-reference)
- [MSBuild SDK (.najaproj)](#msbuild-sdk-najaproj)
- [Standard Library](#standard-library)
- [Test Suite](#test-suite)
- [Codebase Navigation](#codebase-navigation)
- [Contributing](#contributing)

---

## What is Naja?

Naja is a genuine effort to bring Python to .NET as a first-class citizen — not by embedding an interpreter or bridging to CPython, but by compiling Python source directly to IL. The goal is that Python code targeting .NET APIs feels natural to write and produces a real .NET assembly: referenceable, publishable, deployable without a Python installation.

This is early, experimental work. The compiler is not feature-complete, the standard library wiring is still being connected, and there are known bugs at every level. What exists today is a working compilation pipeline covering a substantial subset of Python 3 — classes, closures, generators, comprehensions, pattern matching, f-strings, .NET interop, and more. The hard problems are documented honestly in [Known Limitations](#known-limitations-and-bugs) and [Open Challenges](#open-challenges).

---

## Architecture

The compilation pipeline is a straightforward linear chain:

```
.naja source
      │
      ▼
 Naja.Lexer          Token stream (INDENT/DEDENT, all Python 3 literals)
      │
      ▼
 Naja.Parser         Immutable AST (C# records, recursive-descent)
      │
      ▼
 Naja.Semantics      Symbol tables, scope analysis, forward-reference resolution
      │
      ▼
 Naja.Inference      Optional type inference (conservative, purely additive)
      │
      ▼
 Naja.CodeGen        Three-pass IL emission via System.Reflection.Emit
      │
      ▼
 .NET 10 PE (.exe / .dll)
```

### Three-Pass IL Emission (Naja.CodeGen)

The emitter makes three passes over every module to support Python forward references:

| Pass | What it does |
|------|-------------|
| **Pass 1** | Declare all type stubs — `TypeBuilder`, field and method signatures, class constructors. Populates all lookup dictionaries so later passes never see an undefined identifier. |
| **Pass 2** | Emit the module body (`Main()`) — all statements except `def` and `class` at the top level. |
| **Pass 3** | Emit function bodies and class bodies in declaration order. |

### Assembly Structure

```
output.exe
  └── <ModuleName>  (static class — Abstract | Sealed)
        ├── static fields    (module-level variables)
        ├── static methods   (module-level functions)
        └── static Main()    (entry point, runs module body)
  └── UserClass1, UserClass2 ...  (one TypeBuilder per Python class)
```

### A Note on Dynamic Dispatch

All Python values are represented as `object` in IL. Type-specific fast paths are taken only when `Naja.Inference` can prove a static type — which currently covers a small fraction of expressions. This is the same trade-off C# makes with `dynamic`. It is correct, but slower than fully typed IL. Improving inference coverage is the main path to better-performing output. An AOT publish target is a future goal; it requires annotating all dynamic dispatch paths first.

---

## Project Structure

```
Naja/
├── Naja.Lexer/                  Tokenizer
├── Naja.Parser/                 Recursive-descent parser + AST node types
├── Naja.Semantics/              SemanticAnalyzer, SymbolTable, DiagnosticBag
├── Naja.Inference/              TypeInferenceEngine, DynamicSiteAnalyser
├── Naja.CodeGen/                Main IL emitter
│   ├── AssemblyEmitter*.cs      Top-level orchestrator (split into partials)
│   ├── ExpressionEmitter.cs     Expression emission dispatcher
│   ├── StatementEmitter.cs      Statement emission dispatcher
│   ├── Emitters/
│   │   ├── Expressions/         LiteralEmitters, OperatorEmitters, CallEmitters,
│   │   │                        AttributeEmitters, ComprehensionEmitters,
│   │   │                        GeneratorEmitters, LambdaEmitters, ...
│   │   └── Statements/          AssignmentEmitters, ControlFlowEmitters,
│   │                            ExceptionEmitters, DefinitionEmitters, ScopeEmitters
│   └── Builtins/                DynamicOperators, ComparisonOperators, TypeSystem,
│                                StringFunctions, MathFunctions, ReflectionHelpers,
│                                Iterators, NajaMatch, NajaReModule, ...
├── Naja.CLI/                    `naja` command-line tool (compile / run / publish)
├── Naja.SDK/                    MSBuild SDK for .najaproj files
├── Naja.StdLib/                 Monolithic stdlib (legacy, being replaced)
├── Naja.StdLib.Core/            Modular stdlib — sys, math, unittest
├── Naja.StdLib.IO/              Modular stdlib — os, os.path
├── Naja.StdLib.Time/            Modular stdlib — datetime, time
├── Naja.StdLib.Text/            Modular stdlib — string utilities
├── Naja.StdLib.Data/            Modular stdlib — collections, json
├── Naja.CodeGen.Tests/          Language compliance unit tests (xUnit)
├── Naja.CPythonTests/           CPython test-suite runner (optional, needs CPython source)
├── Naja.WinForms.Tests/         WinForms integration tests
├── Naja.Lexer.Tests/
├── Naja.Parser.Tests/
├── Naja.Semantics.Tests/
└── Naja.Inference.Tests/
```

---

## Implementation Status

> ⚠️ **Naja has not been released. No alpha, no beta, no preview. This section documents what has been built inside the compiler, not what you can reliably use today. Do not attempt to build projects with this yet — wait for an announced release.**

### Language Features

| Feature | Status | Notes |
|---------|--------|-------|
| Variables, arithmetic, comparisons | ✅ | All numeric types, string, bool, None |
| `if / elif / else` | ✅ | Including truthiness for all types |
| `while` loop (+ `else`) | ✅ | |
| `for` loop (+ `else`) | ✅ | Iterates lists, ranges, dicts, strings, generators |
| `break`, `continue`, `pass` | ✅ | Nested loops correctly handled |
| Functions (`def`) | ✅ | Default args, `*args`, `**kwargs` |
| Closures & `nonlocal` | ✅ | LEGB rule via hoisted module-level fields |
| `global` keyword | ✅ | |
| Classes & inheritance | ✅ | `__init__`, `super()`, single inheritance |
| Dunder methods | ✅ | `__str__`, `__repr__`, `__len__`, `__eq__`, `__add__`, `__iter__`, `__next__`, etc. |
| `try / except / else / finally` | ✅ | Multiple clauses, `except E as e`, exception chaining (`raise X from Y`) |
| `raise` | ✅ | Bare re-raise, `raise E(msg)`, `raise E from cause` |
| Custom exception classes | ✅ | Class hierarchy, `isinstance` checks in catch |
| `with` statement (context managers) | ✅ | `__enter__` / `__exit__` protocol |
| `assert` | ✅ | With optional message |
| Generators (`yield`, `yield from`) | ✅ | Basic sequences; see limitations below |
| Lambda expressions | ✅ | |
| List / dict / set comprehensions | ✅ | With filters and nested loops |
| Generator expressions | ✅ | |
| `match / case` (PEP 634) | ✅ | Literal, capture, wildcard, OR, AS, class, sequence, mapping, guard patterns |
| F-strings (PEP 498 / PEP 701) | ✅ | Nested f-strings, format specs, `!r` `!s` `!a` conversion flags |
| Slices (`a[start:stop:step]`) | ✅ | On lists and strings |
| Walrus operator (`:=`) | ✅ | In conditions, comprehensions |
| Tuple unpacking | ✅ | Including starred assignments |
| `del` statement | ✅ | |
| `import` / `from … import` | ✅ | Naja stdlib modules + .NET CLR types |
| `import clr` + .NET interop | ✅ | Can instantiate .NET types, call methods, subscribe events |
| `async` / `await` | 🚧 | Parsed, not emitted |
| Decorators | 🚧 | Parsed, partial emission |
| Multiple inheritance | ⚠️ | Parsed; IL emission limited (single parent only in TypeBuilder) |
| `*` / `**` in function calls | ✅ | Spread expansion |
| Big integers (`int` > 64-bit) | ✅ | Via `System.Numerics.BigInteger` |
| Numeric separators (`1_000`) | ✅ | |
| Hex / octal / binary literals | ✅ | |
| Byte strings (`b"..."`) | ✅ | |
| Triple-quoted strings | ✅ | |

### Built-in Functions

`print`, `input`, `range`, `len`, `type`, `isinstance`, `issubclass`, `hasattr`, `getattr`, `setattr`, `callable`, `id`, `hash`, `repr`, `str`, `int`, `float`, `bool`, `list`, `dict`, `set`, `tuple`, `frozenset`, `enumerate`, `zip`, `map`, `filter`, `sorted`, `reversed`, `any`, `all`, `min`, `max`, `sum`, `abs`, `round`, `pow`, `divmod`, `chr`, `ord`, `hex`, `bin`, `oct`, `open`, `iter`, `next`, `vars`, `dir`, `super`, `object`, `staticmethod`, `classmethod`, `property`

### Standard Library Modules

> 🚧 The stdlib modules below are implemented in C# but the import wiring into the compiler is still being connected. Expect `import` to fail or behave incorrectly for most of these until that work lands.

| Module | Status | Implementation |
|--------|--------|----------------|
| `sys` | 🚧 | `NajaSys` — argv, exit, path, version, platform |
| `os` / `os.path` | 🚧 | `NajaOs` — getcwd, listdir, makedirs, environ, path ops |
| `math` | 🚧 | `NajaMath` — floor, ceil, sqrt, log, sin, cos, pi, e, … |
| `re` | 🚧 | `NajaReModule` — match, search, findall, sub, compile |
| `datetime` | 🚧 | `NajaDateTime` — datetime, date, time, timedelta |
| `time` | 🚧 | sleep, time(), monotonic() |
| `random` | 🚧 | random, randint, choice, shuffle, seed |
| `json` | 🚧 | dumps, loads |
| `collections` | 🚧 | namedtuple, defaultdict, Counter, deque |
| `unittest` | 🚧 | `NajaTestCase` — assertEqual, assertTrue, assertRaises, skip, … |
| `io` | ⚠️ | Partial (StringIO) |
| `threading` | ⚠️ | Partial |
| `pathlib` | ⚠️ | Partial |
| `functools` | ⚠️ | Partial (partial, reduce) |
| `itertools` | ⚠️ | Partial |
| `abc` | ⚠️ | Stubs only |

### .NET Interop

```python
import clr
from System.Windows.Forms import Form, Button, Application
from System.Drawing import Color, Font, Size
from System.Collections.Generic import List, Dictionary
from System import Console, Environment, Math
```

- .NET types are resolved at compile time via reflection over all loaded assemblies.
- Property assignment uses `CoerceValue()` to handle .NET type coercion automatically.
- Event subscription (`+=` / `-=`) emits `AddEventHandler` / `RemoveEventHandler` calls.
- Static method calls and instance method calls dispatch through `ReflectionHelpers`.

### CLI and Tooling

- `naja run file.naja` — compiles to memory and executes in-process
- `naja compile` — invoked by MSBuild for incremental builds
- `naja publish` — wraps `dotnet publish` for self-contained single-file exe
- `.najaproj` / `Naja.Sdk` — full MSBuild integration, incremental compilation, WinForms / WPF / console profiles

---

## Known Limitations and Bugs

### 1. Closure / Lambda Variable Capture (Medium Priority)

Variables defined in an outer function scope are sometimes not accessible inside a lambda expression when those variables are assigned after the lambda is defined. The LEGB hoisting strategy promotes variables to module-level static fields, which works for most cases but fails in certain late-binding scenarios.

```python
# FAILS — 'target' not captured correctly
def update(index):
    target = labels[index]
    target.Invoke(lambda: setattr(target, 'Text', "Updated"))
```

**Affected tests:** `test_invoke_required.naja`, `test_closure_capture.naja`

---

### 2. `len()` on .NET-native Types

`len()` works on Naja-managed Python sequences but not on .NET string or collection types that arrive from .NET API calls.

```python
lbl.Text = "A" * 10000
assert len(lbl.Text) == 10000  # fails — lbl.Text is a .NET System.String
```

---

### 3. .NET Enum Instantiation and Member Access

Enum values cannot currently be accessed via dot notation or constructed via pseudo-constructor syntax:

```python
# Both fail:
txt.Dock = DockStyle.Fill        # MissingFieldException
style = FontStyle(1)             # MissingMethodException
```

.NET enums are value types with a different member-resolution path than regular static fields. `ReflectionHelpers.GetStaticAttr` needs to be extended to handle `Enum.GetValues` lookup.

---

### 4. Multiple Inheritance

Python allows multiple base classes; .NET interfaces aside, `TypeBuilder` supports only one parent class. Multiple inheritance beyond mixing in interfaces is not supported and silently uses only the first base class.

---

### 5. Generator Limitations

`yield` inside a `while` loop with a mutable loop counter does not always produce the correct sequence (the loop counter variable hoisting interferes with the generator state machine). The test `Yield_InLoop` is currently disabled.

---

### 6. `async` / `await`

The lexer and parser handle `async def` and `await` correctly. The code generator does not yet emit state-machine IL for async functions. Async code will compile without error but execute synchronously.

---

### 7. Decorator Emission

`@decorator` syntax is parsed. Simple decorators that wrap a function at module level work in common cases. Complex decorators (class decorators, decorators with arguments on methods) are partially supported.

---

### 8. Type Inference Coverage

`Naja.Inference.TypeInferenceEngine` is operational and additive (zero regressions). However, most expressions in practice still fall through to `UnknownType` and take the dynamic path (boxing + `object`-typed locals + runtime dispatch). Until inference covers more of the common patterns, generated IL is significantly larger and slower than it needs to be.

---

## Open Challenges

These are the hardest unsolved problems in the compiler today. They are not bugs but fundamental design questions.

### A — Python's Dynamic Type System vs. .NET Static IL

Python variables are untyped; .NET IL slots are typed. Naja currently represents all Python values as `object` and boxes/unboxes everywhere. This is correct but slow. The path to efficient code is through `Naja.Inference` — once inference can prove a variable is always `long`, the emitter can use a real `int64` local and skip boxing entirely. The infrastructure is in place; coverage needs to grow.

### B — Closure Semantics (Cell Variables)

Python closures use "cell objects" — a layer of indirection so that all closures sharing a variable see the same cell even when the variable is reassigned after the closure is created (the classic `for i in range(10): funcs.append(lambda: i)` gotcha). Naja's current implementation hoists captured variables to static fields, which handles the common case but breaks when the same name is captured by multiple independent closure calls at the same time (e.g., recursive closures).

### C — .NET Interop Type Coercion

When calling a .NET method that expects `System.Int32` and Naja emits a `long` (Python's default int is 64-bit), coercion must happen at the call site. `TypeSystem.CoerceValue` handles this at runtime via reflection. A better solution would be to resolve overloads statically at compile time, which requires tracking the .NET method signature during IL emission.

### D — Multiple Inheritance / Mixin Pattern

.NET `TypeBuilder` does not support multiple base classes (only interfaces). The long-term solution is to lower Python multiple inheritance into explicit interface delegation at IL level, which requires significantly more analysis in the semantic pass.

### E — Modular Standard Library (In Progress)

The original `Naja.StdLib` is a monolithic assembly. It is being replaced by five separate packages (`Naja.StdLib.Core`, `.IO`, `.Time`, `.Text`, `.Data`) with AOT/trimming annotations. This migration is underway but not complete — new code should target the modular packages.

### F — CPython Test Suite Compatibility

`Naja.CPythonTests` can run individual test methods from CPython's own test suite (`Lib/test/test_*.py`) by compiling and running them through Naja. This requires a local CPython source checkout. Passing rate today is a minority of tests — most failures expose either missing stdlib coverage or semantic differences (e.g., exact exception message text, integer overflow behavior, `__repr__` format).

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json`)
- Windows (for WinForms tests; core compiler tests run on any OS)

### Clone and Build

```bash
git clone https://github.com/ehsen/naja
cd naja
dotnet build
```

### Run the Tests

```bash
dotnet test                             # all test projects
dotnet test Naja.CodeGen.Tests          # language compliance only
dotnet test Naja.WinForms.Tests         # WinForms integration
dotnet test Naja.CPythonTests           # CPython suite (needs CPYTHON_TEST_ROOT set)
```

To run CPython tests, clone [cpython](https://github.com/python/cpython) and set:

```powershell
$env:CPYTHON_TEST_ROOT = "C:/dev/cpython/Lib/test"
dotnet test Naja.CPythonTests
```

### Compile and Run a Script

```bash
# Compile to memory and execute immediately
naja run testdata/simple_unittest.py

# Compile to a .dll
naja compile hello.naja -o bin/hello.dll -t exe

# Build and publish a self-contained .exe
naja publish hello.najaproj -r win-x64
```

---

## CLI Reference

```
naja <command> [options]

COMMANDS:
    compile <file.naja> [...]   Compile .naja source to IL (called by MSBuild)
    run     <file.naja>         Compile to memory and execute immediately
    publish <file | project>    Publish a self-contained single-file exe
    version                     Print version info
    help                        Show this help

COMPILE OPTIONS:
    -o, --output <path>           Output .dll or .exe path
    -t, --type   <type>           exe | winexe | library  (default: library)
    -c, --configuration <config>  Debug | Release         (default: Debug)
    -v, --verbose                 Show compilation stages

RUN OPTIONS:
    -v, --verbose                 Show compilation stages before running

PUBLISH OPTIONS:
    -o, --output <dir>            Output directory      (default: bin/Release/publish)
    -t, --type   <type>           exe | winexe          (default: exe)
    -r, --runtime <rid>           Runtime identifier    (default: win-x64)
    -v, --verbose                 Show compilation stages
```

---

## MSBuild SDK (.najaproj)

Naja ships an MSBuild SDK (`Naja.Sdk`) that enables `dotnet build` / `dotnet publish` on Naja projects.

```xml
<!-- hello.najaproj -->
<Project Sdk="Naja.Sdk/0.1.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

For WinForms applications:

```xml
<Project Sdk="Naja.Sdk/0.1.0">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

All `.naja` files in the project directory are compiled automatically (configurable via `EnableDefaultNajaItems`). The SDK resolves the correct framework references (`Console`, `WinForms`, `WPF`, `AspNetCore`) from the profile.

---

## Standard Library

### Module Import Strategy

Naja resolves imports in the following order at compile time:

1. **Naja stdlib modules** — `sys`, `os`, `math`, `re`, `datetime`, `random`, `json`, `collections`, `unittest`, etc. Each maps to a C# class (`NajaSys`, `NajaOs`, `NajaMath`, …).
2. **.NET CLR types** — `from System.Windows.Forms import Form` resolves `System.Windows.Forms.Form` from the loaded assemblies.
3. **Other .naja files** — multi-file compilation is supported via `naja compile file1.naja file2.naja`.

### Modular StdLib Architecture

The stdlib is being migrated to a modular, AOT-friendly layout:

```
Naja.StdLib.Core   — sys, math, unittest (NajaSys, NajaMath, NajaTestCase)
Naja.StdLib.IO     — os, os.path, io     (NajaOs, NajaOsPath)
Naja.StdLib.Time   — datetime, time      (NajaDateTime, NajaTime)
Naja.StdLib.Text   — string utilities
Naja.StdLib.Data   — collections, json
```

All public types carry `[DynamicallyAccessedMembers]` annotations for trimming compatibility.

---

## Test Suite

### Test Projects

| Project | What it tests | Approx. count |
|---------|---------------|---------------|
| `Naja.Lexer.Tests` | Tokenization of all Python constructs | ~80 |
| `Naja.Parser.Tests` | AST shape for every grammar production | ~120 |
| `Naja.Semantics.Tests` | Symbol resolution, scope errors | ~60 |
| `Naja.Inference.Tests` | Type inference results | ~40 |
| `Naja.CodeGen.Tests` | Language compliance — run .naja, assert output | ~467 |
| `Naja.WinForms.Tests` | WinForms script compilation + execution | 28 |
| `Naja.CPythonTests` | CPython `Lib/test/test_*.py` via Naja | Variable |

### Language Compliance Test Areas (`Naja.CodeGen.Tests`)

- `ControlFlowTests` — if/elif/else, for, while, break, continue, walrus
- `ExceptionTests` — try/except/else/finally, raise, chaining, custom hierarchies
- `ScopingTests` — LEGB rule, closures, nonlocal, global
- `GeneratorTests` — yield, yield from, generator protocol
- `ComprehensionTests` — list/dict/set comprehensions, generator expressions
- `PatternMatchingTests` — all PEP 634 pattern types
- `FStringTests` — PEP 498 / PEP 701, format specs, conversion flags
- `NegativeTests` — invalid programs that must be rejected with `CodeGenException`
- `CanaryTests` — full-script end-to-end smoke tests
- `ILDumpTests` — inspect raw IL output for specific patterns
- `CpythonSuiteRunner` — lightweight subset of CPython suite run inline

### Running a Single Test Category

```bash
dotnet test Naja.CodeGen.Tests --filter "FullyQualifiedName~ControlFlowTests"
dotnet test Naja.CodeGen.Tests --filter "FullyQualifiedName~ExceptionTests"
dotnet test Naja.CodeGen.Tests --filter "FullyQualifiedName~ScopingTests"
```

---

## Codebase Navigation

### "Where is X implemented?"

| Task | File(s) |
|------|---------|
| Tokenize Python source | `Naja.Lexer/Lexer.cs`, `IndentTracker.cs` |
| All token types | `Naja.Lexer/TokenType.cs` |
| Parse statements | `Naja.Parser/Parser.StatementParser.cs` |
| Parse expressions | `Naja.Parser/Parser.ExpressionParser.cs` |
| AST node definitions | `Naja.Parser/Statements.cs`, `Expressions.cs` |
| Symbol resolution | `Naja.Semantics/SemanticAnalyzer.cs`, `SymbolTable.cs` |
| Type inference | `Naja.Inference/Engine/TypeInferenceEngine.cs` |
| Dynamic site analysis | `Naja.Inference/Analysis/DynamicSiteAnalyser.cs` |
| Top-level IL orchestration | `Naja.CodeGen/AssemblyEmitter.cs` (+ `*.ClassBody`, `*.ClassDeclaration`, `*.MethodGeneration`, `*.ModuleEmission`) |
| Emit integer / float / string literal | `Naja.CodeGen/Emitters/Expressions/LiteralEmitters.cs` |
| Emit binary / unary operators | `Naja.CodeGen/Emitters/Expressions/OperatorEmitters.cs` |
| Emit function calls | `Naja.CodeGen/Emitters/Expressions/CallEmitters.cs` |
| Emit attribute access, subscripts, slices | `Naja.CodeGen/Emitters/Expressions/AttributeEmitters.cs` |
| Emit comprehensions | `Naja.CodeGen/Emitters/Expressions/ComprehensionEmitters.cs` |
| Emit generators / yield | `Naja.CodeGen/Emitters/Expressions/GeneratorEmitters.cs` |
| Emit lambda | `Naja.CodeGen/Emitters/Expressions/LambdaEmitters.cs` |
| Emit assignments (=, +=, :=, unpack) | `Naja.CodeGen/Emitters/Statements/AssignmentEmitters.cs` |
| Emit if / while / for | `Naja.CodeGen/Emitters/Statements/ControlFlowEmitters.cs` |
| Emit try / except / finally / raise | `Naja.CodeGen/Emitters/Statements/ExceptionEmitters.cs` |
| Emit def / class | `Naja.CodeGen/Emitters/Statements/DefinitionEmitters.cs` |
| Emit nonlocal / global / return | `Naja.CodeGen/Emitters/Statements/ScopeEmitters.cs` |
| Emit match / case patterns | `Naja.CodeGen/StatementEmitter.cs` (EmitMatch), `Builtins/NajaMatch.cs` |
| Dynamic arithmetic (+, -, *, /) | `Naja.CodeGen/Builtins/DynamicOperators.cs` |
| Dynamic comparisons (==, !=, <, >) | `Naja.CodeGen/Builtins/ComparisonOperators.cs` |
| getattr / setattr / DynamicCall | `Naja.CodeGen/Builtins/ReflectionHelpers.cs` |
| type(), isinstance(), CoerceValue | `Naja.CodeGen/Builtins/TypeSystem.cs` |
| range(), len(), enumerate(), zip() | `Naja.CodeGen/NajaBuiltins.cs`, `Builtins/` |
| String methods | `Naja.CodeGen/Builtins/StringFunctions.cs` |
| Math functions | `Naja.CodeGen/Builtins/MathFunctions.cs` |
| Iterator protocol | `Naja.CodeGen/Builtins/Iterators.cs` |
| Regex (`re` module) | `Naja.CodeGen/Builtins/NajaReModule.cs` |
| Slice runtime object | `Naja.CodeGen/NajaSlice.cs` |
| Python exception types | `Naja.CodeGen/PythonExceptions.cs` |
| All builtin method MethodInfo cache | `Naja.CodeGen/Builtins/NajaBuiltinsMethodCache.cs` |
| PE file writer | `Naja.CodeGen/Emitters/Assembly/AssemblyPEWriter.cs` |
| IL dump / disassembly | `Naja.CodeGen/ILDumper.cs` |
| In-memory compile + run | `Naja.CodeGen/NajaEngine.cs` |
| Emit context (locals, labels, scopes) | `Naja.CodeGen/EmitContext.cs` |
| CLI entry point | `Naja.CLI/Program.cs`, `Commands.cs` |
| MSBuild SDK props / targets | `Naja.SDK/Sdk/Sdk.props`, `Sdk.targets` |
| sys module | `Naja.StdLib.Core/NajaSys.cs` |
| os / os.path module | `Naja.StdLib.IO/NajaOs.cs` |
| datetime module | `Naja.StdLib.Time/NajaDateTime.cs` |
| unittest.TestCase | `Naja.StdLib.Core/NajaUnittest.cs` |

### Key Design Patterns

- **All Python values are `object`** — locals, fields, and method parameters are `object` in IL. Type-specific paths are taken only when inference proves the type statically (currently rare).
- **Builtin functions emit direct IL call-sites** — `range()` does not call `NajaBuiltins.Range` via reflection; the emitter inlines the appropriate `ldsfld` / `newobj` / `call` sequence directly.
- **`NajaBuiltinsMethodCache`** — all `MethodInfo` objects for builtin helpers are resolved once at startup via `typeof(X).GetMethod(...)` and cached as static fields. Never call `GetMethod` inside an emit loop.
- **`EmitContext`** — carries the active `ILGenerator`, local variable map, loop label stacks, and scope state. Passed down to every emitter method.

---

## Contributing

### Build Requirements

- .NET 10 SDK (`global.json` pins to `10.0.100`, rolls forward to latest minor)
- Visual Studio 2022+ or Rider (optional — `dotnet build` works everywhere)

### Adding a New Built-in Function

1. Implement the runtime helper in the appropriate `Naja.CodeGen/Builtins/` file.
2. Add a `MethodInfo` entry to `NajaBuiltinsMethodCache`.
3. Add the emit path in `CallEmitters.cs` (or `ExpressionEmitter.cs` for now) under the builtin name check.
4. Add a test in `Naja.CodeGen.Tests`.

### Adding a New Statement Type

1. Add the AST node record to `Naja.Parser/Statements.cs`.
2. Add the parse method to `Naja.Parser/Parser.StatementParser.cs`.
3. Add the semantic analysis case in `Naja.Semantics/SemanticAnalyzer.cs`.
4. Create or extend the appropriate file in `Naja.CodeGen/Emitters/Statements/`.
5. Add a dispatch case in `Naja.CodeGen/StatementEmitter.cs`.
6. Add tests.

### Coding Conventions

- All emitter classes receive an `EmitContext ctx` — never store an `ILGenerator` as a field.
- Use `NajaBuiltinsMethodCache` for all `MethodInfo` lookups — no inline `typeof(X).GetMethod(...)` in emit paths.
- Errors in user code must throw `CodeGenException` with a `[line:col]` prefix — never let a raw `NullReferenceException` reach the user.
- Tests use `NajaEngine.Eval(path)` for in-process execution and `assert` statements inside the `.naja` script for correctness assertions.

---

## Version

**0.1.0** — active development. API and IL output format are not yet stable.

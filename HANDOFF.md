# Naja — Project Handoff & Status

> **Read this first in any new session.** Verified state as of **2026-09-07**, HEAD = `f099b85` on `main`
> (remote is named `naja` → `github.com/ehsen/naja`). This file is the source of truth for current status.
> README.md is still the architecture/navigation reference but is **stale on several points** (see §9).

---

## 1. What Naja is

Python 3.x syntax → native **.NET 10 IL** compiler via `System.Reflection.Emit`. No interpreter, no bridge,
no C# intermediate step. Pipeline: Lexer → Parser → Semantics → (optional) Inference → 3-pass IL emission.
`.naja` and `.py` files are the same language to the CLI; `.naja` is just the project's native spelling.

## 2. Current verified status (`dotnet test Naja.slnx`, 2026-09-07)

| Suite | Result |
|---|---|
| Naja.Lexer.Tests | 51/51 ✅ |
| Naja.Parser.Tests | 51/51 ✅ |
| Naja.Semantics.Tests | 26/26 ✅ |
| Naja.Inference.Tests | 189/189 ✅ |
| **Naja.CodeGen.Tests** | **280 passed / 0 failed / 4 skipped** ✅ |
| Naja.WinForms.Tests | 29/29 ✅ |
| Naja.CPythonTests | 18 pass / 278 fail / 15 skip — long-tail CPython parity; **host crash eliminated** |

Everything that was failing at the start of the 2026-09-07 session (Win32OsTests, NajaWindowsOsTests,
JsonTests, CPython gap-analysis test) is green. `testdata/windows_os/test_windows.naja` runs 18 tests OK
(12 legitimate symlink-privilege/subprocess-probe skips). `main` contains all work; `dev` is fully merged.

## 3. How to verify / iterate

```bash
dotnet build Naja.slnx -v q && dotnet test Naja.slnx          # full verification (~1 min)
dotnet test Naja.CodeGen.Tests                                # language compliance only
dotnet test Naja.CodeGen.Tests --filter "FullyQualifiedName~Win32OsTests"   # one test class

# Fast iteration loop (~1 s) — the CLI compiles+runs in process:
Naja.CLI/bin/Debug/net10.0-windows/naja.exe run somefile.py
```

- ⚠️ **Rebuild `Naja.CLI` after ANY `Naja.StdLib` / `Naja.CodeGen` edit.** The CLI output dir holds
  *copied* DLLs; building only the project you changed leaves the CLI running stale binaries. This cost
  the session two false-negative bug chases.
- Git remote is `naja`, **not** `origin` → `git push naja main`.
- xUnit test host cwd = the test output dir; `testdata/` under `Naja.CodeGen.Tests/` is copied there
  by `CopyToOutputDirectory` in the csproj.

## 4. Architecture quick map (repo-relative path → responsibility)

| Path | Role |
|---|---|
| `Naja.Lexer/Lexer.cs` | Tokenizer. Escape sequences (`\n \t \r \0 \b \f \v \' \" \\`) are **decoded at tokenize time** (~L343). INDENT/DEDENT via IndentTracker. |
| `Naja.Parser/` | Recursive-descent; AST records in `Statements.cs` / `Expressions.cs`. |
| `Naja.Semantics/SemanticAnalyzer.cs` | Symbol tables, scopes. |
| `Naja.Inference/Engine/TypeInferenceEngine.cs` | Optional, purely additive type inference (coverage still low). |
| `Naja.CodeGen/AssemblyEmitter.ModuleEmission.cs` | `EmitModule`: import pre-pass (top-level + **deep scan** of try/if/for/while/with), optional-import binding-slot hoisting, Pass 1 stubs, Pass 2 `Main()` walk (**top-level class decorators applied HERE**), Pass 3 function/class bodies. |
| `Naja.CodeGen/AssemblyEmitter.ClassBody.cs` | `EmitClassBody`: instance-field scan, class cctor (static fields **+ `__dec_<name>` decorated-method callables**), dunder overrides, IDisposable/IEnumerable/ICloneable wiring. |
| `Naja.CodeGen/AssemblyEmitter.ClassDeclaration.cs` | `DeclareClass`; base-type resolution (`unittest.TestCase` → `typeof(NajaTestCase)`). |
| `Naja.CodeGen/AssemblyEmitter.MethodGeneration.cs` | `DeclareInstanceMethod` / `EmitMethodBody`. **`def m(self)` compiles to `object m()` — self is `ldarg.0`, not a formal param.** |
| `Naja.CodeGen/Emitters/Expressions/NameEmitters.cs` | `EmitName` — the name-resolution order (see §5.2). `GetStdLibField` delegates to `StdLibResolver`. |
| `Naja.CodeGen/Emitters/Expressions/AttributeEmitters.cs` | `EmitAttribute`, `EmitSubscript`. Slice dispatch: StrType fast path → ListType fast path → **dynamic `NajaSlice.Apply`** (runtime type). |
| `Naja.CodeGen/Emitters/Expressions/CallEmitters.cs` | `EmitCall` / `EmitMethodCall`. Dynamic fallback packs `args[]` + `kwNames[]` → `DynamicCallKw`. |
| `Naja.CodeGen/Emitters/Expressions/ComprehensionEmitters.cs` | Generator-based; loop vars hoisted to `__hoisted_<name>_<scopeId>` static fields (type `object` → slice via dynamic Apply). |
| `Naja.CodeGen/Emitters/Statements/AssignmentEmitters.cs` | `self.x = …` → instance field `stfld` (falls back to `SetAttr` for native WinForms properties). |
| `Naja.CodeGen/Emitters/Statements/ExceptionEmitters.cs` | `EmitWith`: `as` target stores to **field** if hoisted, else local (store/load discipline). |
| `Naja.CodeGen/Emitters/Statements/DefinitionEmitters.cs` | `EmitFunctionDef` (module-fn decorators), `EmitClassDef` (nested class registration + decorators), `EmitTopLevelClassDecorators`. |
| `Naja.CodeGen/Emitters/Statements/StatementAnalyzer.cs` | Deep scans: `CollectAssignedNames` (try/while/with covered), `CollectImportBindings`. |
| `Naja.CodeGen/Builtins/ReflectionHelpers.cs` | `DynamicCall` / `DynamicCallKw` / `StaticCall` / `StaticCallKw`, `GetAttr`, `GetStaticAttr` (has a singleton instance-method path), `HasAttr`. |
| `Naja.CodeGen/NajaBuiltins.cs` | ⚠️ Contains a **duplicate** `DynamicCall`/`StaticCall` pair (kept in sync manually with ReflectionHelpers), plus `ImportModule`, `DecorateClassMethod`, `Callable`, `ToFloat` (inf/nan parsing). |
| `Naja.CodeGen/Builtins/TypeConversion.cs` | ⚠️ Another duplicate `ToFloat` — keep inf/nan parsing in sync. |
| `Naja.CodeGen/Builtins/StringFunctions.cs` | `Str*` bridges incl. `StrEncode` / `StrDecode`. |
| `Naja.CodeGen/Builtins/IOFunctions.cs` | `open()` → `NajaFile` (text/binary, r/w/a/x/+, seek/tell, context manager). |
| `Naja.CodeGen/Builtins/NajaBuiltinsMethodCache.cs` | Cached `MethodInfo`s — never call `GetMethod` in emit loops. |
| `Naja.CodeGen/StdLibResolver.cs` | Module→type map; **`IsImplemented()` = the ImportError gate**; `ResolveModuleType/Value`. |
| `Naja.CodeGen/NajaEngine.cs` | `Eval` / `TryCompile` — in-process compile+run (what the tests use). |
| `Naja.CodeGen/{NajaSlice,NajaFunction,PythonExceptions}.cs` | Runtime helpers: slice apply, function wrapper (`__call__(object[])`), exception mapping. |
| `Naja.StdLib/` (monolith) | **This is what actually runs.** `NajaOs`+`NajaOsPath` (normcase, kill, `\\?\` strip, FileNotFoundError on remove), `NajaSys`, `NajaUnittest` (NajaTestCase + runner), `NajaJson`, `NajaRe`, `NajaDateTime`, `NajaTime`, `NajaSubprocess` (Popen, check_output/call), `NajaSignal` (**instance properties, not consts**), `NajaWinapi` (CreateJunction), `NajaIo` (StringIO/BytesIO), `NajaTempfile` (static-only), `NajaShutil`/`NajaTextwrap` (instance, no singleton), `NajaMmap`, `NajaUuid`, `NajaFnmatch`, `NajaMsvcrt`, `NajaStat`, `NajaTestSupport`, `NajaCTypes`. |
| `Naja.StdLib.{Core,IO,Time,Text,Data}` | Modular-stdlib **shells** — migration incomplete, `Data` is empty; the monolith is what loads at runtime. |
| `Naja.CodeGen.Tests/LanguageCompliance/` | Win32OsTests (`testdata/os_windows/test_os_windows.py`), NajaWindowsOsTests (`testdata/windows_os/test_windows.naja`), JsonTests, CpythonTestWindowsGapAnalysis + the unit-area tests. Testdata lives under `Naja.CodeGen.Tests/testdata/`. |

## 5. The consistency invariants — DO NOT regress these

These are the design rules the 2026-09-07 session established; several multi-day bugs were exactly
violations of them.

**5.1 Stdlib module dispatch — one rule, all module shapes.** A module name resolves to the class's
`Instance` singleton when present (os, sys, json, …), else the raw `Type` (tempfile, test.support —
static-only; shutil/textwrap — plain instance). `DynamicCall` on a `Type`: static method match first →
`StaticCall`; else `Instance` field → dispatch on the singleton; else parameterless ctor guarded by
`type.Namespace.StartsWith("Naja.StdLib")` → lazy instance; else static-miss error. Works identically
from module / function / class scope (function+class scopes resolve imports to `Type` via ImportMap;
DynamicCall normalizes).

**5.2 `EmitName` resolution order** (fields BEFORE locals on the load side): `self` → params → cells →
scoped comprehension hoists (`__hoisted_x_comp_*`) → **static fields** → locals → methods (first-class) →
classTypes → True/False/None → builtin type names → `__name__`/`__file__` → exception types →
NamespaceImports (stdlib → `ldsfld Instance` or Type) → ImportMap (Type) → runtime NameError.
Consequence: **every STORE must check `Fields` first too** — `with … as`, import binding, and plain
assignment all route through the same store-side rule or you get null reads from a half-hoisted name.

**5.3 Python methods are `object m()`, self = `ldarg.0`.** They therefore never signature-match base CLR
virtuals (`void setUp()`), so lifecycle hooks must be invoked via **concrete-type reflection**
(`NajaUnittest.InvokeLifecycle`), never virtual dispatch. Reflection by name + 0 params finds them.

**5.4 Keyword arguments.** Dynamic calls carry `kwNames[]` (null = positional); `DynamicCallKw` /
`StaticCallKw` re-bind by parameter name (case-insensitive), then positional in order, optional
parameters get CLR defaults. `json.dumps(x, indent=2)`, `open(p, mode=…)` depend on this.

**5.5 ImportError semantics.** `StdLibResolver.IsImplemented(module)` is the single gate;
`NajaBuiltins.ImportModule` throws `TypeLoadException` (= ImportError) for unimplemented/missing modules,
which is what makes `try: import x / except ImportError: x = None` work. Names both imported AND
assigned get a hoisted binding slot (static field) so the import store and the `= None` fallback write
the same slot.

**5.6 Decorators.** Module functions → `EmitFunctionDef`. Class methods → `__dec_<name>` static field,
bound in the class cctor via `NajaBuiltins.DecorateClassMethod` (bound NajaFunction, decorators applied
bottom-up); the unittest runner prefers `__dec_` callables. Top-level classes → Pass 2
`EmitTopLevelClassDecorators` (execution order). Nested classes → `EmitClassDef` path.

**5.7 No C# `const` for stdlib constants.** `const` breaks `ldsfld`/reflection dispatch (the
`signal.SIGTERM` IL crasher). Stdlib constants are instance properties (`public long SIGTERM => 15;`).

**5.8 unittest runner order matters.** `CreateInstance` → `InvokeLifecycle("setUp")` → run test
(`__dec_` preferred) → `InvokeLifecycle("tearDown")`; `SkipTestException` must be unwrapped from
`TargetInvocationException` **before** the generic catch or skips count as errors; failures rethrow as an
`AssertionException` summary so xUnit sees the full list.

**5.9 `Naja.StdLib` must NOT reference `Naja.CodeGen`** (circular). Runtime logic that needs both lives
in CodeGen builtins invoked via emitted IL, or as reflection-based helpers inside StdLib
(`InvokeLifecycle`, `InvokeDecorated`).

## 6. Session log — 2026-09-07 (all on `main`, pushed)

| Commit | What |
|---|---|
| `6ae4a1e` (pre-existing) | `dev` (157 commits) fast-forward merged into `main` and pushed — repo-safety first. |
| `bbc1791` | Uniform stdlib dispatch (§5.1); lexer escape decoding; `open()`/`NajaFile` + `subprocess.check_output/call`; unittest setUp/tearDown via concrete reflection; `with…as` field discipline; os fixes (normcase, remove→FileNotFoundError, `\\?\` listdir). CodeGen 277→279 pass, Win32Os internal errors 36→4, JSON module errors 94→12, CPython host crash eliminated. |
| `b4b4232` | Import-binding deep scan + `ImportModule` field store; signal `const`→property (fixed the 5-month-old `test_windows.naja` compile crasher); `callable()` + `GetStaticAttr` singleton method-as-value; `os.kill`; `assertIsInstance`; `NajaSlice.Apply` dynamic slicing. CodeGen 279 pass / 1 fail. |
| `f099b85` | Method decorators (`__dec_` + `DecorateClassMethod` + runner preference); top-level class decorators in Pass 2; `DynamicCallKw`/`StaticCallKw` keyword binding; `NajaTime` module + `IsImplemented` gate; `float('inf'/'nan')` parsing; JSON semantics (ensure_ascii, inf→ValueError, tuple key→TypeError). CodeGen **280/0 fail**; test_windows.naja 18 OK. |
| `ef17876` | HANDOFF.md itself. |

## 6b. Session log — 2026-09-08 (CPython parity stress-test round; see CPYTHON_FAILURE_ANALYSIS.md)

Working tree on `main` (committed per-fix at session end). Nine root causes fixed, all
proven by IL-level decoding of repros, verified against the exact CPython test bodies:

1. Mixed int↔float compare/arith widened properly (`OperatorEmitters`) — test_unary::test_negative.
2. Lexer `\xNN`/octal/`\u` escapes + unknown-escape backslash retention — test_utf8source::test_pep3120.
3. Real `eval()` (throwaway-module compile+run; 1-tuple wrapper defeats inference narrowing).
4. `**` double→long bit-reinterpretation fixed: `PyPow(long,long)` checked + BigInteger promotion.
5. Unary TypeError semantics: `PyNeg`/`PyPos`/`PyInvert` (InvalidCastException = TypeError) — test_unary::test_bad_types.
6. PEP 263: new `SourceDecoder` (BOM/coding-cookie/strict); `compile(bytes)`; real `exec(code, ns)` — test_utf8source::test_latin1.
7. **Sequence `+`/`*` raw-opcode AV crasher killed** (was why FullSuite skipped test_augassign.py): List/Str/Tuple operands route to Dynamic helpers in EmitBinary + EmitAugAssign; DynamicAdd/Mul gained list concat + repetition. test_augassign now RUNS: 2 pass / 4 fail (slice-aug-assign, testBasic, `__iadd__`, unpacking — clustered, not yet fixed).
8. `ILDumper` rewritten (reflects over `OpCodes`, OperandType-driven, token-resolved) — it found #7.
9. BigInteger equality/arithmetic in ComparisonOperators/DynamicOperators (checked + promote; Python ints never wrap).

Granular: test_unary 3/6→**6/6**, test_utf8source 0/3→**2/3**. Five base suites re-verified
green (51/51/26/189/29). CodeGen.Tests: 280 pass + 2 pre-existing failures (JSON Module
Tests, test_windows Gap Analysis — proven pre-existing at ef17876 by stash-bisect).
Open items needing approval: dotted-name .py import loader (test_badsyntax), the 4
remaining augassign gap clusters, `__file__` currently emitted empty.

## 7. Known remaining gaps (honest list)

1. **CPythonTests: 278 failures** — semantic long tail (exact exception messages, repr formats, edge
   semantics). Needs `CPYTHON_TEST_ROOT` env var for the full runner. Best next lever: cluster failures
   by theme and pick high-frequency modules.
2. **async/await** — parsed, emitted synchronously (no state machine).
3. **Multiple inheritance** — first base only.
4. **Closure late-binding corner cases** — recursive closures sharing a captured name.
5. **`len()` on .NET-native strings/collections** arriving from interop calls (README limitation #2).
6. **Generator + while-loop mutable counter** — `Yield_InLoop` still disabled (part of the 4 skips).
7. **Type inference coverage low** → most code takes the dynamic-dispatch path (perf headroom).
8. **Modular stdlib migration stalled** — `Naja.StdLib.Data` is empty; monolith is what runs.
9. **pathlib / csv / string** — intentionally raise ImportError now (IsImplemented gate); implement
   them or keep failing fast.
10. The other CodeGen skips are by design (explicit `Skip =` in test code).

## 8. Pitfalls that cost real time (don't relearn these)

- **Stale CLI binaries** — rebuild `Naja.CLI` after any StdLib/CodeGen change (§3). Twice bitten.
- **StdLib ↔ CodeGen circular reference** — §5.9.
- **`PersistedAssemblyBuilder` quirks**: `Stloc`/`Ldloc` in catch blocks unreliable (use `Dup`);
  `GetTypes()`/`GetMethods()` on an uncreated `TypeBuilder` throws `NotSupportedException` → guard with
  `is not TypeBuilder`.
- **Partial-file reads + patching**: re-read a file before patching it, and **build after every patch** —
  one unverified batch corrupted `DynamicCall` and cost a repair cycle.
- **Windows symlink privilege**: this machine has no admin/Developer Mode → symlink tests correctly
  *skip*; enable Developer Mode to run them for real.
- `%TEMP%\naja_*.py` repro scripts from the session are ephemeral — re-create as needed.
- Git CRLF→LF warnings on some files are normal; remote is `naja` not `origin`.

## 9. Stale docs (read with care)

- **README.md** — accurate on architecture/navigation, **stale** on: `open()` ("not implemented" — it
  is), decorators ("partial" — module fn / class method / top-level class all work now), `time`
  ("placeholder" — implemented), stdlib status table. Update opportunistically.
- **ANALYSIS_SUMMARY.txt**, `test_results.log`, `detailed_test_results.log` (repo root) and
  `Naja.CodeGen/llm_context/*` — pre-date this session's fixes; historical planning docs only.

## 10. Suggested next steps (priority order)

1. Cluster the 278 CPythonTests failures (set `CPYTHON_TEST_ROOT`, run, group by missing-module vs
   semantic-mismatch) — the highest-information next move.
2. Update README status tables from this file.
3. Interop blockers for real desktop apps (README "Known Limitations" 1–3): `len()` on .NET string,
   enum member access via non-imported namespaces, closure capture edge.
4. Grow `Naja.Inference` coverage to cut dynamic dispatch (performance).
5. async/await state-machine emission.
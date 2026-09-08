# Naja — Project Handoff & Status

> **Read this first in any new session.** Verified state as of **2026-09-08 PM**, HEAD = `f3ffbab` on `main`
> (remote is named `naja` → `github.com/ehsen/naja`). This file is the source of truth for current status.
> README.md is still the architecture/navigation reference but is **stale on several points** (see §9).
> ⚠️ **Working tree has UNCOMMITTED fixes** (F1/F2/F3 + watchdog + docs) — see §6c before anything else.

---

## 1. What Naja is

Python 3.x syntax → native **.NET 10 IL** compiler via `System.Reflection.Emit`. No interpreter, no bridge,
no C# intermediate step. Pipeline: Lexer → Parser → Semantics → (optional) Inference → 3-pass IL emission.
`.naja` and `.py` files are the same language to the CLI; `.naja` is just the project's native spelling.

## 2. Current verified status (2026-09-08 PM, after F1/F2/F3 — uncommitted tree)

| Suite | Result |
|---|---|
| Naja.Lexer.Tests | 51/51 ✅ |
| Naja.Parser.Tests | 51/51 ✅ |
| Naja.Semantics.Tests | 26/26 ✅ |
| Naja.Inference.Tests | 189/189 ✅ |
| **Naja.CodeGen.Tests** | **288 passed / 2 failed / 4 skipped** (+8 over the 280 baseline; the 2 failures are pre-existing: JSON Module Tests, test_windows Gap Analysis — proven at ef17876 via stash-bisect) ⚠️ "Test Run Aborted" prints AFTER the summary — host dies at teardown, investigate (§6c-C-2) |
| Naja.WinForms.Tests | 29/29 ✅ |
| ConfirmedPassing (CI gate) | 3 pass / 0 fail / 15 skip ✅ |
| CPython granular | int_literal 6/6, utf8source 2/3, unary 6/6, generator_stop 2/2, exception_variations 30, decorators 7, **scope 0, super 0, compare 0** (never investigated — next clusters) |
| test_named_expressions.py | runs to completion: 74 tests, 37 pass (was: infinite wedge) |
| test_augassign.py | runs: 2 pass / 4 fail (was: AV hard-crash) |

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

## 6c. Session state — 2026-09-08 PM (walrus/field-store round) ⚠️ READ FIRST

### A. UNCOMMITTED working tree (verify with `git status`)

All fixes below are built, verified against repros + regression, but **NOT committed**.
Commit per-fix and push `naja main` as the FIRST action of the next session:

| File | Fix |
|---|---|
| `Naja.CodeGen/Emitters/Statements/AssignmentEmitters.cs` | **F1**: `EmitStore` hoisted-field path now converts to the field's CLR type before `stsfld` (Int/Bool→double `conv.r8`; Float/Bool→long `conv.i8`; Unknown→`unbox.any`/`castclass string`). Was: raw bit reinterpretation — `e = 0.0; e = 3 // 2` → `5E-324`; in a while loop this poisoned the condition → the infinite loop that wedged the suite. |
| `Naja.CodeGen/Emitters/Expressions/ControlFlowEmitters.cs` | **F2**: `EmitWalrus` (non-comprehension path) now stores the hoisted FIELD (same conversion rules as F1) AND the local when `_ctx.Fields` contains the target. Was: stored only a local while `EmitName` reads fields first → `while a > (d := 3)` left `d == 0`. Watch out: variable renamed `walrusHoistedField` (name collision with the comprehension-path `walrusField` in the same method). |
| `Naja.CodeGen/Builtins/MathFunctions.cs` | **F3**: new `PyFloorDivDynamic(object,object)` — BigInteger-exact, int-like→exact long floor division, IConvertible→`Math.Floor` double fallback, `DivideByZeroException` ("ZeroDivisionError: …"), `InvalidCastException` TypeError otherwise. |
| `Naja.CodeGen/Builtins/NajaBuiltinsMethodCache.cs` | `PyFloorDivDynamic_Method` entry. |
| `Naja.CodeGen/Emitters/Expressions/OperatorEmitters.cs` | FloorDiv Unknown branch routes through `PyFloorDivDynamic` (returns Unknown) instead of ToFloat-everything (`3 // 2` → int `1`, not `1.0`). |
| `Naja.CodeGen.Tests/LanguageCompliance/CpythonSuiteRunner.cs` | **Watchdog**: `CPython_BulkSuite_PassRate` runs each file on a background thread with a 60s `Join` budget; timeout → recorded `[Watchdog] TIMEOUT` failure, thread abandoned. |
| `CPYTHON_FAILURE_ANALYSIS.md` | Bugs 10/11 + F3 + harness marked FIXED with fix details. |

Verified before writing this: hang repro → `result: 1` (CPython-exact, terminates);
`5E-324` repro → `1.0`; walrus repro → `d == 3`; test_named_expressions.py 74 tests
37 pass; `CPython_NamedExpressions` + `CPython_LongExp` xUnit facts PASS; base suites
51/51/26/189/29 green; CodeGen 288/2/4; ConfirmedPassing 3/0/15.

### B. Pending investigation results (logs exist, unread)

Two background runs finished; their logs are at `%TEMP%\naja_cpy\` (`C:\Users\Ehsen\AppData\Local\Temp\naja_cpy\`):
1. **`suite_final.log`** — all 19 `CpythonSuiteRunner` facts with the fixed tree (was: wedged).
2. **`bulk_watchdog.log`** — full 392-file `CPython_BulkSuite_PassRate` under the new watchdog —
   the honest full-suite pass rate; will contain the first-ever complete bulk failure list.
Read both first; they inform the next gap clusters.

### C. Ordered pending work (user-approved direction: stress-test via CPython's own tests only,
simple → hard, root-cause, document, ASK APPROVAL before each fix; never invent tests)

1. **Commit + push** the §6c-A tree (per-fix commits), after reading B's logs.
2. **Investigate the teardown abort** — CodeGen summary prints (288/2/4) then "Test Run Aborted":
   host dies at process teardown, likely abandoned watchdog threads still running guest IL during
   exit (my watchdog leaves hangers alive by design). Cheap checks: does the abort happen with
   `--filter` runs that exclude bulk? Is it new (post-watchdog) or pre-existing? If watchdog-related,
   consider `Environment.FailFast`-free alternatives: thread-abort is net-core-blocked, so maybe
   track abandoned threads and `Join` them at teardown, or run bulk in a child process.
3. **Scope / super / compare granular areas — 0 passes each.** Next natural clusters; never
   investigated. Scope showed `testComplexDefinitions`/`testFreeingCell`/`testListCompLocalVars`
   failing in ≤450ms; super `test_class_getattr_working`/`test_unbound_method_transfer_working`/
   `test_shadowed_global`; compare `test_issue_1393`/`test_sets`/`test_str_subclass`. Start here —
   likely a few root causes each unblock many FullSuite files.
4. **Previously documented open items** (from CPYTHON_FAILURE_ANALYSIS.md):
   - Dotted-name `.py` import loader (sys.path/package search through SourceDecoder) — blocks
     ONLY test_utf8source::test_badsyntax (needs `import test.tokenizedata.badsyntax_pep3120`
     to raise SyntaxError naming 'utf-8'). A real feature, needs approval.
   - 4 augassign clusters: slice-aug-assign (`x[1:2] *= 2` → NajaSlice vs IConvertible cast),
     testBasic `2 != 3` numeric aug-assign, `__iadd__` dunder dispatch on user classes,
     `test_with_unpacking` (expects SyntaxError Naja doesn't raise).
   - `__file__` emitted as `ldstr ""` (ModuleEmission.cs ~L440) — needed by import-from-script-dir.
   - test_named_expressions remaining 37 fails: missing `subTest` in NajaUnittest, `__x` → `_Foo__x`
     name mangling, etc.
   - 2 pre-existing CodeGen failures (JSON Module Tests, test_windows Gap Analysis
     "Specified method is not supported" at NajaEngine.cs:172) — classify or fix at leisure.

### D. Method notes that made this round fast (keep doing)

- **Bulk-replica harness**: `%TEMP%\naja_dump\{NajaDump.csproj,Program.cs}` references Naja.CodeGen
  and replicates the bulk loop with per-file `START/OK/ERR` progress printing — a hang pinpoints in
  minutes. Program.cs currently holds the replica (not the IL dumper anymore).
- **Timeout-bisect**: `timeout 20 naja.exe run <file>` (exit 124 = hang) + per-method output-char
  counting (`FFEEFF...` — Naja's unittest runner prints one char per method).
- **Head-N bisect pitfall**: truncating a test file cuts off `unittest.main()` → methods never run →
  false negatives. Only bisect files whose harness entry still executes.
- **Stale-stash trap (NEW, cost a repair cycle)**: `git stash pop` on a CLEAN tree pops an OLD
  unrelated stash (here: `WIP on dev: 554ef67` LEGB work) → merge conflicts in untouched files.
  ALWAYS `git stash list` + `git status` before popping; the old `dev` stash is still in the list
  (safe to drop: the LEGB work was ff-merged into main long ago).
- ILDumper works now: `NajaEngine.Eval(path, dumpIL:true)` → `%TEMP%\naja_il_<asm>.txt`.

## 7. Known remaining gaps (honest list)

1. **CPythonTests long tail** — granular passcounts (2026-09-08 PM): int_literal 6/6, utf8source 2/3,
   unary 6/6, generator_stop 2/2, exception_variations 30, decorators 7; **scope 0, super 0,
   compare 0 = next clusters**. test_named_expressions 37/74, test_augassign 2/6 now RUN
   (both were crash/skip). Full-suite honest rate: read `%TEMP%\naja_cpy\bulk_watchdog.log` (§6c-B).
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
11. **Dotted-name .py import loader missing** — `import test.tokenizedata.badsyntax_pep3120` raises
    plain ImportError; any import-from-script-dir feature needs it (+ real `__file__`).
12. **`subTest` not implemented in NajaUnittest** — breaks several modern CPython test files.
13. **Name mangling (`__x` → `_Cls__x`) not implemented** — breaks test_named_expressions scope tests.
14. **Slice aug-assign** — `x[1:2] *= 2` routes NajaSlice into a numeric cast → InvalidCastException.

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
- **`git stash pop` on a clean tree pops an OLD stash** (§6c-D) — always `git stash list` first.
- **stsfld/stloc never converts** — any store into a typed slot must emit the conversion itself
  (F1 pattern in `AssignmentEmitters.EmitStore`). Cost the suite a 20-minute wedge.
- **Raw numeric opcodes on reference-typed operands = AV crash** — List/Str/Tuple operands must
  route to Dynamic* helpers in EmitBinary AND EmitAugAssign AND EmitSingleComparison.
- **`conv.*` converts the TOP of the stack** (right operand) — left-side widening needs
  stash/convert/reload.
- **Head-N bisect lies when the harness entry is truncated** (§6c-D).
- **The bulk suite wedges without the watchdog** — never run `CPython_BulkSuite_PassRate`
  unguarded; one hanging guest file eats the whole 392-file loop.

## 9. Stale docs (read with care)

- **README.md** — accurate on architecture/navigation, **stale** on: `open()` ("not implemented" — it
  is), decorators ("partial" — module fn / class method / top-level class all work now), `time`
  ("placeholder" — implemented), stdlib status table. Update opportunistically.
- **ANALYSIS_SUMMARY.txt**, `test_results.log`, `detailed_test_results.log` (repo root) and
  `Naja.CodeGen/llm_context/*` — pre-date this session's fixes; historical planning docs only.

## 10. Suggested next steps (priority order — session start 2026-09-08 PM+)

1. **Read the two unread logs** (§6c-B): `%TEMP%\naja_cpy\suite_final.log` and
   `%TEMP%\naja_cpy\bulk_watchdog.log` (the honest 392-file pass rate + full failure list).
2. **Commit + push** the §6c-A tree (per-fix commits: F1, F2, F3, watchdog, docs).
3. **Investigate the CodeGen teardown abort** (§6c-C-2) — summary prints then "Test Run Aborted".
4. **Scope / super / compare clusters** (§6c-C-3) — all three granular areas at 0 passes;
   root-cause the 3 visible methods each, fix, document, verify.
5. **Dotted-name import loader + real `__file__`** (§7-11) — needs approval; unlocks
   test_badsyntax and import-from-script-dir generally.
6. **augassign clusters** (§6c-C-4): slice-aug-assign, testBasic, `__iadd__`, unpacking.
7. **`subTest` + name mangling** (§7-12/13) — unlock more of test_named_expressions' 37 fails.
8. Update README status tables from this file; keep §2 current after every round.
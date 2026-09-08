# CPython Test Parity — Failure Analysis

> Stress-test exercise: run CPython's own `Lib/test` suite through the Naja
> compiler, starting from the simplest areas. Each failing area is root-caused
> at the IL level (not guessed), fixed, and verified against the exact test
> body before moving on. Session: 2026-09-07/08. Base: `ef17876`.

## Method

- Granular per-method rows (`Naja.CPythonTests/Granular`) for wired areas;
  `FullSuite` (one row per `test_*.py`) for breadth.
- Every failure minimized to a ≤10-line repro via the CLI
  (`Naja.CLI/bin/Debug/net10.0-windows/naja.exe run <repro.py>`).
- Root cause proven by **decoding the emitted IL** of the repro (token-resolved
  opcode walk) — never by reading the emitter source alone.
- No invented tests: only real CPython test files/repros.

## Fixed this session

### 1. Mixed numeric comparison/arithmetic (test_unary::test_negative) — FIXED
`5.0 == 5` → False. IL showed `ldc.r8 5.0; ldc.i4.5; conv.i8; ceq` — raw
`ceq` between float64 and int64 **bit patterns**. `EmitSingleComparison` and
Add/Sub had no int↔float widening (Mul/Div/Mod already did). Fix: widen the
int side (careful: `conv.r8` converts the TOP of stack — left-side widening
needs stash/convert/reload), bool↔int normalized to int64.
Files: `OperatorEmitters.cs`.

### 2. `\xNN`/octal/`\uNNNN` escapes + unknown-escape backslash (test_utf8source::test_pep3120) — FIXED
`list(b"\xd0\x9f")` → `[120, 100, 48, ...]` = literal chars "xd0xd9f": the
lexer's escape switch had no hex/octal cases; `\x` fell to `_ => esc`.
Also unknown escapes (`"\П"`) dropped the backslash (Python keeps it).
Fix: `TryReadHexEscape` + octal loop + `\a` + backslash retention.
Files: `Lexer.cs`. (assertEqual's byte[] handling was already correct.)

### 3. eval() unimplemented (test_unary::test_no_overflow / test_bad_types) — FIXED
Real `eval()`: parse-strictly-as-expression check (SyntaxError for
statements), compile `__naja_eval_result = (<expr>,)` to a throwaway module
(the 1-tuple defeats inference narrowing to long, which crashed `stfld` on
BigInteger results), run Main, read the static field, unwrap.

### 4. `**` double→long bit reinterpretation (test_no_overflow) — FIXED
`2**10` → `4652218415073722368`: `Math.Pow` (double) result stored into a
long field — raw IEEE bits reinterpreted. And `10**32` wrapped instead of
promoting. Fix: new `MathFunctions.PyPow(long,long)` — checked integer power,
BigInteger promotion on overflow; `PyPowDynamic(object,object)` for dynamic
operands; emitter routes int**int through PyPow (stays int, CPython
semantics), floats keep Math.Pow.

### 5. Unary TypeError semantics (test_bad_types) — FIXED
`~2.0`, `-'a'`, `~2j` must raise TypeError. `EmitUnary` emitted raw `not`/
`neg` for any operand. Fix: `PyNeg`/`PyPos`/`PyInvert` runtime helpers with
type checks throwing `InvalidCastException` (the CLR type "TypeError" maps
to, so `assertRaises(TypeError)` matches); int/float keep opcode fast paths.

### 6. PEP 263 coding declarations + compile(bytes)/exec(ns) (test_utf8source::test_latin1) — FIXED
New `SourceDecoder`: BOM precedence, `# coding:` cookie (first 2 lines),
strict decode, CPython-style message `'utf-8' codec can't decode byte...`
(message must CONTAIN the codec name — test_badsyntax asserts on it).
`NajaEngine` reads all sources through it. `compile()` accepts bytes;
`exec(code, ns)` really executes and copies public static fields into ns
(temp files written as UTF-8 **with BOM** so BOM beats any cookie).

### 7. Sequence `+`/`*` raw-opcode crashes (test_augassign AV) — FIXED
`x + [3,4]` / `x += [3,4]` / `x *= 2` emitted raw `add`/`mul` on two
references → pointer arithmetic → **AccessViolationException** (hard host
crash; the reason FullSuite hard-skips test_augassign.py). IL dump proved it:
`ldsfld x; newobj List..ctor; ...; add`. Fix: ListType/StrType/TupleType
operands route to `DynamicAdd`/`DynamicMul` in both `EmitBinary` and
`EmitAugAssign`; DynamicAdd/DynamicMul gained list concat + repetition
(new-list semantics). test_augassign now RUNS (was: crash): 2 pass, 4 fail
on separate gaps below.

### 8. ILDumper was garbage (tooling) — FIXED
Hand-typed opcode table was wrong (`0x72`→"conv.ovf.i1.un" vs actual
`ldstr`) and operand sizes were misread → desynced garbage dumps.
Fix: table built by reflecting over `System.Reflection.Emit.OpCodes` fields,
operand bytes consumed per `OperandType`, tokens resolved via module.
This tool found bug #7's root cause in minutes after the rewrite.

### 9. DynamicEq / DynamicAdd/Sub/Mul BigInteger (enabler) — FIXED
BigInteger isn't IConvertible: `bigint == long` fell to `Equals` → false;
`bigint + int` threw TypeError. Fix: exact BigInteger comparison paths and
checked long arithmetic promoting to BigInteger (Python ints never wrap).

## Granular scoreboard (before → after)

| Area | Start | Now |
|---|---|---|
| test_int_literal | 6/6 | 6/6 |
| test_generator_stop | 2/2 | 2/2 |
| test_unary | 3/6 | **6/6** |
| test_utf8source | 0/3 | **2/3** |
| test_augassign (FullSuite) | hard-crash | runs: 2 pass / 4 fail |

## Open items (root-caused, awaiting approval)

1. **test_utf8source::test_badsyntax** — needs `import
   test.tokenizedata.badsyntax_pep3120` to raise SyntaxError('utf-8'...).
   Requires a **dotted-name .py file import loader** (sys.path search,
   packages, SourceDecoder integration, caching) — a real feature, not a
   patch. Currently raises ImportError, which `except SyntaxError` misses.
2. **test_augassign remaining 4 failures** — new gap clusters:
   - slice aug-assign (`x[1:2] *= 2`): NajaSlice hits Convert.ToInt64 →
     InvalidCastException (slice targets need their own emitter path).
   - testBasic `2 != 3`: numeric aug-assign semantic diff (investigate).
   - testCustomMethods1: `__iadd__` dunder dispatch on user classes.
   - test_with_unpacking: augmented assignment with unpacking targets.
3. **`__file__` is emitted empty** (`ldstr ""`) — needed by any import-from-
   script-dir feature and several CPython tests.
4. **Pre-existing xUnit failures** (verified at HEAD via stash-bisect, NOT
   caused by this session): `JSON Module Tests`, `test_windows Gap Analysis`
   — both assert differently than their CPython counterpart semantics.

## Pre-existing-but-now-visible

- The AV crasher (fixed as #7) had been masking the true CodeGen.Tests
  totals: runs abort mid-suite when it fires. HANDOFF's "280/0" and current
  "280 pass / 2 fail" describe the same tree measured under different
  run-order aborts. Re-baseline after #7 lands.

## Post-push findings — 2026-09-08 PM (bulk-suite hang investigation)

The CodeGen `dotnet test` hang (testhost burned 20+ min CPU) was traced with
a bulk-replica harness (one static engine + per-file progress, mirroring
`CpythonSuiteRunner.CPython_BulkSuite_PassRate`) to
**test_named_expressions.py** — previously masked because the (now-fixed)
test_augassign AV killed the run earlier in alphabetical order. Two
compounding bugs, both minimal-repro'd via CLI:

### 10. Module-field store never converts — raw bit reinterpretation (FIXED as F1)
`e = 0.0; e = 3 // 2` → prints `5E-324` (int64 bits of `1` read as double).
The module assignment `stsfld` path writes the raw evaluation-stack value into
the inference-typed static field with **no conversion** when the expression
type ≠ field type. Same class as the Pow stsfld bug (#4), but at the
ASSIGNMENT store. In a `while` loop this is lethal:
```python
a = 9; n = 2; x = 3; d = 0
while a > d:
    d = x // a**(n-1)      # RHS: Unknown (Pow returns object) → ToFloat →
    a = ((n-1)*a + d) // n # PyFloorDivF → FLOAT on stack
```
Trace: `d = 3 // 2` produced `4607182418800017408` = IEEE bits of `1.0` stored
into the long-typed field. `a` then fills with garbage, the condition never
turns false → **the actual infinite loop** (also the FullSuite wedge).
**F1 (2026-09-08 PM, `AssignmentEmitters.EmitStore`)**: hoisted-field stores
now convert to the field's actual CLR type before `stsfld` —
Int/Bool→double via `conv.r8`, Float/Bool→long via `conv.i8`,
Unknown→`unbox.any`/`castclass`. Verified: `e = 0.0; e = 3 // 2` → `1.0`;
the hang repro converges to CPython's `1` and terminates.

### 11. Walrus stores to a local while reads hit the field (FIXED as F2)
`a = 9; while a > (d := 3): a = 1` → prints `d == 0`. `EmitWalrus` (non-
comprehension path) declared/used a LOCAL, but Pass 1 hoists the walrus
target to a FIELD and `NameEmitters` reads fields before locals — the write
landed in a dead slot. CPython prints `d == 3`.
**F2 (2026-09-08 PM, `ControlFlowEmitters.EmitWalrus`)**: when the target is
hoisted to a field, store the FIELD (with F1's conversion rules) AND the
local. Verified: walrus repro prints `result: 1 3`.

### F3 — exact dynamic FloorDiv (FIXED)
FloorDiv's Unknown branch unconditionally `ToFloat`'d both operands —
int/BigInteger operands silently lost exactness (`3 // 2` → 1.0, not int 1).
**Fixed**: new `MathFunctions.PyFloorDivDynamic(object,object)` —
BigInteger-exact, int-like → exact long floor division, IConvertible →
double floor fallback, proper ZeroDivisionError, TypeError otherwise;
emitter's Unknown branch routes through it and returns Unknown.
Verified: `3 // 2` with Unknown operands → `1` (int), not `1.0`.

### Harness note (FIXED)
`CPython_BulkSuite_PassRate` runs 392 files with **no per-file timeout** —
one hanger wedged the whole suite. **Fixed**: per-file watchdog thread with
60s budget; a timeout is recorded as `[Watchdog] TIMEOUT` failure and the
thread is abandoned. test_named_expressions.py now runs to completion
(74 tests: 37 pass / 37 fail on genuine documented gaps: missing `subTest`,
name mangling `__x` → `_Foo__x`, etc.).

## Fixed this round — 2026-09-08 PM+3 (from-import + test.support round)

### 12. From-import pushed the module Type as the member VALUE (FIXED as fix 2, commit e918d30)
Every `from <stdlib> import <member>` name resolved to the module's CLR
**Type** — no member resolution at all. Signatures: `print(pi)` →
`Naja.StdLib.NajaMath`; `@cpython_only` → phantom constructor call →
MissingMethodException in the class cctor → **all 41 test_scope.py methods
error at once** (`The type initializer for 'ScopeTests' threw`). The same
bug class blocked test_compare's `ALWAYS_EQ` and any first-class member use.
**Fixed**: `FromImportMembers` map (module/type/member) recorded in the
EmitModule pre-pass, copied to Pass 2 + all 4 Pass-3 sites;
`ReflectionHelpers.ImportFromMember` resolution ladder (instance method →
bound NajaFunction; instance prop/field → VALUE; static prop; enum; static
field; static method → MethodInfo; else exact-CPython-wording ImportError);
`NameEmitters` step 6a.5 ahead of ImportMap; `NajaFunction.__call__` learns
the whole-args `Func<object[],object?>` convention (was Int64→Object[]
ArgumentException); CallEmitters/AttributeEmitters exclude member names from
the ImportMap static branches (value is the member, not a Type).
Verified: pi/sqrt in module+def+class scope; CPython-wording ImportError;
**scope granular 0/41 → 13/41**. Note: from-import is LAZY — ImportError
raises at first use, not at the import statement.

### 13. test.support members cpython_only / gc_collect / check_syntax_error (FIXED as fix 3, commit 91fb0c5)
All three were missing → whole test classes died at import. `check_syntax_error`
is a faithful port of support/__init__.py:826 (compile + errtext regex +
lineno/offset asserts), reaching `TypeSystem.Compile` via raw reflection
(StdLib must not reference CodeGen). `SyntaxErrorException` gained
lineno/offset; `TypeSystem.Compile` threads ParseException/LexerException
line/col into them. Verified: error path + lineno kwarg + decorated fn + gc.

### Re-baselined honest CodeGen.Tests total (first-ever COMPLETE run)
With the bare-raise crasher (bc5b4a5) fixed, the suite no longer aborts
mid-run: **549 passed / 7 failed / 13 skipped of 569**. The old "288/2/4"
was a partial printed at the abort point. All 7 failures stash-bisect to
**pre-existing at f452f4c**: Power + DictComp_With_Filter assert stale
pre-c90af78 float semantics (`2**3` → `8.0`; CPython prints `8` — tests
need updating, not the code), Canary/Generators_FullScript/
Nonlocal_IndependentClosureInstances (LEGB/generator gaps), plus the 2
documented JSON/Gap-Analysis failures.

### New pre-existing bug found (NOT fixed, needs approval)
User-class instantiation **with args** when only a parameterless ctor
exists: CallEmitters emits the args then `newobj` defaultCtor consumes none
→ stack imbalance → `Common Language Runtime detected an invalid program`.
Repro (no imports involved): `class T: pass` + `t = T("x")`.

## Granular scoreboard (after PM+3 round)

| Area | Start | Now |
|---|---|---|
| test_int_literal | 6/6 | 6/6 |
| test_generator_stop | 2/2 | 2/2 |
| test_unary | 3/6 | **6/6** |
| test_utf8source | 0/3 | **2/3** |
| test_exception_variations | — | 30/30 |
| test_decorators | — | 7/16 |
| test_scope | 0/41 | **13/41** (was cctor-dead; now individual semantics fails) |
| test_compare | 0/16 | 0/16 (now a clean ImportError: needs ALWAYS_EQ + fractions/decimal) |
| test_super | 0 | not re-run this round |
| test_augassign (FullSuite) | hard-crash | runs: 2 pass / 4 fail |

test_raise.py: 37 tests, 16 pass / 21 fail (context/cause semantics — unchanged).

### Scope remaining-28 cluster names (for next round)
testBoundAndFree, testCellIsArgAndEscapes, testCellIsKwonlyArg,
testCellIsLocalAndEscapes, testCellLeak, testClassAndGlobal,
testClassNamespaceOverridesClosure, testComplexDefinitions,
testEvalExecFreeVars, testEvalFreeVars, testGlobalInParallelNestedFunctions,
testInteractionWithTraceFunc, testLeaks, testLocalsClass, …
First signature seen: `AttributeError: 'function' object has no attribute
'__closure__'` — closure-introspection surface.
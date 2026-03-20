# Naja Compiler Refactoring — Remaining Work Plan

**Prepared**: January 2026  
**Branch**: `refactor/codebase-organization`  
**Based on**: Actual file measurements vs `REFACTORING_PLAN.md` targets  
**Build Status**: ✅ Passing  
**Test Status**: ✅ 275/467 Passing (baseline maintained)

> ⚠️ **Note to team**: The existing status tracking files (`REFACTORING_OVERALL_STATUS.md`,
> `REFACTORING_STATUS_ANALYSIS.md`, `REFACTORING_COMPLETION_CHECKLIST.md`) contain inaccurate
> line counts and completion percentages. This document is based on **actual file measurements**
> taken January 2026 and supersedes all prior status docs.

---

## Actual Current State (Measured)

| File | Actual Lines | Plan Target | Status |
|------|-------------|-------------|--------|
| `NajaBuiltins.cs` | **2,524** | < 100 (facade) | 🔴 Not started |
| `ExpressionEmitter.cs` | **779** | ~100-150 (dispatcher) | 🟠 Incomplete wiring |
| `AssemblyEmitter.ClassGeneration.cs` | **746** | ~300-400 | 🟠 Needs split |
| `Parser.ExpressionParser.cs` | **655** | ~400 | 🟡 Marginally over |
| `CallEmitters.cs` | **571** | ~350 | 🟡 Acceptable |
| `OperatorEmitters.cs` | **522** | ~400 | 🟡 Acceptable |
| `AssignmentEmitters.cs` | **520** | ~300 | 🟡 Acceptable |
| `StatementEmitter.cs` | **216** | ~100 dispatcher | ✅ Done |
| `AssemblyEmitter.cs` | **263** | ~300 orchestrator | ✅ Done |
| `Parser.cs` | **101** | ~200 orchestrator | ✅ Done |

---

## What Is Fully Complete ✅

### Phase 1 — Foundation
All directories, base classes, and documentation scaffolding in place.

### Phase 4 — Statement Emitters (100%)
`StatementEmitter.cs` reduced from **1,551 → 216 lines**.

| File | Lines | Notes |
|------|-------|-------|
| `StatementEmitter.cs` | 216 | Thin dispatcher ✅ |
| `Emitters/Statements/StatementEmitterBase.cs` | 123 | Base + utilities ✅ |
| `Emitters/Statements/AssignmentEmitters.cs` | 520 | Assign, AnnAssign, AugAssign ✅ |
| `Emitters/Statements/ControlFlowEmitters.cs` | 232 | If, While, For, Match, Break, Continue ✅ |
| `Emitters/Statements/ExceptionEmitters.cs` | 273 | Try, Raise, Assert ✅ |
| `Emitters/Statements/DefinitionEmitters.cs` | 399 | FunctionDef, ClassDef, decorators ✅ |
| `Emitters/Statements/ScopeEmitters.cs` | 104 | With, Global, Nonlocal ✅ |
| `Emitters/Statements/StatementAnalyzer.cs` | 236 | Name analysis utilities ✅ |

### Phase 6 — Parser (100%, partial-class approach)
`Parser.cs` reduced from **1,288 → 101 lines**.

| File | Lines | Notes |
|------|-------|-------|
| `Parser.cs` | 101 | Orchestrator, token management ✅ |
| `Parser.ExpressionParser.cs` | 655 | All expression parsing ✅ |
| `Parser.StatementParser.cs` | 560 | All statement parsing ✅ |
| `Parser.PatternParser.cs` | 186 | match/case pattern parsing ✅ |

### Phase 3 — Builtins (Skeleton complete, implementations are independent)
All **10 specialist files** in `Builtins/` have been created with independent implementations
(0 `NajaBuiltins.` delegation calls for: `DynamicOperators`, `ComparisonOperators`,
`ReflectionHelpers`, `MathFunctions`, `StringFunctions`, `Iterators`).

| File | Lines | Implementation | Notes |
|------|-------|---------------|-------|
| `Builtins/DynamicOperators.cs` | 175 | ✅ Independent | 0 NajaBuiltins refs |
| `Builtins/ComparisonOperators.cs` | 116 | ✅ Independent | 0 NajaBuiltins refs |
| `Builtins/ReflectionHelpers.cs` | 412 | ✅ Independent | 0 NajaBuiltins refs |
| `Builtins/MathFunctions.cs` | 78 | ✅ Independent | 0 NajaBuiltins refs |
| `Builtins/StringFunctions.cs` | 99 | ✅ Independent | 0 NajaBuiltins refs |
| `Builtins/Iterators.cs` | 80 | ✅ Independent | 0 NajaBuiltins refs |
| `Builtins/TypeSystem.cs` | 168 | ⚠️ Near-independent | 1 residual `NajaBuiltins.ToStr` call |
| `Builtins/Collections.cs` | 44 | ❌ Facade only | 11 forwarding calls to NajaBuiltins |
| `Builtins/IOFunctions.cs` | 30 | ⚠️ Partial | Print/Input done, Open is a stub |
| `Builtins/TypeConversion.cs` | 25 | ❌ Facade only | 5 forwarding calls to NajaBuiltins |

### Phase 3 — Assembly Emitter (Mostly complete, partial-class approach)
`AssemblyEmitter.cs` reduced from **1,646 → 263 lines** using partial classes.

| File | Lines | Notes |
|------|-------|-------|
| `AssemblyEmitter.cs` | 263 | Orchestrator partial class ✅ |
| `AssemblyEmitter.ModuleEmission.cs` | 347 | Module-level code ✅ |
| `AssemblyEmitter.MethodGeneration.cs` | 257 | Method/function bodies ✅ |
| `Emitters/Assembly/AssemblyPEWriter.cs` | 127 | PE file generation ✅ |
| `Emitters/Assembly/FrameworkTypeResolver.cs` | 142 | Reference assembly resolution ✅ |
| `Emitters/Assembly/TypeDeclaration.cs` | 107 | Pass-1 type stub declarations ✅ |

---

## What Remains — Ordered by Priority

---

### 🔴 TASK 1 — Complete `ExpressionEmitter.cs` Dispatcher Wiring
**Priority**: High | **Effort**: 0.5 day | **Risk**: Low

**Problem**: `ExpressionEmitter.cs` is 779 lines. The constructor already instantiates
`OperatorEmitters`, `CallEmitters`, etc., and dispatches to them — but `LiteralEmitters`
and `CollectionEmitters` were never wired. The `Emit()` switch still routes literals and
collections to private inline methods that duplicate what the specialist files already do.

**Evidence** — Current `Emit()` switch (wrong):
```csharp
IntLiteral e => EmitInt(e),      // ← calls local inline method
ListExpr e => EmitList(e),       // ← calls local inline method
```

**Target** — After fix:
```csharp
IntLiteral e => _literalEmitters.EmitLiteral(e),
ListExpr e => _collectionEmitters.EmitCollection(e),
```

#### Checklist

- [ ] **1.1** Add `_literalEmitters` and `_collectionEmitters` fields to `ExpressionEmitter`
- [ ] **1.2** Instantiate them in the constructor: `new LiteralEmitters(ctx)` and `new CollectionEmitters(ctx, this)`
- [ ] **1.3** Update `Emit()` switch to route `IntLiteral`, `FloatLiteral`, `StringLiteral`, `BoolLiteral`, `NoneLiteral`, `EllipsisLiteral` → `_literalEmitters.EmitLiteral(e)`
- [ ] **1.4** Update `Emit()` switch to route `ListExpr`, `TupleExpr`, `SetExpr`, `DictExpr`, `StarredExpr` → `_collectionEmitters.EmitCollection(e)` (add `StarredExpr` support to `CollectionEmitters` if missing)
- [ ] **1.5** Delete the now-redundant private methods: `EmitInt`, `EmitFloat`, `EmitString`, `EmitBool`, `EmitNone`, `EmitEllipsis`, `EmitList`, `EmitTuple`, `EmitDict`, `EmitSet`, `EmitStarred`
- [ ] **1.6** Verify `EmitWalrus` is fully implemented in `ControlFlowEmitters` — if yes, remove local copy; `Emit()` already routes `WalrusExpr` to `_controlFlowEmitters.EmitWalrus(e)` ✅
- [ ] **1.7** Verify `EmitYield`/`EmitYieldFrom` are fully implemented in `GeneratorEmitters` — if yes, remove local copies; `Emit()` already routes `YieldExpr` to `_generatorEmitters.EmitYield(e)` ✅
- [ ] **1.8** Move `EmitComprehensionHelper`, `EmitComprehensionLoopsHelper`, `EmitComprehensionLoops`, `StoreComprehensionTarget` into `ComprehensionEmitters.cs` (these are comprehension infrastructure, not dispatcher logic). `EmitComprehensionLoopsHelper` is already `internal` — make it a method of `ComprehensionEmitters`
- [ ] **1.9** Move utility methods `EmitCoercion`, `EmitDefaultValue`, `ParseCastTarget`, `DescribeExpr`, `ConvertToDouble` into `ExpressionEmitterBase.cs` (as `protected static` where possible)
- [ ] **1.10** Run `Naja.CodeGen.Tests` — all tests must pass
- [ ] **1.11** Verify `ExpressionEmitter.cs` is ≤ 150 lines (pure dispatcher + constructor)

**Files changed**: `ExpressionEmitter.cs`, `CollectionEmitters.cs`, `ComprehensionEmitters.cs`, `ExpressionEmitterBase.cs`

---

### 🔴 TASK 2 — Extract `NajaBuiltins.cs` Content into Specialist Files
**Priority**: Critical | **Effort**: 3–4 days | **Risk**: Medium

**Problem**: `NajaBuiltins.cs` is **2,524 lines** with 131 public static methods. It has grown
since the refactoring began. The specialist `Builtins/` files were created but the original
monolith was never modified. `NajaBuiltinsMethodCache.cs` has **123** entries all pointing
to `typeof(NajaBuiltins).GetMethod(...)` — these are the critical call sites that lock the
monolith in place.

#### Sub-task 2A — Make `Collections.cs` and `TypeConversion.cs` Independent
**Effort**: 0.5 day

- [ ] **2A.1** Copy `Len()`, `Range()`, `Enumerate()`, `Zip()`, `Map()`, `Filter()`, `Any()`, `All()`, `Sorted()`, `MakeList()`, `MakeDict()` implementations from `NajaBuiltins.cs` directly into `Collections.cs` (remove the 11 forwarding stubs)
- [ ] **2A.2** Copy `ToStr()`, `ToBool()`, `ToInt()`, `ToFloat()`, `Repr()` implementations from `NajaBuiltins.cs` into `TypeConversion.cs` (remove the 5 forwarding stubs)
- [ ] **2A.3** Fix the 1 residual `NajaBuiltins.ToStr()` call in `TypeSystem.cs` → `TypeConversion.ToStr()`
- [ ] **2A.4** Fix the 1 residual `NajaBuiltins.PyMod()` call in `DynamicOperators.cs` → inline or local helper
- [ ] **2A.5** Build and run tests

#### Sub-task 2B — Audit and Distribute Remaining Methods
**Effort**: 1–1.5 days

The following method groups in `NajaBuiltins.cs` have no specialist file yet. Each needs a home:

| NajaBuiltins.cs Section | Method Count | Target File |
|------------------------|-------------|------------|
| Dynamic method dispatch (`DynamicCall`, `StaticCall`) | ~4 | `ReflectionHelpers.cs` ✅ (already exists) |
| Subscript helpers (`GetItem`, `SetItem`) | ~4 | `ReflectionHelpers.cs` |
| Context manager helpers (`EnterContext`, `ExitContext`) | ~3 | New: `ContextManagers.cs` or `ScopeEmitters` |
| Starred unpack helpers (`UnpackIterable`, `PackArgs`) | ~5 | `Collections.cs` |
| Membership (`Contains`, `NotContains`) | ~3 | `ComparisonOperators.cs` |
| `id()` / `hash()` | ~2 | `ReflectionHelpers.cs` |
| `hasattr()` / `callable()` | ~3 | `ReflectionHelpers.cs` |
| `vars()` / `dir()` | ~3 | `ReflectionHelpers.cs` |
| `format()` | ~2 | `StringFunctions.cs` |
| `iter()` / `next()` | ~4 | `Iterators.cs` ✅ (already exists) |
| `isinstance()` with tuple of types | ~2 | `TypeSystem.cs` ✅ (already exists) |
| String method bridge | ~15 | `StringFunctions.cs` |
| List method bridge | ~10 | `Collections.cs` |
| Dict method bridge | ~10 | `Collections.cs` |
| Exception helpers (`EnsureException`, `SetExceptionCause`) | ~4 | New: `ExceptionHelpers.cs` |
| Assert helper | ~1 | `ReflectionHelpers.cs` or `ExceptionHelpers.cs` |
| WinForms preamble helpers | ~3 | `AssemblyEmitter` area |
| First-class callable helpers | ~4 | `ReflectionHelpers.cs` |
| PyMod, PyFloorDiv (exact integer arithmetic) | ~6 | `DynamicOperators.cs` ✅ (already exists) |
| `CreateDotNet` | ~2 | `TypeSystem.cs` |

- [ ] **2B.1** For each group above, move the implementation to the target file
- [ ] **2B.2** Build after each group move — do not batch all moves into one commit
- [ ] **2B.3** Run tests after each group move

#### Sub-task 2C — Update `NajaBuiltinsMethodCache.cs` (Critical Blocker)
**Effort**: 1 day  
**Note**: This is the hardest sub-task. Every IL emitter calls builtins through this cache.

`NajaBuiltinsMethodCache.cs` contains 123 entries like:
```csharp
public static readonly MethodInfo DynamicAdd_Method =
    typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicAdd))!;
```

Each entry must be updated to point to the owning specialist class:
```csharp
public static readonly MethodInfo DynamicAdd_Method =
    typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.DynamicAdd))!;
```

- [ ] **2C.1** Create a mapping table: `NajaBuiltins.XYZ` → `SpecialistClass.XYZ` (use Sub-task 2B table above as the source)
- [ ] **2C.2** Update each of the 123 `MethodInfo` entries in `NajaBuiltinsMethodCache.cs`
- [ ] **2C.3** Update the 4 direct `typeof(NajaBuiltins).GetMethod(...)` calls that remain outside the cache:
  - `ExpressionEmitter.cs` — `NajaBuiltins.ToStr` in `EmitCoercion` (→ `TypeConversion.ToStr`)
  - `ExpressionEmitter.cs` — `NajaBuiltins.ToInt`, `NajaBuiltins.ToFloat`, `NajaBuiltins.ToBool` (→ `TypeConversion.*`)
  - `AttributeEmitters.cs` — `NajaBuiltins.GetStaticAttr` (→ `ReflectionHelpers.GetStaticAttr`)
  - `CallEmitters.cs` — `NajaBuiltins.ToStr`, `NajaBuiltins.CreateDotNet` (→ their new owners)
  - `AssignmentEmitters.cs` — `NajaBuiltins.AddEventHandler` / `RemoveEventHandler` (→ `ReflectionHelpers`)
  - `ExceptionEmitters.cs` — `NajaBuiltins.SetExceptionCause` (→ `ExceptionHelpers`)
  - `StatementEmitter.cs` — `NajaBuiltins.UnpackIterable` (→ `Collections`)
- [ ] **2C.4** Build — fix any method-not-found runtime errors
- [ ] **2C.5** Run full test suite: `Naja.CodeGen.Tests` + `Naja.WinForms.Tests`

#### Sub-task 2D — Trim `NajaBuiltins.cs` to Facade or Delete
**Effort**: 0.5 day

Once all methods are in specialist files and `NajaBuiltinsMethodCache.cs` no longer points
to `NajaBuiltins`, there are two options:

**Option A (Recommended)**: Keep `NajaBuiltins.cs` as a thin backward-compatibility facade
```csharp
// NajaBuiltins.cs — thin facade, ~50 lines
public static class NajaBuiltins
{
    public static object DynamicAdd(object a, object b) => DynamicOperators.DynamicAdd(a, b);
    // ... one-liner forwarding for any external consumers
}
```

**Option B**: Delete `NajaBuiltins.cs` entirely (only safe if no external project references it directly via IL at runtime — check `Naja.WinForms.Tests` and any downstream consumers).

- [ ] **2D.1** Search all `.naja` test files for `NajaBuiltins` references (none expected)
- [ ] **2D.2** Check `Naja.SDK`, `Naja.CLI`, and any other projects for compile-time references to `NajaBuiltins`
- [ ] **2D.3** Decide Option A or B based on findings
- [ ] **2D.4** Implement chosen option; build and test
- [ ] **2D.5** Verify `NajaBuiltins.cs` is ≤ 100 lines (or deleted)

**Files changed**: `NajaBuiltins.cs`, `NajaBuiltinsMethodCache.cs`, `Collections.cs`, `TypeConversion.cs`, `TypeSystem.cs`, `DynamicOperators.cs`, `ReflectionHelpers.cs`, `StringFunctions.cs`, `MathFunctions.cs`, `Iterators.cs` (possibly new `ExceptionHelpers.cs`)

---

### 🟠 TASK 3 — Split `AssemblyEmitter.ClassGeneration.cs` (746 lines)
**Priority**: Medium | **Effort**: 1 day | **Risk**: Medium

**Problem**: `AssemblyEmitter.ClassGeneration.cs` at 746 lines is the last file over the
600-line plan limit in the assembly layer. It mixes two distinct concerns:
- **Pass 1**: `DeclareClass()` — class stub and type builder creation
- **Pass 3**: `EmitClassBody()` — full class implementation, constructor wiring, dunder overrides

**Additionally**: Two empty placeholder files need resolution:
- `AssemblyEmitter.Module.cs` (0 lines) — placeholder
- `Emitters/Assembly/ModuleEmitter.cs` (0 lines) — placeholder

#### Checklist

- [ ] **3.1** Read `AssemblyEmitter.ClassGeneration.cs` fully — identify the exact line boundary between `DeclareClass` (stub) and `EmitClassBody` (implementation)
- [ ] **3.2** Create `AssemblyEmitter.ClassDeclaration.cs` — move `DeclareClass()` and all Pass-1 type-stub logic (approx. 250–300 lines)
- [ ] **3.3** Rename remaining content of `AssemblyEmitter.ClassGeneration.cs` to `AssemblyEmitter.ClassBody.cs` — should be ~400-450 lines covering constructor wiring, dunder overrides (`ToString`, `Equals`, `GetHashCode`, `IDisposable`, `IEnumerable`, etc.)
- [ ] **3.4** Delete the original `AssemblyEmitter.ClassGeneration.cs` file
- [ ] **3.5** Decide fate of `AssemblyEmitter.Module.cs` (0 lines): if module-level logic is fully in `AssemblyEmitter.ModuleEmission.cs`, delete the empty file; otherwise populate it
- [ ] **3.6** Decide fate of `Emitters/Assembly/ModuleEmitter.cs` (0 lines): if the design intent is to eventually move module emission to a standalone non-partial class, leave with a `TODO` comment; otherwise delete
- [ ] **3.7** Build and run `Naja.CodeGen.Tests` + `Naja.WinForms.Tests`

**Files changed**: `AssemblyEmitter.ClassGeneration.cs` (split/delete), new `AssemblyEmitter.ClassDeclaration.cs`, new `AssemblyEmitter.ClassBody.cs`, `AssemblyEmitter.Module.cs` (resolve), `Emitters/Assembly/ModuleEmitter.cs` (resolve)

---

### 🟡 TASK 4 — Resolve Minor Line Count Overages (Optional Polish)
**Priority**: Low | **Effort**: 0.5–1 day | **Risk**: Low

These files exceed the plan targets but are **functionally complete** and acceptable for now.
Address only if the team has bandwidth after Tasks 1–3.

| File | Lines | Plan Target | Suggested Action |
|------|-------|-------------|-----------------|
| `Parser.ExpressionParser.cs` | 655 | ~400 | Extract `ParserHelpers.cs` (~150 lines) for `Expect()`, `SkipNewlines()`, `IsAtEnd()`, precedence tables |
| `CallEmitters.cs` | 571 | ~350 | Extract parameter packing/unpacking logic to `CallParameterHelper.cs` (~150 lines) |
| `OperatorEmitters.cs` | 522 | ~400 | Optional: split binary vs. comparison operators |
| `AssignmentEmitters.cs` | 520 | ~300 | Optional: split augmented assignments from plain assignments |
| `DefinitionEmitters.cs` | 399 | ~250 | At plan limit, no action needed |

---

### 🟡 TASK 5 — Final Validation Pass
**Priority**: High (must do before merging) | **Effort**: 0.5 day | **Risk**: Low

- [ ] **5.1** Run the complete test suite: `Naja.CodeGen.Tests`, `Naja.Parser.Tests`, `Naja.WinForms.Tests`, `Naja.Semantics.Tests`, `Naja.Lexer.Tests`
- [ ] **5.2** Confirm no file in `Naja.CodeGen/` exceeds 600 lines (excluding `NajaBuiltinsMethodCache.cs` which may remain large as a registry)
- [ ] **5.3** Confirm `ExpressionEmitter.cs` is a pure dispatcher (≤150 lines)
- [ ] **5.4** Confirm `NajaBuiltins.cs` is ≤ 100 lines (facade) or deleted
- [ ] **5.5** Confirm `AssemblyEmitter.ClassGeneration.cs` no longer exists
- [ ] **5.6** Run a benchmark build (compile a medium `.naja` project) — confirm build time is within ±5% of pre-refactoring baseline
- [ ] **5.7** Update `REFACTORING_OVERALL_STATUS.md` to reflect actual final state
- [ ] **5.8** Merge `refactor/codebase-organization` → `main`

---

## Execution Timeline

```
Week 1
├── Task 1: ExpressionEmitter wiring       (0.5 day)  ← Start here, low risk
└── Task 2A: Collections + TypeConversion  (0.5 day)

Week 2
├── Task 2B: Method distribution audit     (1.5 days)
└── Task 2C: NajaBuiltinsMethodCache       (1 day)

Week 3
├── Task 2D: Trim NajaBuiltins.cs          (0.5 day)
├── Task 3: ClassGeneration split          (1 day)
└── Task 5: Final validation               (0.5 day)

Week 4 (optional)
└── Task 4: Minor line count overages
```

**Total estimate**: ~7–9 working days for Tasks 1–3 + 5  
**Risk**: Task 2C (`NajaBuiltinsMethodCache.cs` migration) is the highest-risk item — allocate buffer

---

## Dependency Graph

```
Task 1 ─────────────────────────────────── independent
Task 2A ──┐
Task 2B ──┤──► Task 2C ──► Task 2D ──► Task 5
Task 3 ───────────────────────────────── independent
Task 4 ─────────────────────────────────── independent (any time)
```

Task 1 and Task 3 are **fully independent** of the NajaBuiltins work and can be done in parallel.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `NajaBuiltinsMethodCache.cs` migration breaks IL call sites at runtime | Medium | High | Do one method group at a time; run tests after each group |
| Comprehension infrastructure move (`StoreComprehensionTarget` etc.) breaks scoping | Low | High | Run `ComprehensionTests` + `ScopingTests` after Task 1.8 specifically |
| `AssemblyEmitter.ClassGeneration.cs` split breaks dunder override wiring | Low | Medium | Run `CanaryTests` (dunder suite) after Task 3 |
| `NajaBuiltins.cs` is referenced by IL in compiled `.naja` programs at runtime | Low | High | Verify with Task 2D.1 before deleting/trimming |
| `EmitCoercion` / `ParseCastTarget` used by `CallEmitters` after being moved | Low | Low | Check `Find All References` before deleting from `ExpressionEmitter.cs` |

---

## Quick Reference — File Ownership After Completion

| Concern | File |
|---------|------|
| Dynamic `+`, `-`, `*`, `/`, `//`, `%`, `**` | `Builtins/DynamicOperators.cs` |
| Dynamic `==`, `!=`, `<`, `>`, `<=`, `>=` | `Builtins/ComparisonOperators.cs` |
| `len`, `range`, `enumerate`, `zip`, `map`, `filter`, `any`, `all` | `Builtins/Collections.cs` |
| `print`, `input`, `open` | `Builtins/IOFunctions.cs` |
| `abs`, `min`, `max`, `sum`, `pow`, `round`, `divmod` | `Builtins/MathFunctions.cs` |
| `chr`, `ord`, `hex`, `bin`, `oct`, string bridges | `Builtins/StringFunctions.cs` |
| `ToStr`, `ToBool`, `ToInt`, `ToFloat`, `Repr` | `Builtins/TypeConversion.cs` |
| `isinstance`, `type`, `CreateDotNet` | `Builtins/TypeSystem.cs` |
| `iter`, `next`, iterator protocol | `Builtins/Iterators.cs` |
| `getattr`, `setattr`, `getitem`, `setitem`, `DynamicCall`, events | `Builtins/ReflectionHelpers.cs` |
| Exception cause, `EnsureException` | `Builtins/ExceptionHelpers.cs` (new) |
| IL method cache (all MethodInfo lookups) | `Builtins/NajaBuiltinsMethodCache.cs` |
| Emit int/float/string/bool/None literals | `Emitters/Expressions/LiteralEmitters.cs` |
| Emit list/tuple/dict/set/starred | `Emitters/Expressions/CollectionEmitters.cs` |
| Emit `+`, `-`, `==`, `and`, `or`, etc. | `Emitters/Expressions/OperatorEmitters.cs` |
| Emit function/method/builtin calls | `Emitters/Expressions/CallEmitters.cs` |
| Emit `obj.attr`, `obj[x]`, slices | `Emitters/Expressions/AttributeEmitters.cs` |
| Emit `x if c else y`, `x := expr` | `Emitters/Expressions/ControlFlowEmitters.cs` |
| Emit `[x for x in y]`, `{k:v for ...}` | `Emitters/Expressions/ComprehensionEmitters.cs` |
| Emit `yield`, `yield from` | `Emitters/Expressions/GeneratorEmitters.cs` |
| Emit `lambda` | `Emitters/Expressions/LambdaEmitters.cs` |
| Emit f-strings | `Emitters/Expressions/FStringEmitters.cs` |
| Resolve names, closures, globals | `Emitters/Expressions/NameEmitters.cs` |
| Emit `x = y`, `x += y` | `Emitters/Statements/AssignmentEmitters.cs` |
| Emit `if`, `while`, `for`, `match` | `Emitters/Statements/ControlFlowEmitters.cs` |
| Emit `try/except/finally`, `raise`, `assert` | `Emitters/Statements/ExceptionEmitters.cs` |
| Emit `def`, `class`, decorators | `Emitters/Statements/DefinitionEmitters.cs` |
| Emit `with`, `global`, `nonlocal` | `Emitters/Statements/ScopeEmitters.cs` |
| Emit class stubs (Pass 1) | `AssemblyEmitter.ClassDeclaration.cs` |
| Emit class bodies (Pass 3) | `AssemblyEmitter.ClassBody.cs` |
| Emit method/function bodies | `AssemblyEmitter.MethodGeneration.cs` |
| Emit module-level code | `AssemblyEmitter.ModuleEmission.cs` |
| Write PE file | `Emitters/Assembly/AssemblyPEWriter.cs` |
| Resolve framework assemblies | `Emitters/Assembly/FrameworkTypeResolver.cs` |
| Parse expressions | `Parser.ExpressionParser.cs` |
| Parse statements | `Parser.StatementParser.cs` |
| Parse match/case patterns | `Parser.PatternParser.cs` |

---

*Document Version: 2.0 — Based on actual file measurements*  
*Supersedes: REFACTORING_OVERALL_STATUS.md, REFACTORING_STATUS_ANALYSIS.md, REFACTORING_COMPLETION_CHECKLIST.md*  
*Author: Naja Development Team*  
*Status: Approved for team handoff*

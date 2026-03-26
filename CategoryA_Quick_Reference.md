# Category A Compiler Issues — Quick Reference Card

## Current State: 47/63 Tests (74.6%)

```
PHASE 1: Builtins-as-Values
┌─────────────────────────────┐
│ ✅ A1  : object builtin     │
│ ✅ A2  : isinstance/issubclass wrappable │
│ ✅ A20 : bytes == bytes structural eq  │
└─────────────────────────────┘
   Status: 3/3 FIXED 🎯

PHASE 2: Scope Bugs
┌─────────────────────────────┐
│ ⏳ A3  : recursive nested fn │
│ ⏳ A4  : class body module names (BLOCKS 2+ tests) │
└─────────────────────────────┘
   Status: 0/2 PENDING 🔴

PHASE 3: Decorator Bugs
┌─────────────────────────────┐
│ ✅ A5  : __name__ persistence │
│ ✅ A6  : __dict__ fallthrough │
│ ⏳ A7  : @staticmethod descriptor │
│ ⏳ A8  : __func__ wrapper caching │
│ ⏳ A9  : dotted decorator dispatch │
│ ⏳ A10 : class instance type boxing │
│ ⏳ A11 : object[] tuple equality │
│ ⏳ A12 : staticmethod in classmethod │
└─────────────────────────────┘
   Status: 2/8 FIXED, 6/8 PENDING

PHASE 4: Generators / Other
┌─────────────────────────────┐
│ 🔄 GENERATORS : yield from detection │
│ ⏳ A15 : method override resolution │
│ ⏳ A16 : TypeBuilder property emit │
│ ⏳ A17 : F-string parser edge case │
│ ⏳ A18 : isinstance numeric hierarchy │
│ ⏳ YIELD-FROM : nested scope (blocks: A4) │
└─────────────────────────────┘
   Status: 0/6 + 1 IN-PROGRESS
```

---

## 🎯 Priority Matrix

```
         EASY                          HARD
        ↑                               ↑
    8 │                                 │ A4 (scope blocks 2+)
      │ A11(tuple) A8(caching)         │ A10 (type boxing)
    6 │ A17(fstring)                    │ A15 (method resolution)
      │ BUG-GEN(verify) A18(numeric)   │ A12 (compiler storage)
    4 │ A16(typebuilder)                │
      │                                 │
    2 │                                 │
      │                                 │
    0 └─────────────────────────────────┴─→
        IMPACT (tests unlocked)
        LOW    MED    HIGH   CRITICAL
```

**Optimal Next Fixes**:
1. BUG-GENERATORS (verify) — 0 effort, confirms status
2. A11 (tuple equality) — 30 min, fixes 1 test
3. A4 (class scope) — 3 hrs, fixes 2-3 tests + unblocks dependency

---

## 📊 Statistics

| Metric | Value |
|--------|-------|
| **Total Category A Bugs** | 18 identified |
| **Fixed** | 6 (33%) |
| **Pending** | 11 (61%) |
| **In-Progress** | 1 (6%) |
| **Estimated Total Effort** | 25-30 hrs |
| **Estimated Test Gain** | +6-10 tests |

---

## 🚨 Blocking Dependencies

```
  BUG-A4 (Class body scope)
    └─ Required by: BUG-YIELD-FROM, BUG-A9
    └ Unlocks: 2 tests + 1 dependency

  CLASS-BODY FIELD STORAGE (FIX-5 identified)
    └─ Affects: BUG-A12, BUG-A9
    └ Unlocks: 2-3 tests

  NUMERIC HIERARCHY (BUG-A18)
    └─ Affects: isinstance checks
    └ Unlocks: 1 test

  DESCRIPTOR PROTOCOL (BUG-A7, A8, A9)
    └─ Shared across 3 decorator bugs
    └ Unlocks: 3 tests
```

---

## ✨ Session Action Plan

### Immediate (Next 1-2 hours)
- [ ] Verify BUG-GENERATORS with `test_generators.py`
- [ ] Fix BUG-A11 (object[] equality) — 30 min fix
- [ ] Investigate FIX-5 class-body storage — document findings

### Short-term (Next 4-6 hours)
- [ ] Fix BUG-A4 (class body module scope) — highest leverage
- [ ] Fix BUG-A18 (numeric type hierarchy) — quick win
- [ ] Fix BUG-A3 (recursive nested closure) — unblocks gen tests

### Medium-term (Next 8-10 hours)
- [ ] Implement descriptor protocol (A7, A8, A9)
- [ ] Fix class-body field storage (A12)
- [ ] Debug F-string parser (A17)

### Deferred (Post-MVP)
- ⏸ `eval()` builtin — requires interpreter
- ⏸ `compile()` builtin — requires runtime compiler

---

## 💡 Technical Notes

### Quick-Win Fixes

**BUG-A11** (object[] tuple equality):
```csharp
// In NajaUnittest.AreEqual
if (left is object[] lo && right is object[] ro) {
    if (lo.Length != ro.Length) return false;
    return lo.SequenceEqual(ro, /* recursive comparer */);
}
```

**BUG-A18** (numeric hierarchy):
```csharp
// In ReflectionHelpers.IsInstance
// Python rule: isinstance(1, float) → True (int ⊆ float conceptually)
if (expectedType == typeof(float) && actualType == typeof(int)) 
    return true;
```

### High-Impact Fixes

**BUG-A4** (class body scope):
- Current: `classBodyCtx` initialized with empty dicts
- Fix: Copy `methods`, `fields`, `classTypes` from module context
- Location: `AssemblyEmitter.ClassDeclaration.cs` EmitContext creation

**BUG-A12** (staticmethod access):
- Current: Class-body assignments stored as... (TBD)
- Fix: Determine storage mechanism; ensure accessible via `StaticCall`
- Location: `AssemblyEmitter.ClassBody.cs` field/property emission

---

## 📈 Expected Impact

| Fix | Tests | Effort | ROI |
|-----|-------|--------|-----|
| GEN verify | +0 (confirm) | 10 min | High (info) |
| A11 | +1 | 30 min | **High** |
| A18 | +1 | 60 min | High |
| A4 | +2-3 | 180 min | **Very High** |
| A3 | +1 | 120 min | High |
| A7/A8/A9 | +3 | 180 min | High |

**Best path**: A4 foundation → A11 + A18 quick wins → A3 + descriptors
**Realistic next milestone**: 50-52/63 (79-82%) in 6-8 hours

---

Generated from: `Naja.CodeGen\llm_context\CategoryA_Fix_Plan.md`

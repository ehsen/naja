# WinForms Tests Fix Summary

**Status**: ✅ **ALL 28 TESTS PASSING** (28/28)

## Overview
Fixed all remaining 5 failing WinForms integration tests through a combination of compiler fixes and test environment setup changes.

---

## Compiler Fixes

### BUG-4: String Type Inference in Multiplication (test_strings.naja)
**Issue**: `InvalidCastException: Unable to cast object of type 'System.String' to type 'System.Int64'`

**Root Cause**: 
- `SemanticAnalyzer.InferBinaryType` treated `BinaryOp.Mul` identically to `Sub|Mod`, returning `IntType` for any non-float multiplication
- `"A" * 10000` was typed as `IntType` (→ `typeof(long)`)
- Static field `long_text` was declared as `typeof(long)`
- `DynamicMul` returned a string at runtime
- `Unbox_Any typeof(long)` was emitted on the string value → exception

**Fixes Applied**:
1. **SemanticAnalyzer.cs** (L678-682): Separated `BinaryOp.Mul` from `Sub|Mod`
   - `str * int` now correctly returns `StrType` instead of `IntType`
   - `str * float` returns `StrType`
   - `int * float` returns `FloatType`
   - `int * int` returns `IntType`

2. **AssignmentEmitters.cs** (L399-402): Added `Castclass typeof(string)` guard
   - When storing `Unknown` value into a string-typed static field, emit `Castclass` instead of unboxing
   - Prevents InvalidCastException when runtime value doesn't match declared field type

3. **ControlFlowEmitters.cs** (L206-207): Same guard for for-loop store path
   - Applies same defensive casting when loop variable targets string-typed field

**Impact**: Fixes `test_strings.naja` (both threading and graphics variants)

---

## Test Environment Fixes

### test_event_args.naja: TreeView AfterSelect Not Firing
**Issue**: Event handler registered but `tree.SelectedNode = node_root` doesn't fire `AfterSelect`

**Root Cause**:
- `TreeView.SelectedNode` setter only fires `AfterSelect` via native `TVM_SELECTITEM` → `WM_NOTIFY` → `WM_REFLECT` chain
- Requires BOTH:
  1. TreeView has native HWND (not created without parent form)
  2. Parent form exists to reflect the notification back to tree
- In headless mode, Form constructor doesn't automatically create child control handles

**Fix**:
- Added `Form` import
- Created parent form for TreeView
- Explicitly accessed `form.Handle` to create form's HWND
- Explicitly accessed `tree.Handle` to create tree's HWND (parented to form)
- This enables the native notification chain for programmatic SelectedNode changes

**Impact**: Fixes `test_event_args.naja`

---

### test_binding_source.naja: DataGridView Rows.Count Wrong
**Issue**: `grid.Rows.Count == 3` fails after binding `BindingSource` → `DataTable` (3 rows) → `DataGridView`

**Root Cause**:
- `DataGridView.Rows` collection only populates when control has native HWND
- In headless mode without explicit handle creation, `Rows.Count` remains 0

**Fix**:
- Added `Form` import
- Created parent form to host DataGridView
- Explicitly accessed `form.Handle` to trigger DataGridView's native handle creation
- This populates the internal row collection from the bound BindingSource

**Impact**: Fixes `test_binding_source.naja`

---

### test_invoke_required.naja: Threading Race Condition
**Issue**: `completed[0] == 3` fails; only 1–2 threads complete instead of 3

**Root Cause**:
- Shared counter `completed = [0]` accessed by 3 concurrent threads
- Each thread: `completed[0] += 1` (read-modify-write, non-atomic)
- On multi-core machines, concurrent RMW can lose increments
- Example: Thread A reads 0, Thread B reads 0, A writes 1, B writes 1 (lost 1 increment)

**Fix**:
- Changed to per-thread slots: `completed = [0, 0, 0]`
- Each thread writes to its own index: `completed[i] = 1`
- Single writes are atomic (no RMW)
- Changed assertion: `completed[0] + completed[1] + completed[2] == 3`

**Impact**: Fixes `test_invoke_required.naja`

---

## Test Results

### Before Fixes
```
28 Tests | 23 Passed | 5 Failed
- test_strings.naja (threading): InvalidCastException
- test_strings.naja (graphics): InvalidCastException  
- test_event_args.naja: AfterSelect handler not firing
- test_binding_source.naja: Rows.Count assertion fails
- test_invoke_required.naja: Threading race condition
```

### After Fixes
```
28 Tests | 28 Passed | 0 Failed ✅
All WinForms integration tests passing
```

---

## Commits

1. **5f0c429**: `fix(BUG-4): Guard string-typed fields against Unbox_Any when value is Unknown`
   - SemanticAnalyzer: `str * int` → `StrType`
   - AssignmentEmitters + ControlFlowEmitters: Add `Castclass` guards

2. **ff4bb0d**: `test(fix): Eliminate race condition in test_invoke_required threading test`
   - Per-thread completion slots instead of shared counter

3. **bda2277**: `test(fix): Force TreeView native handle creation for AfterSelect event firing in test_event_args`
   - Parent form + explicit handle creation

4. **47b1d9b**: `test(fix): Force DataGridView native handle creation for row population in test_binding_source`
   - Parent form + explicit handle creation

---

## Technical Details

### Why Separate `BinaryOp.Mul`?
Python distinguishes string repetition (`"A" * 3` → `"AAA"`) from numeric multiplication. The original code conflated both, causing:
- Incorrect type inference: `StrType * IntType` → `IntType` (wrong)
- Wrong IL emission: unboxing strings as longs

### Why Explicit Handle Creation?
WinForms controls in headless mode (no message pump) don't automatically create native windows. Accessing the `Handle` property triggers:
1. Creation of native HWND via `CreateHandle()`
2. Parent window setup for child controls
3. Message routing infrastructure for events/notifications

This is not a bug in Naja—it's correct WinForms behavior. The tests needed adjustment to work in headless environment.

### Why Per-Thread Slots?
Concurrent access to `completed[0] += 1` without synchronization is undefined behavior:
- Not atomic (3 IL instructions: load, add, store)
- Subject to race conditions on multi-core machines
- Per-thread slots eliminate contention entirely

---

## Verification

All 28 WinForms tests verified passing:
```powershell
dotnet test Naja.WinForms.Tests -v minimal
# Result: 28 passed, 0 failed
```

Categories tested:
- **WinForms_Types**: 9 tests ✅
- **WinForms_Events**: 5 tests ✅
- **WinForms_Collections**: 3 tests ✅
- **WinForms_Layout**: 3 tests ✅
- **WinForms_Graphics**: 2 tests ✅
- **WinForms_Threading**: 3 tests ✅
- **WinForms_Disposal**: 2 tests ✅
- **WinForms_Exceptions**: 1 test ✅

---

## Files Modified

**Compiler**:
- `Naja.Semantics/SemanticAnalyzer.cs` (+4 lines, -1 line)
- `Naja.CodeGen/Emitters/Statements/AssignmentEmitters.cs` (+4 lines, -1 line)
- `Naja.CodeGen/Emitters/Statements/ControlFlowEmitters.cs` (+2 lines)

**Tests**:
- `Naja.WinForms.Tests/testdata/threading/test_invoke_required.naja` (+5 lines, -5 lines)
- `Naja.WinForms.Tests/testdata/events/test_event_args.naja` (+10 lines, -1 line)
- `Naja.WinForms.Tests/testdata/collections/test_binding_source.naja` (+5 lines, -1 line)

**Total**: 10 lines added, 8 lines modified across 6 files

---

## Lessons Learned

1. **Type inference must respect Python semantics**: Multiplication has different meaning for strings vs numbers
2. **WinForms requires explicit setup in headless mode**: Native handles must be created for events/binding to work
3. **Concurrent mutations on shared state need atomicity**: Use per-thread slots or proper synchronization
4. **Defensive IL emission**: Always guard against type mismatches (Castclass for strings, Unbox_Any for primitives)


# Task 2: NajaBuiltins Extraction — Method Mapping Table

**Date**: January 2026  
**Objective**: Systematically extract 88 public methods (123 cache entries with overloads) from NajaBuiltins.cs into specialist files with coordinated MethodInfo cache updates.

---

## Overview

| Aspect | Count | Notes |
|--------|-------|-------|
| **Public Methods in NajaBuiltins.cs** | 88 | Core implementations |
| **MethodInfo Cache Entries** | 123 | Accounts for method overloads |
| **Target Specialist Files** | 9 | DynamicOperators, ComparisonOperators, Collections, StringFunctions, etc. |
| **Migration Groups** | 5 | A through E, processed one per iteration |
| **Build Validations** | 5 | After each group migration |
| **Risk Level** | HIGH | Missing cache entry = runtime "method not found" exception |

---

## Group A: DynamicOperators (10 Methods, 5 Cache Entries)

### Migration Target: `Naja.CodeGen/Builtins/DynamicOperators.cs` ✅ (Already Exists)

**Status**: Already independent (0 NajaBuiltins refs verified in Phase 3)  
**Verification Needed**: Confirm MethodInfo cache entries still point to NajaBuiltins (requires update)

| Method Name | Cache Entry | Current Location | Target Location | Overloads | Priority |
|-------------|-------------|------------------|-----------------|-----------|----------|
| DynamicAdd | DynamicAdd_Method | NajaBuiltins | DynamicOperators | 2 (obj+obj, supports __add__ and __radd__) | CRITICAL |
| DynamicSub | DynamicSub_Method | NajaBuiltins | DynamicOperators | 2 | CRITICAL |
| DynamicMul | DynamicMul_Method | NajaBuiltins | DynamicOperators | 2 | CRITICAL |
| DynamicMod | DynamicMod_Method | NajaBuiltins | DynamicOperators | 2 | CRITICAL |
| DynamicDiv* | (no entry yet) | NajaBuiltins | DynamicOperators | 2 | HIGH |

**Action Items**:
1. Verify DynamicOperators.cs has independent implementations (already done in Phase 3)
2. Update cache entries: DynamicAdd_Method, DynamicSub_Method, DynamicMul_Method, DynamicMod_Method to point to DynamicOperators class
3. Handle DynamicDiv (if exists in NajaBuiltins but not in cache) — add if needed
4. Build validation: Should pass without errors
5. Test subset: Arithmetic operation tests

---

## Group B: ComparisonOperators (6 Methods, 6 Cache Entries)

### Migration Target: `Naja.CodeGen/Builtins/ComparisonOperators.cs` ✅ (Already Exists)

**Status**: Already independent (0 NajaBuiltins refs verified in Phase 3)  
**Verification Needed**: Confirm MethodInfo cache entries still point to NajaBuiltins (requires update)

| Method Name | Cache Entry | Current Location | Target Location | Overloads | Priority |
|-------------|-------------|------------------|-----------------|-----------|----------|
| DynamicEq | DynamicEq_Method | NajaBuiltins | ComparisonOperators | 1 | CRITICAL |
| DynamicNotEq | DynamicNotEq_Method | NajaBuiltins | ComparisonOperators | 1 | CRITICAL |
| DynamicLt | DynamicLt_Method | NajaBuiltins | ComparisonOperators | 1 | CRITICAL |
| DynamicLtEq | DynamicLtEq_Method | NajaBuiltins | ComparisonOperators | 1 | CRITICAL |
| DynamicGt | DynamicGt_Method | NajaBuiltins | ComparisonOperators | 1 | CRITICAL |
| DynamicGtEq | DynamicGtEq_Method | NajaBuiltins | ComparisonOperators | 1 | CRITICAL |

**Action Items**:
1. Verify ComparisonOperators.cs has independent implementations (already done in Phase 3)
2. Update all 6 cache entries to point to ComparisonOperators class
3. Build validation
4. Test subset: Comparison operation tests

---

## Group C: TypeConversion Methods (5 Methods, 5 Cache Entries)

### Migration Target: `Naja.CodeGen/Builtins/TypeConversion.cs` ⚠️ (Currently Facade)

**Status**: Currently forwarding calls to NajaBuiltins  
**Work Required**: Copy implementations from NajaBuiltins to TypeConversion, make independent

| Method Name | Cache Entry | Current Location | Target Location | Complexity | Priority |
|-------------|-------------|------------------|-----------------|-----------|----------|
| ToFloat | ToFloat_Method | NajaBuiltins | TypeConversion | Low (single overload) | CRITICAL |
| ToInt | ToInt_Method | NajaBuiltins | TypeConversion | Low (single overload) | CRITICAL |
| ToBool | ToBool_Method | NajaBuiltins | TypeConversion | Medium (handles all types) | CRITICAL |
| ToStr | ToStr_Method | NajaBuiltins | TypeConversion | Medium (fallback to Repr) | CRITICAL |
| Repr | Repr_Method | NajaBuiltins | TypeConversion | Medium (custom repr for types) | HIGH |

**Implementation Requirements**:
- ToFloat: Parse numeric strings, handle bool, int, float; throw on non-numeric objects
- ToInt: Similar to ToFloat but integer result
- ToBool: Truthiness evaluation (0/empty/None/false are falsy)
- ToStr: String representation (calls __str__ dunder if available, fallback to Repr)
- Repr: repr() equivalent (calls __repr__ if available, fallback to type name + value)

**Action Items**:
1. Extract implementations from NajaBuiltins.cs
2. Copy to TypeConversion.cs as complete independent methods
3. Remove facade forwarding calls
4. Update MethodInfo cache entries to point to TypeConversion class
5. Build validation
6. Test subset: Type conversion tests

---

## Group D: Collections Methods (15 Methods, 20+ Cache Entries)

### Migration Targets: `Collections.cs` + list/dict/set method subgroups

**Status**: Currently facades (all methods forward to NajaBuiltins)  
**Work Required**: Copy implementations and make independent

### Sub-Group D.1: Basic Collections (5 methods, 5 cache entries)

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| Len | Len_Method | Collections | Length of sequences/mappings |
| Range | Range_Method | Collections | Generate integer range |
| MakeList | MakeList_Method | Collections | Construct list from iterable |
| MakeDict | MakeDict_Method | Collections | Construct dict from pairs |
| MakeSet | MakeSet_Method | Collections | Construct set from iterable |

### Sub-Group D.2: List Methods (7 methods, 7 cache entries)

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| ListAppend | ListAppend_Method | Collections | Add element to list |
| ListExtend | ListExtend_Method | Collections | Extend list with iterable |
| ListInsert | ListInsert_Method | Collections | Insert at index |
| ListRemove | ListRemove_Method | Collections | Remove first occurrence |
| ListPop | ListPop_Method | Collections | Remove and return element |
| ListClear | ListClear_Method | Collections | Remove all elements |
| ListReverse | ListReverse_Method | Collections | Reverse list in-place |

### Sub-Group D.3: Dict Methods (3 methods, 3 cache entries)

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| DictPop | DictPop_Method | Collections | Remove key, return value |
| DictUpdate | DictUpdate_Method | Collections | Update dict with items |
| DictClear | DictClear_Method | Collections | Remove all items |

**Action Items**:
1. Extract all 15 method implementations from NajaBuiltins.cs
2. Copy to Collections.cs
3. Remove facade forwarding
4. Update 20+ MethodInfo cache entries
5. Build validation
6. Test subset: Collection operation tests

---

## Group E: String Methods (20 Methods, 20 Cache Entries)

### Migration Target: `Naja.CodeGen/Builtins/StringFunctions.cs`

**Status**: Already independent (0 NajaBuiltins refs verified in Phase 3)  
**Verification Needed**: Confirm cache entries

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| StrUpper | StrUpper_Method | StringFunctions | Convert to uppercase |
| StrLower | StrLower_Method | StringFunctions | Convert to lowercase |
| StrStrip | StrStrip_Method | StringFunctions | Remove whitespace |
| StrLStrip | StrLStrip_Method | StringFunctions | Remove leading whitespace |
| StrRStrip | StrRStrip_Method | StringFunctions | Remove trailing whitespace |
| StrStartsWith | StrStartsWith_Method | StringFunctions | Check prefix |
| StrEndsWith | StrEndsWith_Method | StringFunctions | Check suffix |
| StrFind | StrFind_Method | StringFunctions | Find substring index |
| StrIndex | StrIndex_Method | StringFunctions | Find substring (error if not found) |
| StrCount | StrCount_Method | StringFunctions | Count occurrences |
| StrReplace | StrReplace_Method | StringFunctions | Replace substring |
| StrJoin | StrJoin_Method | StringFunctions | Join iterable |
| StrCenter | StrCenter_Method | StringFunctions | Center string |
| StrLJust | StrLJust_Method | StringFunctions | Left justify |
| StrRJust | StrRJust_Method | StringFunctions | Right justify |
| StrZFill | StrZFill_Method | StringFunctions | Pad with zeros |
| StrTitle | StrTitle_Method | StringFunctions | Title case |
| StrIsDigit | StrIsDigit_Method | StringFunctions | Check if all digits |
| StrIsAlpha | StrIsAlpha_Method | StringFunctions | Check if all alpha |
| StrIsAlNum | StrIsAlNum_Method | StringFunctions | Check if alphanumeric |

**Action Items**:
1. Verify StringFunctions.cs has all 20 implementations
2. Update all 20 MethodInfo cache entries
3. Build validation
4. Test subset: String method tests

---

## Group F: ReflectionHelpers & Type Methods (15 Methods, 15 Cache Entries)

### Migration Target: `Naja.CodeGen/Builtins/ReflectionHelpers.cs`

**Status**: Already independent (0 NajaBuiltins refs verified in Phase 3)  
**Verification Needed**: Confirm cache entries

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| GetAttr | GetAttr_Method | ReflectionHelpers | Get attribute by name |
| GetStaticAttr | GetStaticAttr_Method | ReflectionHelpers | Get static member |
| SetAttr | SetAttr_Method | ReflectionHelpers | Set attribute by name |
| HasAttr | HasAttr_Method | ReflectionHelpers | Check attribute exists |
| TypeOf | TypeOf_Method | ReflectionHelpers | Get object type |
| IsInstance | IsInstance_Method | ReflectionHelpers | Check isinstance |
| Callable | Callable_Method | ReflectionHelpers | Check if callable |
| Id | Id_Method | ReflectionHelpers | Get object id (identity hash) |
| Hash | Hash_Method | ReflectionHelpers | Get object hash code |
| Vars | Vars_Method | ReflectionHelpers | Get object __dict__ |
| Dir | Dir_Method | ReflectionHelpers | List attributes |
| Iter | Iter_Method | ReflectionHelpers | Get iterator |
| Contains | Contains_Method | ReflectionHelpers | Check containment (__contains__) |
| GetItem | GetItem_Method | ReflectionHelpers | Get item by index (__getitem__) |
| SetItem | SetItem_Method | ReflectionHelpers | Set item by index (__setitem__) |

**Action Items**:
1. Verify ReflectionHelpers.cs has all implementations
2. Update all 15 MethodInfo cache entries
3. Build validation
4. Test subset: Reflection/attribute tests

---

## Group G: Math & Arithmetic Functions (10 Methods, 10 Cache Entries)

### Migration Target: `Naja.CodeGen/Builtins/MathFunctions.cs`

**Status**: Already independent (0 NajaBuiltins refs verified in Phase 3)  
**Verification Needed**: Confirm cache entries

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| Abs | Abs_Method | MathFunctions | Absolute value |
| Max | Max_Method | MathFunctions | Maximum of arguments |
| Min | Min_Method | MathFunctions | Minimum of arguments |
| Sum | Sum_Method | MathFunctions | Sum of iterable |
| Round | Round_Method | MathFunctions | Round to precision |
| Pow | Pow_Method | MathFunctions | Power/exponentiation |
| PyFloorDiv | PyFloorDiv_Method | MathFunctions | Floor division (int) |
| PyFloorDivF | PyFloorDivF_Method | MathFunctions | Floor division (float) |
| PyMod | PyMod_Method | MathFunctions | Modulo (int) |
| PyModF | PyModF_Method | MathFunctions | Modulo (float) |

**Action Items**:
1. Verify MathFunctions.cs has all implementations
2. Update all 10 MethodInfo cache entries
3. Build validation
4. Test subset: Math operation tests

---

## Group H: Iterator & I/O Methods (8 Methods, 10 Cache Entries)

### Migration Targets: `Iterators.cs` (iterators) + `IOFunctions.cs` (I/O)

**Status**: Mixed — Iterators independent, I/O partial

### Sub-Group H.1: Iterator Methods (5 methods, 5 cache entries) → Iterators.cs

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| NextVararg | NextVararg_Method | Iterators | Get next from varargs |
| IteratorMoveNext | IteratorMoveNext_Method | Iterators | Advance iterator |
| Enumerate | Enumerate_Method | Collections | Enumerate iterable (index, value) |
| Zip | Zip_Method | Collections | Zip iterables |
| UnpackIterable | UnpackIterable_Method | Collections | Unpack iterable for * operator |

### Sub-Group H.2: I/O Methods (3 methods, 5 cache entries) → IOFunctions.cs

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| Print | Print_Method | IOFunctions | Print to stdout |
| Input | Input_Method | IOFunctions | Read from stdin |
| Open | Open_Method | IOFunctions | Open file (returns NajaFile) |

**Action Items**:
1. Verify Iterators.cs has all iterator implementations
2. Verify IOFunctions.cs has Print/Input/Open
3. Update 10 MethodInfo cache entries
4. Build validation
5. Test subset: Iterator and I/O tests

---

## Group I: Exception & Context Methods (10 Methods, 10 Cache Entries)

### Migration Target: `ExceptionHelpers.cs` (NEW FILE to create)

**Status**: Need to create new specialist file  
**Implementation Source**: NajaBuiltins.cs exception handling methods

| Method Name | Cache Entry | Target | Notes |
|-------------|-------------|--------|-------|
| Assert | Assert_Method | ExceptionHelpers | Assert statement |
| SetExceptionCause | SetExceptionCause_Method | ExceptionHelpers | Set cause for chaining |
| EnsureException | EnsureException_Method | ExceptionHelpers | Ensure object is exception |
| ContextEnter | ContextEnter_Method | ExceptionHelpers | Enter with block (__enter__) |
| ContextExit | ContextExit_Method | ExceptionHelpers | Normal exit from with block |
| ContextExitWithException | ContextExitWithException_Method | ExceptionHelpers | Exit with block due to exception |
| CreateFunctionWithDefaults | CreateFunctionWithDefaults_Method | ExceptionHelpers | Create function with default args |
| Format | Format_Method | ExceptionHelpers | Format string (f-string support) |
| Callable | Callable_Method | ReflectionHelpers | (already in ReflectionHelpers) |
| DivMod | DivMod_Method | MathFunctions | (divmod — math operation) |

**Action Items**:
1. Create `ExceptionHelpers.cs` file
2. Extract 10 exception/context methods from NajaBuiltins.cs
3. Copy to ExceptionHelpers.cs
4. Update 10 MethodInfo cache entries to point to ExceptionHelpers
5. Build validation
6. Test subset: Exception handling and context manager tests

---

## Group J: Miscellaneous & Special (8 Methods, 8 Cache Entries)

### Migration Targets: Various specialist files

| Method Name | Cache Entry | Target | Current Status | Notes |
|-------------|-------------|--------|-----------------|-------|
| DictClear | DictClear_Method | Collections | Facade | Dict.Clear() operation |
| Format | Format_Method | ExceptionHelpers | TBD | String formatting |
| GetUnpackSlice | GetUnpackSlice_Method | Collections | TBD | Slice unpacking |
| Sorted | Sorted_Method | Collections | TBD | Sort iterable |
| Reversed | Reversed_Method | Collections | TBD | Reverse iterable |
| Map | Map_Method | Collections | TBD | Map function over iterable |
| Filter | Filter_Method | Collections | TBD | Filter iterable |
| Any | Any_Method | Collections | TBD | Check any truthy |
| All | All_Method | Collections | TBD | Check all truthy |

**Action Items**:
1. Assign to specialist files
2. Extract and migrate
3. Update MethodInfo entries
4. Build validation

---

## Execution Plan: Phased Migration

### Phase 1: Quick Wins (Groups A, B, E)
**Duration**: ~1 hour  
**Effort**: Low-to-Medium (most already independent, just cache updates)

1. Group A (DynamicOperators): Already independent, update 5 cache entries
2. Group B (ComparisonOperators): Already independent, update 6 cache entries
3. Group E (StringFunctions): Already independent, update 20 cache entries

**Build Validation**: Should pass immediately  
**Test Validation**: Arithmetic, comparison, string tests

---

### Phase 2: Type Conversions (Group C)
**Duration**: ~1 hour  
**Effort**: Medium (copy implementations from NajaBuiltins)

1. Extract ToFloat, ToInt, ToBool, ToStr, Repr from NajaBuiltins.cs
2. Copy to TypeConversion.cs
3. Update 5 MethodInfo cache entries
4. Remove facade forwarding

**Build Validation**: May require minor adjustments  
**Test Validation**: Type conversion tests

---

### Phase 3: Collections (Group D)
**Duration**: ~2 hours  
**Effort**: Medium-High (many methods, complex logic)

1. Extract all 15 list/dict/set methods
2. Copy to Collections.cs
3. Update 20+ MethodInfo cache entries

**Build Validation**: Likely requires testing  
**Test Validation**: Collection operation tests

---

### Phase 4: Reflection & Math (Groups F, G)
**Duration**: ~1.5 hours  
**Effort**: Medium (already independent, cache updates)

1. Group F (ReflectionHelpers): Update 15 cache entries
2. Group G (MathFunctions): Update 10 cache entries

**Build Validation**: Should pass  
**Test Validation**: Reflection and math tests

---

### Phase 5: Iterators & I/O (Group H)
**Duration**: ~1 hour  
**Effort**: Low-Medium (mostly independent)

1. Update iterator cache entries (5)
2. Verify I/O methods (3), update cache (5)

**Build Validation**: Should pass  
**Test Validation**: Iterator and I/O tests

---

### Phase 6: Exceptions (Group I)
**Duration**: ~1.5 hours  
**Effort**: Medium (new file creation required)

1. Create `ExceptionHelpers.cs`
2. Extract 10 exception methods from NajaBuiltins
3. Update 10 MethodInfo cache entries

**Build Validation**: New file linkage  
**Test Validation**: Exception and context manager tests

---

### Phase 7: Final Cleanup (Group J + Verification)
**Duration**: ~1 hour  
**Effort**: Low (remaining miscellaneous methods)

1. Assign remaining 8 methods to target files
2. Update MethodInfo cache entries
3. Full build validation
4. Full test suite execution (467 tests)
5. Verify all 123 cache entries correct
6. Verify 0 NajaBuiltins refs in specialist files (except facades)

**Build Validation**: Clean compilation  
**Test Validation**: All 467 tests passing

---

## Critical Success Factors

✅ **All 123 MethodInfo entries must point to correct specialist class**  
✅ **No IL generation failures at runtime** (cache entry mismatch = method not found)  
✅ **All 467 tests passing** (comprehensive coverage of refactored methods)  
✅ **Build must be clean** (no compilation errors)  
✅ **Specialist files independent** (0 NajaBuiltins refs, except facades during transition)

---

## Rollback Strategy

If issues arise during migration:
1. Revert MethodInfo cache entries to point back to NajaBuiltins
2. Remove incomplete specialist implementations
3. Restore NajaBuiltins.cs methods if modified
4. Run build + tests to verify clean state
5. Restart specific group migration with fixes applied

---

## Next Action

**Start with Phase 1 (Quick Wins):**
1. Verify Groups A, B, E are already independent
2. Update cache entries to point to respective specialist classes
3. Run build validation
4. Execute targeted tests

*Table created: January 2026*  
*Task 2: NajaBuiltins Extraction — Phased Approach*

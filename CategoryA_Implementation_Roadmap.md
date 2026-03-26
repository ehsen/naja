# Category A Fixes — Implementation Roadmap

## Overview
- **Current**: 47/63 tests (74.6%)
- **Target**: 52-54/63 tests (82-85%)
- **Estimated Effort**: 8-12 hours across 3 phases
- **High-confidence path**: Phase 1 + Phase 2 → 50-52/63 (79-82%)

---

## Phase 1: Foundation & Verification (2-3 hours)

### 1.1 Verify BUG-GENERATORS Status [10 min]
**File**: `Naja.CodeGen\Emitters\Statements\DefinitionEmitters.cs`

```csharp
// Verify that YieldExpr detection handles "yield from"
case YieldExpr ye when ye.IsFrom => true; // Should detect yield from
```

**Action**: 
- Run: `test_generators.py`
- Expected: Should pass (already implemented)
- If fails: Debug YieldExpr detection logic

**Impact**: Confirms baseline or identifies quick fix

---

### 1.2 Fix BUG-A11: object[] Tuple Equality [30 min]
**File**: `Naja.StdLib\NajaUnittest.cs` — `AreEqual` method

**Current State**:
```csharp
public static bool AreEqual(object? a, object? b)
{
    if (a is Dictionary<object, object> da && b is Dictionary<object, object> db)
        return da.Count == db.Count && da.All(kv => db.TryGetValue(kv.Key, out var v) && AreEqual(kv.Value, v));
    
    if (a is List<object> la && b is List<object> lb)
        return la.Count == lb.Count && la.SequenceEqual(lb, EqualityComparer<object>.Default);
    
    // Missing: object[] comparison
    return Equals(a, b);
}
```

**Fix**:
```csharp
if (a is object[] oa && b is object[] ob) {
    if (oa.Length != ob.Length) return false;
    for (int i = 0; i < oa.Length; i++) {
        if (!AreEqual(oa[i], ob[i])) return false;
    }
    return true;
}
```

**Location**: After List comparison, before final `Equals(a, b)`

**Tests Fixed**: `test_decorators · test_argforms`

---

### 1.3 Fix BUG-A18: Python Numeric Type Hierarchy [60 min]
**File**: `Naja.CodeGen\Builtins\ReflectionHelpers.cs` — `IsInstance` method

**Issue**: `isinstance(1, float)` returns `False` but Python returns `True`
- Python rule: `int` is numerically compatible with `float` in type hierarchy
- CLR rule: `Int32` and `Single` are distinct types

**Current Logic**:
```csharp
public static bool IsInstance(object obj, object typeOrTuple)
{
    var actualType = obj.GetType();
    // ... other checks ...
    return expectedType.IsAssignableFrom(actualType);  // Direct type check
}
```

**Fix**:
```csharp
// Add Python numeric hierarchy support
private static bool IsInstanceNumericHierarchy(Type actualType, Type expectedType)
{
    // Python: isinstance(1, float) → True, isinstance(1.0, int) → False
    if (expectedType == typeof(double) || expectedType == typeof(float)) {
        return actualType == typeof(int) || actualType == typeof(long) || 
               actualType == typeof(double) || actualType == typeof(float) ||
               actualType == typeof(decimal);
    }
    if (expectedType == typeof(bool)) {
        return actualType == typeof(bool);
    }
    return false;
}
```

**Insert into IsInstance**:
```csharp
// Before: return expectedType.IsAssignableFrom(actualType);
if (IsInstanceNumericHierarchy(actualType, expectedType)) return true;
return expectedType.IsAssignableFrom(actualType);
```

**Tests Fixed**: `test_unary · test_negative`

---

## Phase 2: High-Impact Scope Fix (3-4 hours)

### 2.1 Fix BUG-A4: Class Body Module-Level Scope [3-4 hours]
**File**: `Naja.CodeGen\AssemblyEmitter.ClassDeclaration.cs`

**Problem Statement**:
- Class bodies can't resolve module-level names (fields, methods, types, etc.)
- Example: `class C: base = E` fails with "Undefined name 'E'" if `E` is a module-level class

**Root Cause**:
```csharp
// Current (broken)
private void EmitClassBody(ClassDef cls, TypeBuilder ct, ...)
{
    var ctx = new EmitContext(
        il: classIL,
        Fields: new Dictionary<string, FieldBuilder>(),        // ❌ Empty!
        Methods: new Dictionary<string, MethodBuilder>(),      // ❌ Empty!
        ClassTypes: new Dictionary<string, TypeBuilder>(),     // ❌ Empty!
        ...
    );
    // Class body tries to resolve 'E' but dict is empty
}
```

**Solution**:
```csharp
private void EmitClassBody(
    ClassDef cls, 
    TypeBuilder ct, 
    ModuleBuilder modBuilder,
    Dictionary<string, FieldBuilder> moduleFields,      // Pass in module dicts
    Dictionary<string, MethodBuilder> moduleMethods,
    Dictionary<string, Type[]> paramTypes,
    Dictionary<string, TypeBuilder> classTypes,        // Module-level classes
    Dictionary<string, ConstructorBuilder> classCtors,
    ...)
{
    // Create class body context WITH module-scope dictionaries
    var ctx = new EmitContext(
        il: classIL,
        Fields: moduleFields,                           // ✅ Include module fields
        Methods: moduleMethods,                         // ✅ Include module methods
        ClassTypes: classTypes,                         // ✅ Include module class types
        ClassConstructors: classCtors,
        ...
    );
    
    // Now class body can resolve module-level names!
}
```

**Implementation Steps**:
1. Locate `EmitClassBody` signature in `AssemblyEmitter.ClassDeclaration.cs`
2. Add parameters for module context dicts (if not already present)
3. Update all call sites in `AssemblyEmitter.ModuleEmission.cs` Pass 3
4. Initialize EmitContext with module dicts, not empty dicts
5. Test with `test_super.py` and `test_compare.py`

**Call Sites to Update**:
- Line ~400 in `ModuleEmission.cs`: `EmitClassBody(cls, ct, modBuilder, fields, methods, paramTypes, classTypes, classCtors, ...)`
- Line ~408 in `ModuleEmission.cs` (nested classes): Similar fix

**Tests Fixed**: 
- `test_super.py`
- `test_compare.py`
- **Unblocks**: `BUG-YIELD-FROM` (nested generator scope)

---

## Phase 3: Protocol & Type System (4-5 hours)

### 3.1 Fix BUG-A3: Recursive Nested Function Closure [2 hours]

**File**: `Naja.Semantics\SemanticAnalyzer.cs` + `Naja.CodeGen\Emitters\Statements\DefinitionEmitters.cs`

**Problem**:
```python
def outer():
    def fact(n):
        return fact(n-1)  # ← 'fact' not registered as cell var
    return fact
```

Error: `Undefined name 'fact'`

**Root Cause**: Closure analysis doesn't treat self-references in nested functions as captured vars.

**Solution**:
1. In `DefinitionEmitters.cs`, `CollectClosureVars`:
```csharp
// When analyzing a nested function that references its own name,
// treat it as captured from enclosing scope
private void CollectClosureVars(FunctionDef fn, HashSet<string> enclosingVars, ...)
{
    // ...existing logic...
    
    // NEW: If this nested function references its own name, add to closure
    if (fn.Name != null && referencedNames.Contains(fn.Name)) {
        enclosingVars.Add(fn.Name);  // Self-reference as cell var
    }
}
```

2. Register function in enclosing scope BEFORE analyzing body:
```csharp
// In EmitFunctionDef: register function name in scope before emitting body
ctx.Methods[fn.Name] = methodBuilder;
// Then emit body (which may reference fn.Name)
```

**Tests Fixed**: `test_scope.py`

---

### 3.2 Fix Descriptor Protocol (BUG-A7, A8, A9) [2-3 hours]

**Three related bugs, one pattern**: Descriptor protocol not properly invoked.

#### BUG-A7: @staticmethod not unwrapped on instance access

**File**: `Naja.CodeGen\Emitters\Expressions\AttributeEmitters.cs`

**Problem**: `obj.foo` where `foo` is `@staticmethod` should unwrap and return function
```python
class C:
    @staticmethod
    def foo(): pass

C().foo()  # Should work — unwrap staticmethod
```

**Fix**:
```csharp
// In AttributeEmitter.EmitAttribute or similar
if (attrValue is NajaStaticMethod sm) {
    return sm.Function;  // Unwrap descriptor
}
```

#### BUG-A8: __func__ wrapper caching

**File**: `Naja.CodeGen\Builtins\NajaStaticMethod.cs` (or relevant descriptor class)

**Problem**: Each access to `func.__func__` creates new wrapper
```python
sm = staticmethod(func)
assert sm.__func__ is func.__func__  # ❌ Fails: different wrappers
```

**Fix**:
```csharp
class NajaStaticMethod : ... {
    private object? _cachedFunc = null;
    
    public object __func__ {
        get {
            if (_cachedFunc == null) {
                _cachedFunc = new NajaFunction(...);
            }
            return _cachedFunc;
        }
    }
}
```

#### BUG-A9: Dotted decorator dispatch

**File**: `Naja.CodeGen\Emitters\Expressions\CallEmitters.cs`

**Problem**: `@MiscDecorators.author` doesn't properly unwrap staticmethod
```python
class MiscDecorators:
    @staticmethod
    def author(fn): return fn

@MiscDecorators.author  # ← Should unwrap staticmethod and apply
def foo(): pass
```

**Fix**: Ensure descriptor protocol applied in decorator call:
```csharp
// When calling descriptor, check and unwrap
if (callable is NajaStaticMethod sm) {
    callable = sm.__func__;
}
```

**Tests Fixed**: 
- `test_decorators · test_single` (A7)
- `test_decorators · test_staticmethod` + `test_classmethod` (A8)
- `test_decorators · test_dotted` + `test_dbcheck` (A9)

---

### 3.3 Fix BUG-A16: TypeBuilder Lifecycle [30 min]

**File**: `Naja.CodeGen\AssemblyEmitter.ClassBody.cs`

**Problem**: `CreateType()` not called before accessing TypeBuilder members for properties

**Fix**:
```csharp
// Before emitting property IL:
if (!typeCreated) {
    classTypeBuilder.CreateType();
    typeCreated = true;
}
// Now emit property IL
```

**Tests Fixed**: `test_property.py`

---

### 3.4 Fix BUG-A17: F-string Parser Edge Case [30 min - 1 hour]

**File**: `Naja.Lexer\Lexer.cs` (F-string tokenization)

**Problem**: `Unterminated string literal at L1025` in complex f-string

**Fix**: Debug specific edge case in f-string (requires examining `test_fstring.py` line 1025)

**Tests Fixed**: `test_fstring.py`

---

### 3.5 Investigate BUG-A12: Class-Body Field Storage [1 hour]

**File**: `Naja.CodeGen\AssemblyEmitter.ClassBody.cs`

**Problem**: `bar = staticmethod(...)` in class body stored differently; not accessible as static field

**Investigation**:
1. How are class-body assignments stored at IL/CLR level?
2. Are they static fields, properties, or something else?
3. Why does `StaticCall` field lookup fail?

**Current Blocker**: FIX-5 identified this but didn't resolve it

**Action**: Document findings and propose solution for next session

---

## Integration Checklist

### Before Implementation
- [ ] All code locations identified and mapped
- [ ] Call sites documented
- [ ] Test cases identified for each fix

### During Implementation
- [ ] Changes committed incrementally (one bug per commit)
- [ ] Compilation verified after each change
- [ ] Related tests run immediately after fix

### After Implementation
- [ ] Run full ScopingTests suite (baseline: 14/14 ✅)
- [ ] Run Phase 1-4 CPython tests
- [ ] Document any regressions
- [ ] Update baseline count

---

## Estimated Timeline

| Phase | Task | Time | Tests | Cumulative |
|-------|------|------|-------|-----------|
| 1 | Verify GEN | 10 min | +0 | 47/63 |
| 1 | Fix A11 (tuple) | 30 min | +1 | 48/63 |
| 1 | Fix A18 (numeric) | 60 min | +1 | 49/63 |
| 2 | Fix A4 (scope) | 3 hrs | +2-3 | 51-52/63 |
| 2 | Fix A3 (recursive) | 2 hrs | +1 | 52-53/63 |
| 3 | Fix descriptors | 2-3 hrs | +3 | 55-56/63 |
| 3 | Fix A16 + A17 | 1 hr | +1-2 | 56-58/63 |

**Quick Path (6-8 hours)**: Phases 1 + 2 → **51-52/63 (81-82%)**
**Full Path (12-14 hours)**: Phases 1 + 2 + 3 → **56-58/63 (89-92%)**

---

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| EmitContext propagation breaks existing tests | Test ScopingTests (14/14) after A4 fix |
| Descriptor protocol affects decorators elsewhere | Run full decorator test suite |
| Numeric hierarchy affects other isinstance calls | Audit all isinstance usage patterns |
| Recursive closure affects other nested functions | Run all closure/lambda tests |

---

## Success Criteria

✅ **Phase 1 Complete**: 49/63 (77.8%)
✅ **Phase 2 Complete**: 52-53/63 (82-84%)
✅ **Phase 3 Complete**: 56-58/63 (89-92%)
🎯 **Final Target**: 58+/63 (92%+)

---

**Status**: Ready for implementation
**Recommendation**: Start with Phase 1 + Phase 2 (scope fix) for maximum efficiency

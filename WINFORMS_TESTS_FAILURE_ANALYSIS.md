# WinForms Tests Failure Analysis

## Summary
**Total Failures: 9 out of 29 WinForms tests**

All failures stem from inadequate type coercion in the Naja runtime when dealing with .NET WinForms types. The core issue is that Naja's type conversion system doesn't properly handle:
1. Delegate wrapping (NajaFunction → ThreadStart, EventHandler, etc.)
2. Complex .NET types (FontFamily, DateOnly, SerializationInfo)
3. Nullable value types
4. Event handler instantiation

---

## Detailed Failure Analysis

### 1. **test_invoke_required.naja** (Threading)
**Error:** `ArgumentException: Object of type 'Naja.CodeGen.NajaFunction' cannot be converted to type 'System.Threading.ThreadStart'`

**Root Cause:** 
- Naja functions are not being automatically wrapped as delegate instances
- When passing a Naja function to `Thread(func)`, the constructor expects a `ThreadStart` delegate
- Missing delegate conversion in TypeSystem or CallEmitters

**Affected Code:**
```python
def background_work():
    invoke_was_required[0] = lbl.InvokeRequired
    ...

t = Thread(background_work)  # <- Fails here: expects ThreadStart, gets NajaFunction
```

**Fix Location:** `Naja.CodeGen/Builtins/TypeSystem.cs` - Add delegate wrapping

---

### 2. **test_nullables.naja** (WinForms Types)
**Error:** `ArgumentException: Object of type 'System.Int64' cannot be converted to type 'System.DateOnly'`

**Root Cause:**
- DateTimePicker.MinDate and MaxDate are `System.DateTime` properties
- Naja is trying to coerce `long` (from internal representation) to `DateOnly`
- Missing DateTime↔DateOnly conversion path

**Affected Code:**
```python
picker.MinDate = System.DateTime(2000, 1, 1)  # <- Fails during type coercion
```

**Fix Location:** `Naja.StdLib/Core/TypeCoercion.cs` - Add DateTime handling

---

### 3. **test_dispose.naja** (Disposal)
**Error:** `ArgumentException: Object of type 'System.String' cannot be converted to type 'System.Runtime.Serialization.SerializationInfo'`

**Root Cause:**
- SerializationInfo requires special construction via SerializationEntry
- Naja is attempting direct string→SerializationInfo conversion
- Missing handling of complex constructor parameter types

**Fix Location:** `Naja.CodeGen/Builtins/TypeSystem.cs` - CreateDotNet method

---

### 4. **test_using_pattern.naja** (Disposal)
**Error:** `TypeLoadException: Cannot instantiate type: null`

**Root Cause:**
- Type resolution failure - likely a type that implements IDisposable but isn't correctly instantiated
- Missing type metadata or null type in reflection chain

**Fix Location:** `Naja.CodeGen/Builtins/TypeSystem.cs` - Type resolution

---

### 5. **test_fonts.naja (Graphics)** & **test_fonts.naja (Threading)**
**Error:** `ArgumentException: Object of type 'System.String' cannot be converted to type 'System.Drawing.FontFamily'`

**Root Cause:**
- FontFamily constructor expects `System.String` overload but conversion isn't matching properly
- Naja isn't using the correct constructor overload (FontFamily has multiple overloads)
- Missing FontFamily(string) constructor detection

**Affected Code:**
```python
font = System.Drawing.Font("Arial", 12)  # <- FontFamily("Arial") conversion fails
```

**Fix Location:** `Naja.CodeGen/Builtins/TypeSystem.cs` - Constructor overload resolution

---

### 6. **test_events.naja** (Events)
**Error:** `TypeLoadException: Cannot instantiate type: null`

**Root Cause:**
- Event handler instantiation failing
- Null type reference during event subscription setup
- Delegates not properly constructed for event handlers

**Fix Location:** `Naja.CodeGen/Emitters/Expressions/CallEmitters.cs` - Event handler wrapping

---

### 7. **test_event_unsubscribe.naja** (Events)
**Error:** `Exception: Handler should not fire after unsubscription (at 20:1)`

**Root Cause:**
- Event unsubscription not working correctly
- Handler reference not matching after delegation
- Likely due to delegate wrapper not preserving identity

**Expected Behavior:** After `-=` unsubscription, handler should not fire
**Actual Behavior:** Handler fires anyway

**Fix Location:** `Naja.CodeGen/Emitters/Expressions/CallEmitters.cs` - Event subscription/unsubscription

---

### 8. **test_exception_propagation.naja** (Exceptions)
**Error:** `TargetInvocationException: Exception has been thrown by the target of an invocation`

**Root Cause:**
- Exception wrapping/unwrapping issue in reflection-based invocation
- Naja isn't properly propagating exceptions from reflected method calls

**Fix Location:** `Naja.CodeGen/Builtins/ReflectionHelpers.cs` - Exception handling

---

### 9. **test_exception_propagation.naja** (Nested)
**Error:** Same as #8, potentially in a different context

**Root Cause:** Same as #8

---

## Type Conversion Flow Issues

### Current Flow (Broken):
```
Naja Function → Direct .NET Type Conversion → FAIL
                 (No delegate wrapping)

String → Complex Type → FAIL
         (No specialized handling)
```

### Required Flow (Fixed):
```
Naja Function → Delegate Wrapper Check → ThreadStart/EventHandler/etc. → OK

String → Type-Specific Converters → FontFamily/DateOnly/etc. → OK
         (Check for specialized factory methods)

Complex Type → Constructor Analysis → Correct Overload Selection → OK
```

---

## Root Cause Summary

| Component | Issue | Severity | Fix Priority |
|-----------|-------|----------|--------------|
| TypeSystem.CreateDotNet() | Missing delegate wrapping | High | 1 |
| TypeCoercion.cs | Missing DateTime/DateOnly conversion | High | 2 |
| CallEmitters.cs | Missing event handler instantiation | High | 3 |
| TypeSystem.CreateDotNet() | Missing FontFamily constructor detection | Medium | 4 |
| Reflection invocation | Exception not properly propagated | Medium | 5 |
| Type resolution | Null type references in some cases | Medium | 6 |

---

## Files Requiring Changes

1. **Naja.CodeGen/Builtins/TypeSystem.cs**
   - Add delegate conversion (NajaFunction → Delegate)
   - Improve constructor overload resolution
   - Fix exception propagation in CreateDotNet()

2. **Naja.StdLib/Core/TypeCoercion.cs**
   - Add DateTime/DateOnly conversion
   - Add FontFamily/Font handling

3. **Naja.CodeGen/Emitters/Expressions/CallEmitters.cs**
   - Fix event handler creation
   - Fix event unsubscription matching

4. **Naja.CodeGen/Builtins/ReflectionHelpers.cs**
   - Improve exception unwrapping for reflected calls

---

## Test Files Requiring Fixes

```
Naja.WinForms.Tests\testdata\
├── winforms\
│   └── test_nullables.naja              [Type coercion - DateTime]
├── threading\
│   ├── test_invoke_required.naja        [Delegate wrapping]
│   └── test_fonts.naja                  [FontFamily conversion]
├── disposal\
│   ├── test_dispose.naja                [SerializationInfo]
│   └── test_using_pattern.naja          [Type resolution]
├── events\
│   ├── test_events.naja                 [Event handler instantiation]
│   └── test_event_unsubscribe.naja      [Event unsubscription]
└── exceptions\
    └── test_exception_propagation.naja  [Exception handling]
```


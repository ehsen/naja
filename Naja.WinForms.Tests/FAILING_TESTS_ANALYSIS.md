# WinForms Tests Failure Analysis

**Test Run Summary:**
- Total Tests: 28
- Passing: 11
- Failing: 17

## Compiler Bugs/Limitations Identified

### 1. Lambda/Closure Variable Capture (2 tests)
**Files:** `test_invoke_required.naja`, `test_closure_capture.naja`

**Error:**
```
[L70:C39] Undefined name 'target'
[L26:C32] Undefined name 'captured_factor'
```

**Issue:** Variables defined in outer scope are not accessible within lambda expressions or nested functions. The compiler is not properly capturing variables from enclosing scopes.

**Code Pattern That Fails:**
```python
def update_label(index):
    target = labels[index]  # target not captured in lambda
    target.Invoke(lambda: setattr(target, 'Text', "Updated"))
```

---

### 2. Missing len() Builtin for .NET Types (2 tests)
**Files:** `test_strings.naja` (both threading and graphics folders)

**Error:**
```
Exception: Long string length should be preserved
```

**Issue:** The `len()` builtin function doesn't work on .NET string types. It likely only works on Python-native sequences.

**Code Pattern That Fails:**
```python
lbl.Text = "A" * 10000
assert len(lbl.Text) == 10000  # len() fails on .NET string
```

---

### 3. Enum Instantiation via Constructor (3 tests)
**Files:** `test_fonts.naja` (threading & graphics), `test_flowlayout.naja`

**Error:**
```
MissingMethodException: Constructor on type 'System.Drawing.FontStyle' not found for 1 arg(s)
MissingMethodException: Constructor on type 'System.Windows.Forms.FlowDirection' not found for 1 arg(s)
```

**Issue:** Cannot create enum values using constructor syntax like `FontStyle(1)` or `FlowDirection(0)`. Enums in .NET don't have constructors, but the compiler should convert this to the appropriate enum value.

**Code Pattern That Fails:**
```python
FontStyle(1)  # Should create FontStyle.Bold
FlowDirection(0)  # Should create FlowDirection.LeftToRight
```

---

### 4. Enum Member Access by Name (3 tests)
**Files:** `test_enums.naja`, `test_layout.naja`, `test_exception_propagation.naja`

**Error:**
```
MissingFieldException: Field not found: 'System.Windows.Forms.DockStyle.Fill'
MissingFieldException: Field not found: 'System.Windows.Forms.DockStyle.Top'
```

**Issue:** Cannot access enum values using dot notation like `DockStyle.Fill` or `FontStyle.Bold`. The compiler is looking for static fields but enums are value types with specific member resolution rules.

**Code Pattern That Fails:**
```python
txt.Dock = DockStyle.Fill  # Cannot access enum member by name
```

---

### 5. Module Import - System Namespace (1 test)
**File:** `test_nullables.naja`

**Error:**
```
[L11:C18] Undefined name 'System'
```

**Issue:** Cannot import from the `System` namespace using `from System import DateTime`. The import system may not be resolving the System.dll assembly correctly.

**Code Pattern That Fails:**
```python
from System import DateTime  # System namespace not found
```

---

### 6. Event Handler Binding for Module-Level Functions (3 tests)
**Files:** `test_exception_propagation.naja`, `test_event_args.naja`, `test_event_unsubscribe.naja`

**Error:**
```
TypeError: event handler target is null for 'Click' (expected something like self.method)
```

**Issue:** Event subscription (`+=`) doesn't work with module-level functions. The event binding system expects instance methods (with a `self` target) but gets `null` for module-level functions.

**Code Pattern That Fails:**
```python
def handler(s, e):
    pass
btn.Click += handler  # handler target is null
```

---

### 7. Single-Line Function Definition Parsing (1 test)
**File:** `test_multicast_events.naja`

**Error:**
```
Parse error: [L64:C17] Expected Indent but got 'counter4'
```

**Issue:** The parser doesn't support single-line function definitions. Python allows `def func(): pass` but the Naja parser expects an indented body.

**Code Pattern That Fails:**
```python
def h1(s, e): counter2[0] += 1  # Parser expects indent, not statement on same line
```

---

### 8. with Statement Disposal (1 test)
**File:** `test_using_pattern.naja`

**Error:**
```
Exception: TrackingForm 'form1' should be disposed on with exit
```

**Issue:** The `with` statement is not properly calling `Dispose()` when exiting the block for custom Form subclasses. The `__exit__` method or disposal protocol is not being invoked correctly.

**Code Pattern That Fails:**
```python
with TrackingForm("form1") as f1:
    f1.Text = "Test"
# Dispose() should be called here but isn't
```

---

### 9. Numeric Type Coercion - Int64 to Int32 (1 test)
**File:** `test_dispose.naja`

**Error:**
```
ArgumentException: Parameter is not valid. (at Bitmap constructor)
```

**Issue:** When passing Int64 values to constructors/methods expecting Int32, the compiler is not properly narrowing the numeric type. `Bitmap(100, 100)` receives Int64 but needs Int32.

**Code Pattern That Fails:**
```python
bmp = Bitmap(100, 100)  # 100 is Int64, Bitmap expects Int32
```

---

## Test Logic Issues (Not Compiler Bugs)

### 10. Data Binding Timing Issue (1 test)
**File:** `test_binding_source.naja`

**Error:**
```
Exception: Grid should show 3 rows from DataTable
```

**Issue:** The test expects `grid.Rows.Count == 3` immediately after setting `grid.DataSource = bs`, but WinForms data binding may be asynchronous or require a layout update. The grid may show 0 rows initially until the binding is fully established.

**Potential Fix:** Add a layout refresh or check for `>= 3` instead of `== 3`.

---

## Summary Table

| # | Issue | Tests Affected | Category |
|---|-------|----------------|----------|
| 1 | Lambda/Closure Variable Capture | 2 | Compiler Bug |
| 2 | len() on .NET Types | 2 | Compiler Bug |
| 3 | Enum Constructor Syntax | 3 | Compiler Bug |
| 4 | Enum Member Access | 3 | Compiler Bug |
| 5 | System Namespace Import | 1 | Compiler Bug |
| 6 | Event Handler Binding | 3 | Compiler Bug |
| 7 | Single-Line Function Parse | 1 | Parser Limitation |
| 8 | with Statement Disposal | 1 | Compiler Bug |
| 9 | Int64 to Int32 Coercion | 1 | Compiler Bug |
| 10 | Data Binding Timing | 1 | Test Issue |

**Total: 14 Compiler Bugs, 1 Parser Limitation, 1 Test Issue**

## Recommended Priority for Fixes

### High Priority (Core Language Features)
1. Lambda/Closure Variable Capture (#1) - Breaks functional programming patterns
2. Event Handler Binding (#6) - Critical for WinForms development
3. Enum Member Access (#4) - Essential for WinForms API usage
4. Int64/Int32 Coercion (#9) - Basic numeric operations

### Medium Priority (Quality of Life)
5. len() on .NET Types (#2) - Common operation
6. System Namespace Import (#5) - Required for many APIs
7. with Statement Disposal (#8) - Resource management

### Lower Priority (Edge Cases)
8. Enum Constructor Syntax (#3) - Workaround available (use literals)
9. Single-Line Function Parse (#7) - Style issue, has workaround

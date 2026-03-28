# CPython Stdlib Implementation Strategy for Naja

## Overview

This document outlines the comprehensive strategy for implementing a native C# Python stdlib that satisfies the exact behavioral contract of CPython. This follows the **naja_stdlib_v2** strategy: build native C# implementations instead of compiling CPython source.

## Architecture Completed

### Core Utilities (Core/)

All stdlib modules now depend on three shared utility classes:

1. **PythonException.cs** — Exception factory
   - Creates Python-compatible exception types with exact CPython messages
   - All modules throw exceptions via this factory
   - Ensures `test_<module>` assertions on exception type/message pass exactly

2. **Protocols.cs** — Dunder method dispatch
   - Implements protocol dispatch for `__floor__`, `__ceil__`, `__index__`, `__float__`, `__int__`, `__str__`, `__repr__`, `__len__`, `__bool__`, `__iter__`
   - CRITICAL: Protocol dispatch happens **before** type coercion
   - User-defined types with custom dunder methods are respected

3. **TypeCoercion.cs** — Python-correct type conversion
   - `ToDouble(object)` — checks `__float__` then primitive conversion
   - `ToLong(object)` — checks `__index__` then `__int__` then primitive conversion
   - `ToBool(object)` — implements Python truthiness rules
   - `GetLen(object)` — delegates to `__len__` protocol
   - All type conversion in the stdlib uses these helpers, never `Convert.ToXXX`

## Modules Implemented (Phase 1)

### ✅ Complete and Enhanced

1. **math** (Naja.StdLib/NajaMath.cs)
   - All constants: pi, e, tau, inf, nan
   - All rounding functions: floor, ceil, trunc, fabs
   - All power/log functions: sqrt, cbrt, exp, log, log2, log10, pow
   - All trig functions: sin, cos, tan, asin, acos, atan, atan2, sinh, cosh, tanh, degrees, radians
   - All number theory: factorial, gcd, isfinite, isinf, isnan, hypot, copysign, remainder
   - Combinatorics: comb, perm
   - **Protocol dispatch**: floor() and ceil() check for `__floor__` and `__ceil__` dunder methods before converting to double
   - **Error types**: ValueError, OverflowError raised via PythonException factory

2. **sys** (Naja.StdLib/NajaSys.cs)
   - argv — command-line arguments
   - version, version_info, platform, maxsize
   - stdin, stdout, stderr
   - exit() — process termination with exit code
   - executable — path to running executable
   - getrecursionlimit(), setrecursionlimit() — no-op stubs
   - settrace(), gettrace(), getframe() — no-op stubs (Naja has no trace infrastructure)
   - **Type coercion**: exit(code) uses TypeCoercion.ToLong

3. **os** / **os.path** (Naja.StdLib/NajaOs.cs)
   - **os.path**: join, exists, isfile, isdir, isabs, dirname, basename, abspath, normpath, realpath, split, splitext, expanduser, expandvars, sep, pathsep
   - **os**: sep, pathsep, linesep, curdir, pardir, getcwd, chdir, listdir, mkdir, makedirs, rmdir, remove, unlink, rename, environ, getenv, putenv, getpid, getppid, system
   - **String conversion**: All path operations use S() helper for safe string conversion

### 🚧 Phase 1 Next Steps

The following modules follow the same pattern and should be implemented in order of priority:

#### 4. **re** (regex)
- Match compiled patterns against strings
- Implement match, search, findall, split, sub, subn
- Handle groups, flags, special sequences
- Use System.Text.RegularExpressions as backing

#### 5. **io** (I/O streams)
- StringIO — in-memory text buffer
- BytesIO — in-memory binary buffer
- TextIOWrapper — wraps streams
- Critical dependency for json, csv modules

#### 6. **json** (JSON serialization)
- dumps() — serialize Python objects to JSON string
- loads() — parse JSON string to Python objects
- Handle Python-specific type coercion (int vs float)
- Use System.Text.Json as backing

#### 7. **collections** (Container types)
- namedtuple — create lightweight classes with named fields
- defaultdict — dict with default factory
- Counter — count occurrences of hashable objects
- deque — double-ended queue
- OrderedDict — preserve insertion order
- Mix of custom + .NET backing

#### 8. **itertools** (Iterator tools)
- chain, cycle, repeat, accumulate
- islice, count, zip_longest
- combinations, permutations, product
- Pure logic — no I/O dependency

#### 9. **functools** (Function tools)
- partial — create partial function application
- reduce — aggregate values with a function
- lru_cache — memoization decorator
- wraps — decorator utility

#### 10. **datetime** (Date/time types)
- datetime — combined date and time
- date — year, month, day only
- time — hour, minute, second, microsecond
- timedelta — time duration
- Use System.DateTime, TimeSpan as backing

## Implementation Workflow Per Module

For each new module, follow this pattern:

### 1. **Study CPython Test File**
```bash
# Read the test specification
cat F:\Sources\cpython\Lib\test\test_<module>.py
```
The tests ARE the specification. Not the documentation. Edge cases in test_* reveal the true contract.

### 2. **Understand the Module's Contract**
- What does each function take as input?
- What exceptions does it raise and under what conditions?
- What is the exact message text?
- What return type is expected?

### 3. **Implement the C# Module**
```csharp
// Naja.StdLib/Naja<Module>.cs

using Naja.StdLib.Core;

namespace Naja.StdLib;

public sealed class Naja<Module>
{
    public static readonly Naja<Module> Instance = new();

    // Each function/property follows this pattern:
    // 1. Check for protocol methods first (if applicable)
    // 2. Use TypeCoercion for any type conversion
    // 3. Use PythonException for all errors
    // 4. Return the exact type CPython returns
}
```

### 4. **Create Type Stubs** (.pyi files)
```python
# Naja.StdLib/stubs/<module>.pyi
# Minimal type hints for import resolution
```

### 5. **Validate Against CPython Test Suite**
```bash
# Run the CPython test in Naja
# If test_<module> doesn't pass 100%, the implementation isn't done
```

## Critical Rules (No Exceptions)

1. **Protocol dispatch before coercion**
   - Always check for `__floor__`, `__ceil__`, `__float__`, `__int__`, etc. first
   - Only call TypeCoercion after confirming dunder methods don't apply

2. **Exception messages must match CPython exactly**
   - "math domain error" not "invalid argument"
   - "cannot unpack sequence of length N into N values" not "wrong length"
   - Run CPython locally and copy the exact message string

3. **Return types must match CPython**
   - `math.floor()` returns `int` (C# `long`), never `float`
   - `re.findall()` returns `list`, never `IEnumerable`
   - `collections.Counter` inherits from `dict`, not just implements IEnumerable

4. **No `Convert.ToXXX` in module code**
   - All type conversion goes through `TypeCoercion`
   - This is the only way to ensure `__float__`, `__int__`, etc. are invoked

5. **No .NET exceptions in the Python layer**
   - `System.DivideByZeroException` must become `PythonZeroDivisionError`
   - `System.OverflowException` must become `PythonOverflowError`
   - Catch and re-throw with the correct message

6. **Test gate is 100%, not 90%**
   - Partial passing is a code smell — it means edge cases are wrong
   - A module is not done until `test_<module>` passes completely

## AOT / WASM Readiness

### Already Built In
- All public exception types are defined (no reflection-based creation)
- Protocol dispatch uses type methods safely (no synthetic invocation)
- TypeCoercion doesn't use reflection for dispatch

### Future Additions (Phase 2)
- Add `[DynamicallyAccessedMembers]` attributes for NativeAOT linker hints
- Verify binary size with NativeAOT compilation (tree shaking works)

## File Structure

```
Naja.StdLib/
├── Core/
│   ├── PythonException.cs      ✅ Complete
│   ├── Protocols.cs            ✅ Complete
│   └── TypeCoercion.cs         ✅ Complete
├── Naja<Module>.cs
│   ├── NajaMath.cs             ✅ Complete + cbrt()
│   ├── NajaSys.cs              ✅ Complete
│   ├── NajaOs.cs               ✅ Complete (os + os.path)
│   ├── NajaRe.cs               🚧 Next
│   ├── NajaIO.cs               🚧 Planned
│   ├── NajaJson.cs             🚧 Planned
│   ├── NajaCollections.cs      🚧 Planned
│   ├── NajaItertools.cs        🚧 Planned
│   ├── NajaFunctools.cs        🚧 Planned
│   └── NajaDateTime.cs         ✅ Existing (needs validation)
└── Naja.StdLib.csproj
```

## Testing Strategy

1. **Unit tests in test suite** — each Naja.*.Tests project validates compiler behavior
2. **CPython test suite** — run individual test_* files against Naja-compiled modules
3. **Integration tests** — verify modules work together (e.g., json + io)

## Success Criteria

- **Per module**: `test_<module>` passes 100%
- **Cross-module**: Modules that depend on each other (json → io) work together
- **Protocol dispatch**: User-defined types with dunder methods work correctly
- **Exception accuracy**: Exception types and messages match CPython exactly
- **Type correctness**: Return types match CPython (int vs float, list vs tuple, etc.)

---

**Next Action**: Implement re (regex) module following the pattern above. Start by reading test_re.py to understand the exact contract.

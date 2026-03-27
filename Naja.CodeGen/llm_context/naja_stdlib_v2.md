# Naja Compiler — stdlib Migration Plan v2
> Python → .NET MSIL | Native C# stdlib with Python API contract
> Replaces v1 strategy of compiling CPython source directly

---

## 1. Why v1 Failed

The original plan assumed CPython's "pure Python" modules could be fed directly into the Naja compiler. In practice this is a bootstrapping trap:

- Even simple modules like `os.path` import from `posixpath`, which imports from `stat`, which imports from `_stat` (a C extension), which doesn't exist in Naja's runtime.
- CPython's test suite (`test_cpython_*`) exposed the same problem — dependencies cascade in every direction before a single module can load.
- A young compiler cannot be expected to handle this dependency graph. Trying to do so burns development time on infrastructure instead of language correctness.

**The root mistake:** trying to *compile* Python stdlib source before the compiler is mature enough to handle its own dependencies.

---

## 2. The New Strategy: Native C# stdlib, Python-contract API

Instead of compiling Python source, we **write C# implementations** that satisfy the exact behavioral contract of each Python module. The compiler's job is reduced to:

1. Resolving `import math` → `NajaStdlib.Modules.Math`
2. Emitting IL calls into that pre-built assembly

No dependency graph resolution. No bootstrapping problem. The stdlib is a stable, independently compilable C# project.

### How IronPython and Jython solved this same problem

Both runtimes took this exact approach at the same stage of development. It is not a compromise — it is the proven architecture for a Python runtime on a managed VM. The C# implementation *is* the stdlib. Compiled Python source is an optimization for later, not a requirement now.

---

## 3. Single Assembly Design

All stdlib modules live in **one assembly**: `NajaStdlib.dll`.

### Why one assembly, not one project per module

| Concern | Answer |
|---|---|
| "Too much code in one place" | AOT/WASM tree shaking eliminates unused methods at link time. You pay only for what user code actually calls. |
| "Hard to manage" | One import resolver lookup. One version. No cross-project dependency graph inside the stdlib itself. |
| "Slower to compile" | C# compiles fast. This is never the bottleneck. |
| Multiple projects | Requires a discovery mechanism, versioning, and NuGet-like resolution — compiler infrastructure you don't want yet. |

### Assembly layout

```
NajaStdlib/
├── NajaStdlib.csproj          ← single project, targets net9.0
├── Core/
│   ├── PythonException.cs     ← shared exception factory (exact CPython messages)
│   ├── Protocols.cs           ← __floor__, __ceil__, __index__, __float__ dispatch
│   ├── TypeCoercion.cs        ← Python-correct type coercion helpers
│   └── NajaObject.cs          ← base object protocol
├── Modules/
│   ├── Sys.cs
│   ├── OsPath.cs
│   ├── Math.cs
│   ├── Re.cs
│   ├── Io.cs
│   ├── Json.cs
│   ├── Collections.cs
│   ├── Itertools.cs
│   ├── Functools.cs
│   └── Datetime.cs
└── Stubs/
    ├── sys.pyi
    ├── os/path.pyi
    ├── math.pyi
    ├── re.pyi
    ├── io.pyi
    ├── json.pyi
    ├── collections.pyi
    ├── itertools.pyi
    ├── functools.pyi
    └── datetime.pyi
```

### The Core/ layer is the real unlock

Every module needs the same primitives. Writing them once in `Core/` means:

- Exception messages match CPython exactly (`"math domain error"`, not a .NET message)
- Protocol dispatch (`__floor__`, `__index__`, etc.) is correct everywhere
- Type coercion goes through Python's rules, not `Convert.ToDouble`

Without a shared `Core/`, these patterns get copy-pasted and diverge across modules.

---

## 4. AOT / WASM Considerations

Plan for NativeAOT from day one — retrofitting is painful.

### Tree shaking
NativeAOT and WASM linkers do dead code elimination at the **method level**. A single `NajaStdlib.dll` with every stdlib module compiled in will produce a binary that only contains the methods reachable from user code. The assembly size at development time is irrelevant.

### Trim-safe protocol dispatch
NativeAOT's linker cuts reflection that isn't annotated. Any protocol dispatch in `Protocols.cs` that uses reflection must use `[DynamicallyAccessedMembers]`:

```csharp
// Core/Protocols.cs
public static object InvokeFloor(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type t,
    object x)
{
    var method = t.GetMethod("__floor__");
    return method?.Invoke(x, null) ?? FallbackFloor(x);
}
```

This annotation tells the linker: "keep `__floor__` on any type passed here." Without it, NativeAOT silently removes the method and dispatch fails at runtime with no useful error.

---

## 5. Implementation Contract Per Module

Each C# module class must satisfy three requirements, in this order:

### 5.1 Correct exception types and messages
CPython tests assert exact exception type and message string. This must match.

```csharp
// Core/PythonException.cs
public static Exception ValueError(string message) =>
    new PythonValueError(message);   // maps to Python ValueError

// Usage in Math.cs
if (x <= 0)
    throw PythonException.ValueError("math domain error");
```

### 5.2 Protocol dispatch before type coercion
Python checks for dunder methods on the argument type before falling back to primitive conversion. This is not optional — CPython's test suite probes it directly.

```csharp
// Math.cs — floor()
public static object Floor(object x)
{
    // 1. Protocol dispatch first
    var floorMethod = Protocols.FindMethod(x, "__floor__");
    if (floorMethod != null)
        return floorMethod.Invoke(x, null);

    // 2. Only then coerce to float
    double v = TypeCoercion.ToFloat(x);   // calls __float__, not Convert.ToDouble
    return (BigInteger)Math.Floor(v);      // return type is int, not float
}
```

### 5.3 Return types must match CPython exactly
`math.floor` returns `int`, not `float`. `math.gcd` returns `int`. `re.findall` returns `list`. These are tested.

---

## 6. The Top 10 Modules — Priority Order

Ordered by how much real-world code each one unblocks.

| Priority | Module | .NET Backing | Notes |
|---|---|---|---|
| 1 | `sys` | Runtime metadata | `sys.argv`, `sys.version`, `sys.path` — imported by almost everything |
| 2 | `os.path` | `System.IO.Path` | File manipulation in nearly every script |
| 3 | `math` | `System.Math`, `BigInteger` | Well-defined contract, good first proof-of-pattern |
| 4 | `re` | `System.Text.RegularExpressions` | Huge unlock for parsers and text tools |
| 5 | `io` | `System.IO.Stream` | `StringIO`/`BytesIO` needed by parsers, json, csv |
| 6 | `json` | `System.Text.Json` | Very mappable, needed by almost all web-facing code |
| 7 | `collections` | Custom + BCL | `defaultdict`, `OrderedDict`, `Counter`, `deque` |
| 8 | `itertools` | Pure logic | No I/O, clean to implement, unblocks functional patterns |
| 9 | `functools` | Delegates + reflection | `partial`, `lru_cache`, `reduce` — needed by many libs |
| 10 | `datetime` | `System.DateTime` | Complex but `System.DateTime`/`TimeSpan` cover most of it |

---

## 7. Implementation Workflow Per Module

For each module, follow this sequence:

```
1. Read the CPython test file (test_<module>.py)
   — The tests are the specification. Not the docs.

2. Read the CPython docs for the module
   — Understand the contract Claude will implement

3. Feed both to Claude with the prompt template (Section 8)
   — Generate the C# implementation

4. Write the .pyi stub
   — This is what Naja's import resolver reads

5. Wire the import resolver
   — import math  →  NajaStdlib.Modules.Math

6. Run the CPython test suite against the compiled output
   — Gate: test_<module> passes. No partial credit.
```

### Validation gate

A module is not done until its CPython test file passes. Behavioral differences compound up the stack and produce hard-to-diagnose failures in user code. 90% passing is not passing.

---

## 8. Claude Prompt Template for Module Generation

Use this template when generating each C# module implementation:

```
You are implementing Python's `{module}` module for the Naja compiler,
a Python-to-.NET MSIL compiler. The implementation must be in C#.

Requirements:
1. The implementation must satisfy every test in the attached CPython
   test file (test_{module}.py). Read the test file carefully — the
   tests are the specification, not the docs.

2. Exception types must match CPython exactly:
   - ValueError with the exact CPython message string
   - TypeError with the exact CPython message string
   - Never let .NET exceptions propagate to the Python layer

3. Protocol dispatch must happen before type coercion:
   - Check for __floor__, __ceil__, __index__, __float__ etc. before
     calling any Convert.* or cast
   - Use the Protocols helper class (provided separately)

4. Return types must match CPython:
   - math.floor() returns int (BigInteger), not float
   - List-returning functions return PyList, not IEnumerable

5. Use System.Math, System.Text.RegularExpressions, System.IO, etc.
   as the .NET backing — never reimplement what the BCL provides.

Attached:
- test_{module}.py (CPython test file — the specification)
- {module} documentation excerpt
- Core/PythonException.cs (shared exception factory)
- Core/Protocols.cs (shared protocol dispatch)
- Core/TypeCoercion.cs (Python-correct coercion)

Generate: Modules/{Module}.cs
```

---

## 9. Future Migration Path (When Compiler Matures)

The C# stdlib is not a dead end. It is the stable foundation that lets the compiler grow without being blocked.

Once Naja can compile non-trivial Python without dependency issues, modules can be **optionally** migrated to compiled Python — but this is never required. The C# implementation remains valid indefinitely.

```
Phase 1 (now)     → C# implementations, Python API contract
Phase 2 (6-12mo)  → Compiler handles pure Python modules with no C deps
                    Migrate Tier 1 modules (string, textwrap, difflib, etc.)
                    C# implementations remain for Tier 2/3
Phase 3 (12mo+)   → @__intrinsic__ system for C-backed hot paths
                    C# stdlib becomes the intrinsic backing layer
                    Python source becomes the public API surface
```

The `@__intrinsic__` system from v1 still has a role — but in Phase 3, not Phase 1. It is the optimization layer on top of correct code, not the foundation.

---

## 10. Anti-Patterns to Avoid (Carried Forward from v1)

### ❌ Using Convert.ToDouble or Convert.ToInt64 in module implementations

These bypass Python's type coercion protocol. All coercion must go through `TypeCoercion.cs` which calls `__float__`, `__int__`, etc. correctly.

### ❌ Letting .NET exceptions propagate

`System.OverflowException`, `System.DivideByZeroException` must be caught and re-thrown as the correct Python exception type with the correct message.

### ❌ Skipping the CPython test file

Reading only the documentation misses the contract. The docs describe the happy path. The tests describe what must actually work including edge cases like `math.floor(True)`, `math.gcd(0, 0)`, `math.factorial(2.0)`.

### ❌ One project per module

Solves nothing and creates a discovery and versioning problem inside the stdlib itself.

### ❌ Implementing based on "this will probably work"

If `test_<module>` doesn't pass, it doesn't work. The test suite is the only gate.

---

## Summary

| What | How |
|---|---|
| stdlib architecture | Single `NajaStdlib.dll` assembly |
| Implementation language | C#, written with Claude assistance |
| API contract | Exact CPython behavioral contract per module |
| Validation gate | CPython `test_<module>` passes 100% |
| AOT / WASM | Tree shaking handles binary size; trim-safe dispatch from day one |
| Dependency problem | Eliminated — C# compiles independently of Naja |
| Migration path | C# stdlib → compiled Python stdlib as compiler matures |
| Phase 1 target | Top 10 modules that unblock 80% of real user code |

The compiler is young. The stdlib should not be. Build a stable, testable, AOT-ready C# stdlib now, and let the compiler grow into it.

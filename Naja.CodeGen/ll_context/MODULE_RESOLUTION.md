# Naja Module Resolution Design

**Project**: Naja Python-to-.NET Compiler  
**Branch**: `dev`  
**Last Updated**: January 2026  
**Status**: Partially Implemented — Active Development

---

## Overview

Module resolution in Naja handles Python `import` and `from … import` statements by mapping Python module names to .NET types, assemblies, or built-in Naja runtime objects. It is a **compile-time** process: by the time IL is emitted, every import name is already resolved to a concrete CLR type or a runtime singleton.

There are four distinct import categories, each with its own resolution path:

| Category | Example | Resolution Path |
|---|---|---|
| **Built-in Naja modules** | `import re` | → `NajaReModule.Instance` singleton |
| **Namespace imports** | `import System` | → namespace stub; type resolved at attribute access |
| **Type imports** | `from System.Drawing import Color` | → `FrameworkTypeResolver` → `AppDomain` fallback |
| **StdLib modules** | `import os`, `import unittest` | ⚠️ Not yet implemented |

---

## Pipeline Stages

### Stage 1 — Parsing (`Naja.Parser`)

The parser produces two AST nodes from `Statements.cs`:

```csharp
// import os  /  import os.path as p
public sealed record ImportStatement(
    IReadOnlyList<ImportAlias> Names,
    int Line, int Column
) : Statement(Line, Column);

// from System.Drawing import Color, Font
public sealed record FromImportStatement(
    string Module,
    IReadOnlyList<ImportAlias> Names,   // empty = star import (*)
    int Level,                           // dots for relative import
    int Line, int Column
) : Statement(Line, Column);
```

`Parser.StatementParser.cs` handles:
- `import a.b.c as alias` — dotted names, optional alias
- `from a.b import X, Y` — named imports
- `from a.b import (X, Y)` — parenthesised list
- `from a.b import *` — star import (parsed, not yet emitted)
- `from .sub import X` — relative imports (`Level > 0`, not yet emitted)

---

### Stage 2 — Semantic Analysis (`Naja.Semantics`)

`SemanticAnalyzer.cs` registers import names into the current scope with `SymbolKind.Import` and type `NajaTypes.Unknown`. No assembly or type resolution occurs at this stage — it only ensures names are defined so later semantic passes can reference them without errors.

```csharp
private void Analyze(ImportStatement s)
{
    foreach (var alias in s.Names)
    {
        var name = alias.Alias ?? alias.Name.Split('.')[0];
        _scope.Define(name, SymbolKind.Import, NajaTypes.Unknown, s.Line, s.Column);
    }
}

private void Analyze(FromImportStatement s)
{
    foreach (var alias in s.Names)
    {
        var name = alias.Alias ?? alias.Name;
        _scope.Define(name, SymbolKind.Import, NajaTypes.Unknown, s.Line, s.Column);
    }
}
```

---

### Stage 3 — Import Map Construction (`AssemblyEmitter.ModuleEmission.cs`)

The first thing `EmitModule()` does — before Pass 1 stub declaration — is build two dictionaries from the module's top-level statements:

```
importMap         : string → (TypeName, AssemblyName)   // from X import Y
namespaceImports  : string → string                     // import X
```

**`FromImportStatement` processing:**

```
from System.Drawing import Color
  → typeName = "System.Drawing.Color"
  → asmShortName = FrameworkTypeResolver.ResolveAssemblyName("System.Drawing.Color")
                ?? AppDomain search
                ?? "System.Drawing"   (last-resort: use module name)
  → importMap["Color"] = ("System.Drawing.Color", "System.Drawing")
```

**`ImportStatement` processing:**

```
import System
  → namespaceImports["System"] = ""   (assembly TBD at attribute-access time)

import re
  → namespaceImports["re"] = ""       (handled as special case at emit time)
```

Both dictionaries are threaded into `EmitContext` and made available to all expression emitters.

---

### Stage 4 — Type Resolution (`FrameworkTypeResolver`)

`FrameworkTypeResolver` (`Naja.CodeGen/Emitters/Assembly/FrameworkTypeResolver.cs`) performs disk-based .NET type lookup using `MetadataLoadContext` — no runtime assembly loading required.

**Resolution order:**

1. **Known-type table** — fast path for common ASP.NET Core / WinForms / Hosting types hardcoded in `_knownTypeToAssembly`.
2. **MetadataLoadContext** — scans all `.dll` files found under:
   - `$DOTNET_ROOT/packs/Microsoft.NETCore.App.Ref/**`
   - `$DOTNET_ROOT/packs/Microsoft.WindowsDesktop.App.Ref/**`
   - `$DOTNET_ROOT/packs/Microsoft.AspNetCore.App.Ref/**`
   - `RuntimeEnvironment.GetRuntimeDirectory()/**`
3. **AppDomain fallback** — used at import-map construction time to catch types already loaded by the host process (e.g. `System.Private.CoreLib`).

Results are cached per `fullyQualifiedTypeName` (case-insensitive).

---

### Stage 5 — IL Emission

#### 5a. Name emission (`NameEmitters.cs`)

When the compiler encounters a bare name that resolves to an import:

**Priority 6a — Namespace import (`import System`):**
```
if e.Name == "re"  →  ldsfld NajaReModule::Instance       (built-in module singleton)
else               →  ldnull                               (namespace stub; real resolution in EmitAttribute)
```

**Priority 6b — Type import (`from System.X import Y`):**
```
resolvedType != null  →  ldtoken <Type>; call Type.GetTypeFromHandle  (compile-time)
resolvedType == null  →  ldstr "TypeName"; call Type.GetType           (runtime fallback)
```

#### 5b. Attribute access (`AttributeEmitters.cs`)

When the compiler encounters `Namespace.Type` (e.g. `System.DateTime`):

```
ctx.NamespaceImports["System"] exists
  → fullTypeName = "System" + "." + "DateTime"
  → AppDomain search (or assembly-qualified lookup)
  → ldtoken <Type>; call Type.GetTypeFromHandle
```

When the compiler encounters `ImportedType.Member` (e.g. `Color.White`):

```
ctx.ImportMap["Color"] = ("System.Drawing.Color", "System.Drawing")
  → AppDomain search → Type.GetType with AQN → Type.GetType plain
  → dotnetType.GetField("White") or GetProperty("White")
  → emit static field load / property call
```

---

## Built-in Module Support

### Currently Implemented

| Python Module | Naja Implementation | Notes |
|---|---|---|
| `re` | `NajaReModule` (`Builtins/NajaReModule.cs`) | `search`, `match`, `compile`, `sub`, `findall` |

`NajaReModule` is a singleton (`NajaReModule.Instance`) returned when `re` is used as a name. Its methods delegate to `System.Text.RegularExpressions.Regex`.

### Not Yet Implemented

The following commonly-used Python standard-library modules have no Naja implementation. They are tracked as blockers for CPython test-suite compliance (see `CPYTHON_INTEGRATION_STRATEGY.md`):

| Module | Priority | Notes |
|---|---|---|
| `os` / `os.path` | High | File system, env vars, path operations |
| `sys` | High | `sys.argv`, `sys.exit`, `sys.path` |
| `unittest` | High | Needed for 98%+ of CPython test suite |
| `math` | Medium | `sqrt`, `floor`, `ceil`, `pi`, `e` etc. |
| `random` | Medium | `random()`, `randint()`, `choice()` etc. |
| `json` | Medium | `dumps`, `loads` |
| `io` / `StringIO` | Medium | In-memory streams |
| `collections` | Medium | `defaultdict`, `OrderedDict`, `Counter`, `deque` |
| `functools` | Low | `reduce`, `partial`, `lru_cache` |
| `itertools` | Low | `chain`, `product`, `combinations` |
| `pathlib` | Low | `Path` objects |
| `typing` | Low | Partially parsed as annotations; no runtime |
| `dataclasses` | Low | `@dataclass` decorator |
| `abc` | Low | `ABC`, `abstractmethod` |

---

## `Naja.StdLib` Project

`Naja.StdLib` (`Naja.StdLib/Class1.cs`) is currently an **empty placeholder**. It is intended to host managed C# implementations of Python standard-library modules that are better served by custom .NET code than by pure delegation to the BCL.

### Planned Role

```
Naja.StdLib.dll
  ├── NajaOs          (os / os.path)
  ├── NajaSys         (sys)
  ├── NajaUnittest    (unittest)
  ├── NajaMath        (math)
  ├── NajaRandom      (random)
  ├── NajaJson        (json)
  ├── NajaIo          (io / StringIO / BytesIO)
  ├── NajaCollections (collections.defaultdict, Counter, deque ...)
  └── NajaFunctools   (functools.reduce, partial ...)
```

Each class follows the same singleton pattern as `NajaReModule`:

```csharp
public sealed class NajaOs
{
    public static readonly NajaOs Instance = new();
    // methods matching Python's os module API
}
```

The compiler maps `import os` → `NajaOs.Instance` at name-emit time, exactly as it already does for `import re` → `NajaReModule.Instance`.

---

## Current Limitations & Known Gaps

### 1. Star imports not emitted
`from x import *` is parsed (produces empty `Names` list in `FromImportStatement`) but no IL is emitted for it. Attempting to use names from a star import will throw `CodeGenException: Undefined name`.

### 2. Relative imports not emitted
`from .sub import X` sets `Level > 0` but the emitter does not yet handle multi-file / package imports. All source files are currently compiled as single-module assemblies.

### 3. Multi-file packages not supported
There is no module search path (no equivalent of `sys.path`). Each `.naja` / `.py` file compiles independently. Cross-file imports (`from mymodule import foo`) are not yet possible.

### 4. `import X` used as a value emits `ldnull`
When a namespace is loaded with `import System` and passed as an object (e.g. `print(System)`), `ldnull` is emitted. This is intentional — namespaces are not first-class objects — but can produce confusing `None` output.

### 5. Runtime vs. compile-time type resolution inconsistency
`from X import Y` tries compile-time `ldtoken` first but falls back to runtime `Type.GetType` when the type is not found by `FrameworkTypeResolver`. This means some type errors are only detected at runtime.

### 6. No `__init__.py` / package concept
Naja has no concept of packages or `__init__.py`. Module names are flat strings.

---

## Data Flow Summary

```
Source file
    │
    ▼
Parser ──► ImportStatement / FromImportStatement (AST)
    │
    ▼
SemanticAnalyzer ──► scope.Define(name, SymbolKind.Import)
    │
    ▼
AssemblyEmitter.EmitModule()
    ├── FromImportStatement ──► FrameworkTypeResolver ──► importMap[localName]
    └── ImportStatement     ──► namespaceImports[nsName]
    │
    ▼
EmitContext carries { ImportMap, NamespaceImports }
    │
    ▼
NameEmitters.EmitName()
    ├── Priority 6a: NamespaceImports
    │       ├── "re" ──► ldsfld NajaReModule::Instance
    │       └── other ──► ldnull (type resolved at attribute access)
    └── Priority 6b: ImportMap
            ├── type resolved ──► ldtoken <Type>
            └── unresolved    ──► ldstr + Type.GetType (runtime)
    │
    ▼
AttributeEmitters.EmitAttribute()
    ├── NamespaceImports: Namespace.Type ──► AppDomain/Type.GetType ──► ldtoken
    └── ImportMap: ImportedType.Member  ──► static field/property IL
```

---

## Implementation Roadmap

### Near-term (unblocks CPython suite)

1. **`NajaUnittest`** in `Naja.StdLib` — `TestCase`, `assertEqual`, `assertTrue`, `assertRaises`, `setUp`/`tearDown`, test runner
2. **`NajaSys`** in `Naja.StdLib` — `argv`, `exit()`, `path` (read-only list), `version`
3. **`NajaOs`** in `Naja.StdLib` — `getcwd()`, `path.join/exists/dirname/basename`, `environ` dict
4. **`NajaMath`** in `Naja.StdLib` — delegates to `System.Math`; exposes `pi`, `e`, `inf`, `nan`

### Medium-term

5. **Multi-file compilation** — compiler driver that resolves cross-module imports by compiling dependency graphs
6. **Compile-time star import** — expand `from x import *` by reflecting the source module's public members
7. **Relative imports** — resolve `Level > 0` using the current file's package path

### Long-term

8. **`sys.path` search** — runtime module loader that searches configured directories for `.naja`/`.py` files
9. **Package support** — honour `__init__.py` / `__init__.naja` as package roots
10. **`importlib` compatibility** — `import_module()`, `reload()`

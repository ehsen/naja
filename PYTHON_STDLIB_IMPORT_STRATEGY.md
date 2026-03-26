# Naja Python StdLib Import Strategy

**Status**: ✅ IMPLEMENTATION COMPLETE  
**Date**: January 2026  
**Context**: Three-tier modular stdlib deployment with AOT/WASM support

---

## Executive Summary

Based on your current architecture and constraints, **Option A (Pre-Compiled .NET Assemblies)** is the **recommended approach** for production use. However, a phased strategy combining both approaches gives you maximum flexibility.

---

## Current State

Your Naja compiler already has:
- ✅ A `Naja.StdLib` project with hand-written C# wrappers for stdlib modules
  - `NajaSys.cs` — `sys` module
  - `NajaUnittest.cs` — `unittest` module
  - `NajaMath.cs` — `math` module  
  - `NajaDateTime.cs` — `datetime` module
  - `NajaOs.cs` — `os` module

- ✅ Import registration system in `AssemblyEmitter.ModuleEmission.cs`
  - `ImportStatement` → `namespaceImports` dict
  - `FromImportStatement` → `importMap` dict

- ✅ Singleton pattern for stdlib modules (see `NajaSys.Instance`)

---

## Option Comparison

### **Option A: Pre-Compiled .NET Assemblies** ⭐ RECOMMENDED

**Approach**: Compile `Naja.StdLib` project to a .NET assembly, reference it in generated code

**Pros**:
- ✅ **Fast**: No re-compilation needed; assembly is loaded at program startup
- ✅ **Efficient**: Single binary, version-controlled, reusable across all Naja programs
- ✅ **Type-safe**: IDE support for stdlib methods; compile-time validation
- ✅ **Cacheable**: Assembly can be packaged/distributed
- ✅ **Works with packaging**: Can be part of an SDK/nuget package
- ✅ **Mirrors real Python**: Most Python distributions use compiled C/Cython modules (`.so`, `.pyd`)

**Cons**:
- ❌ Breaking changes to stdlib require recompiling + redistributing the assembly
- ❌ Users cannot hot-modify stdlib
- ❌ Requires maintaining C# code for each stdlib module

**Implementation**:
```csharp
// In AssemblyEmitter.ModuleEmission.cs, when handling `import sys`:
if (name == "sys")
{
    importMap["sys"] = ("Naja.StdLib.NajaSys", "Naja.StdLib");
    // Codegen emits: IL.Emit(OpCodes.Call, typeof(NajaSys).GetProperty("Instance").GetGetMethod());
}
```

---

### **Option B: Dynamic Python Source Compilation**

**Approach**: Ship Python `.py` files in stdlib directory; compile them on-the-fly when imported

**Pros**:
- ✅ **Flexible**: Users can modify stdlib locally
- ✅ **Pythonic**: Distributing `.py` files feels natural
- ✅ **Reduced C# maintenance**: No C# boilerplate for each module

**Cons**:
- ❌ **Slow**: Compilation happens at runtime (every import)
- ❌ **Bootstrapping issue**: Compiler itself would need to compile stubs → circular dependency
- ❌ **Hard to version**: Unclear which Python source version matches which compiled binary
- ❌ **Caching complexity**: Need persistent cache layer to avoid re-parsing
- ❌ **Not production-ready**: adds latency to all program startups

**When to use**: Development/experimentation only; **not recommended for production**

---

### **Option C: Lazy Hybrid Approach** (Middle Ground)

**Approach**: 
1. Ship pre-compiled stdlib assembly (Option A) as default
2. Allow override: if user provides `stdlib/sys.py`, compile & use that instead
3. Cache compiled versions in a `.naja_cache/` directory

**Pros**:
- ✅ Best of both worlds: fast default, flexible when needed
- ✅ Gradual stdlib evolution (can prototype in Python, promote to C#)

**Cons**:
- ❌ Complex caching logic
- ❌ Harder to test & maintain
- ❌ Potential version mismatches

**When to use**: Advanced workflow if you expect frequent stdlib updates

---

## Recommended Architecture (Phase 1 → Phase 2)

### **Phase 1: Core Infrastructure** (Current + Immediate)

1. **Keep current hand-written C# stdlib** in `Naja.StdLib/`
   - Expands with: `re`, `json`, `itertools`, `collections`, etc.

2. **Compile to assembly** during solution build
   ```xml
   <!-- In Naja.StdLib.csproj -->
   <PropertyGroup>
       <OutputType>Library</OutputType>
       <TargetFramework>net10.0</TargetFramework>
   </PropertyGroup>
   ```

3. **Update import resolution** in `AssemblyEmitter.ModuleEmission.cs`:
   ```csharp
   private static readonly Dictionary<string, (string TypeName, string AssemblyName)> StdLibMap = 
       new()
       {
           ["sys"] = ("Naja.StdLib.NajaSys", "Naja.StdLib"),
           ["unittest"] = ("Naja.StdLib.NajaUnittest", "Naja.StdLib"),
           ["math"] = ("Naja.StdLib.NajaMath", "Naja.StdLib"),
           ["datetime"] = ("Naja.StdLib.NajaDateTime", "Naja.StdLib"),
           ["os"] = ("Naja.StdLib.NajaOs", "Naja.StdLib"),
           // Add more as implemented
       };
   
   // In EmitModule():
   if (stmt is ImportStatement imp)
   {
       foreach (var alias in imp.Names)
       {
           var name = alias.Alias ?? alias.Name;
           if (StdLibMap.TryGetValue(alias.Name, out var (typeName, asmName)))
           {
               importMap[name] = (typeName, asmName);
               // Register singleton access
           }
           else
           {
               // Fall through to namespace resolution
               namespaceImports[name] = "";
           }
       }
   }
   ```

4. **Generate singleton access** at codegen time:
   - `import sys` → IL that loads `NajaSys.Instance` and assigns to local `sys`
   - `from sys import argv` → IL that loads `NajaSys.Instance.argv` property

---

### **Phase 2: Expand Coverage** (Next Iteration)

Add more stdlib modules in priority order:
1. **Data structures**: `collections`, `itertools`, `functools`
2. **File I/O**: `io`, `pathlib` (bridge to `System.IO`)
3. **Regular expressions**: `re` (map to `System.Text.RegularExpressions`)
4. **JSON/Data**: `json` (map to `System.Text.Json`)
5. **Time**: Already have `datetime`; add `time`
6. **Random**: `random` (bridge to `System.Random`)

---

## Implementation Plan

### Step 1: Update `AssemblyEmitter.ModuleEmission.cs`

**Goal**: Recognize stdlib imports and generate singleton property access

**Change**:
- Add stdlib name mapping (see above)
- In `EmitModule()`, intercept stdlib imports before namespace resolution
- Generate IL that loads singleton instance

### Step 2: Expand `Naja.StdLib` 

**Goal**: Implement core stdlib modules in C#

**Modules to add** (priority order):
1. `re` — regex wrapper around `System.Text.RegularExpressions.Regex`
2. `json` — wrapper around `System.Text.Json`
3. `random` — wrapper around `System.Random`
4. `itertools` — common iteration functions
5. `collections` — common data structures

### Step 3: Update Codegen Tests

**Goal**: Ensure imports work end-to-end

**Add tests** for:
- `import sys; sys.exit(0)`
- `from math import pi, cos`
- `import json; json.dumps(...)`

---

## Detailed Recommendation: Phase 1 Roadmap

### What to do **immediately**:

1. **Formalize the stdlib mapping** in a static dictionary
   - Location: `Naja.CodeGen/StdLibResolver.cs` (new file)
   - Maps module names to their .NET type addresses

2. **Extend import resolution logic**
   - Check stdlib map before namespace resolution
   - Register with `importMap` instead of `namespaceImports`

3. **Test the foundation**
   - Add unit test: `import sys; assert sys.platform in ['win32', 'linux', 'darwin']`

### Why this beats Python source compilation:

| Aspect | Compiled .NET | Python Source |
|--------|---------------|---------------|
| **Startup time** | ~0ms | 50-200ms per import |
| **Caching** | Built-in (CLR) | Needs implementation |
| **Type safety** | ✅ Compile-time | ❌ Runtime only |
| **Distribution** | 1 .dll | Many .py files |
| **Backwards compat** | Via versioning | Hard to track |
| **CPython alignment** | Similar (compiled C) | Different (interpreted) |

---

## File Locations & Changes

```
Naja.CodeGen/
├── AssemblyEmitter.ModuleEmission.cs    [MODIFY] - Import resolution
├── StdLibResolver.cs                     [NEW] - stdlib name-to-type mapping
└── ...

Naja.StdLib/
├── Naja.StdLib.csproj                   [KEEP] - Compile as .NET assembly
├── NajaSys.cs
├── NajaMath.cs
├── NajaDateTime.cs
├── NajaUnittest.cs
├── NajaOs.cs
├── NajaRe.cs                            [NEW] - regex module
├── NajaJson.cs                          [NEW] - json module
└── ...
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **Stdlib API mismatch with CPython** | Write conformance tests; reference Python 3.11 docs |
| **Assembly versioning issues** | Use strong naming; include version in API docs |
| **Users want to extend stdlib** | Document that custom modules go in `lib/` folder, not stdlib |
| **Missing stdlib modules slow adoption** | Prioritize by frequency (sys, math, re, json) |

---

## Summary

**Recommendation**: Adopt **Option A (Pre-Compiled .NET Assemblies)** with the **Phase 1 → Phase 2 roadmap** above.

**Immediate next steps**:
1. Create `StdLibResolver.cs` with module mapping
2. Update `AssemblyEmitter.ModuleEmission.cs` to check stdlib map
3. Implement `from X import Y` code generation for stdlib
4. Add tests
5. Begin expanding stdlib coverage (re, json, random, etc.)

This approach:
- ✅ Aligns with your current architecture
- ✅ Matches how real Python handles compiled modules
- ✅ Scales to full stdlib coverage
- ✅ Maintains version control & distribution simplicity
- ✅ Enables fast, efficient program startup

---

**Questions?** Happy to discuss or adapt based on your constraints!

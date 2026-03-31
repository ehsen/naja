# CPython test_windows Implementation - QUICK START GUIDE

## What You Need to Know

You're implementing support for CPython's Windows-specific OS tests in the Naja project. The goal is to make the test suite executable and passing.

**File being ported:** `F:\Sources\cpython\Lib\test\test_os\test_windows.py` (~700 lines)

**Current status:** Analysis complete, implementation plan created

---

## The Scope (High Level)

### What's Being Tested
6 major test classes covering Windows-specific functionality:

1. **Win32ListdirTests** (✅ Low effort) - Directory listing with extended paths
2. **Win32JunctionTests** (✅ Low effort) - NTFS junctions (_winapi.CreateJunction already works)
3. **Win32SymlinkTests** (⚠️ Medium effort) - Symlinks (privilege-dependent)
4. **Win32ListdriveTests** (🔴 High effort) - Drive/volume enumeration (new APIs)
5. **Win32KillTests** (🔴 Very High effort) - Process signals (needs ctypes)
6. **Win32NtTests** (🔴 High effort) - NT-specific APIs (needs WinAPI wrappers)

### Current Coverage
- ✅ Win32JunctionTests (2/2 tests should pass)
- ✅ Win32ListdirTests (3/3 tests should pass)
- ⚠️ Win32SymlinkTests (Partial - depends on privilege)
- ❌ Win32ListdriveTests (0/3 - new functions needed)
- ❌ Win32KillTests (0/2 - ctypes/msvcrt/mmap needed)
- ❌ Win32NtTests (0/5 - WinAPI wrappers needed)

**After completing Phase 1+2: ~13/17 tests should pass (76%)**

---

## What Needs to Be Implemented

### Phase 1: Critical (8-9 hours) - DO THIS FIRST
These enable basic test execution:

```
├─ signal.SIGTERM, SIGINT, CTRL_C_EVENT, CTRL_BREAK_EVENT constants
├─ subprocess.CREATE_NEW_PROCESS_GROUP constant
├─ os.listdrives(), os.listvolumes(), os.listmounts() functions
├─ Adapt test_windows.py to Naja (remove test.support dependencies)
├─ Create Win32WindowsOsTests.cs test runner
└─ Build & validate
```

### Phase 2: Medium (13-14 hours) - Unlock More Tests
These add significant test coverage:

```
├─ stat module: IO_REPARSE_TAG_* constants
├─ mmap module (for process sync tests)
├─ uuid module (for test tagging)
├─ fnmatch module (for pattern matching)
├─ msvcrt module (for file handle conversion)
├─ Extract test.support decorators into test file
└─ Build & validate
```

### Phase 3: Optional (16+ hours) - Nice to Have
These complete the suite (but very high effort):

```
├─ _winapi extensions (GetCurrentProcess, GetProcessHandleCount)
├─ nt._getfinalpathname() (WinAPI wrapper)
├─ ctypes module stubs (if critical tests fail)
└─ Full ctypes implementation (very large - 50+ hours)
```

---

## Key Documents Created

1. **CPython_test_windows_DEPENDENCY_ANALYSIS.md**
   - Complete import/API breakdown
   - Coverage matrix for each module
   - Critical gaps identified

2. **CPython_test_windows_IMPLEMENTATION_ROADMAP.md**
   - Step-by-step execution plan
   - Phase 1, 2, 3 with prioritization
   - Expected test coverage targets

3. **CPython_test_windows_MODULE_CHECKLIST.md**
   - Detailed checklist for each module
   - Code snippets and P/Invoke declarations
   - Effort estimates per module
   - Implementation notes and decisions

---

## Recommended Execution Path

### Week 1: Phase 1 (Basic Execution)
```
Day 1-2: Create test data file (testdata/windows_os/test_windows.py)
         - Port CPython test_windows.py
         - Replace test.support with inline logic
         - Estimated: 2.5 hours

Day 2: Extend signal module (SIGTERM, etc.)
       - Estimated: 1 hour

Day 2-3: Extend subprocess module (CREATE_NEW_PROCESS_GROUP)
         - Estimated: 0.5 hours

Day 3-4: Extend os module (listdrives, listvolumes, listmounts)
         - Estimated: 3-4 hours

Day 4: Create test runner (Win32WindowsOsTests.cs)
       - Estimated: 1 hour

Day 5: Build, validate, debug
       - Estimated: 2-3 hours
```

**Week 1 Target:** Basic test execution with ~5/17 tests passing

### Week 2: Phase 2 (Unlock More Tests)
```
Day 1: Extend stat module (reparse constants)
       - Estimated: 2 hours

Day 1-2: Implement mmap module
         - Estimated: 3-4 hours

Day 2: Implement uuid module
       - Estimated: 1-2 hours

Day 3: Implement fnmatch module
       - Estimated: 1-2 hours

Day 3: Implement msvcrt module
       - Estimated: 2-3 hours

Day 4-5: Final validation and debugging
         - Estimated: 3-5 hours
```

**Week 2 Target:** ~75% test coverage (13/17 tests passing)

---

## Critical Dependencies Map

```
Win32ListdirTests
  └─ os.listdir ✅ (already works)
  └─ os.fsencode ✅ (already works)

Win32JunctionTests
  └─ _winapi.CreateJunction ✅ (already works)
  └─ os.readlink ✅ (already works)

Win32SymlinkTests
  └─ os.symlink ⚠️ (needs verification)
  └─ os.readlink ✅ (already works)
  └─ Symlink privilege detection ❌ (needs inline check)

Win32ListdriveTests
  └─ os.listdrives ❌ P1 (new)
  └─ os.listvolumes ❌ P1 (new)
  └─ os.listmounts ❌ P1 (new)
  └─ subprocess ✅ (already works)

Win32KillTests
  └─ signal.SIGTERM ❌ P1 (new constant)
  └─ signal.CTRL_C_EVENT ❌ P1 (new constant)
  └─ signal.CTRL_BREAK_EVENT ❌ P1 (new constant)
  └─ subprocess.Popen ✅ (already works)
  └─ subprocess.CREATE_NEW_PROCESS_GROUP ❌ P1 (new constant)
  └─ os.kill ✅ (already works)
  └─ ctypes ❌ P3 (very high effort)
  └─ msvcrt.get_osfhandle ❌ P2 (new)
  └─ mmap ❌ P2 (new module)
  └─ uuid.uuid1 ❌ P2 (new module)

Win32NtTests
  └─ nt._getfinalpathname ❌ P3 (new)
  └─ ctypes ❌ P3 (very high effort)
  └─ _winapi extensions ❌ P3 (new functions)
```

---

## Files to Create/Modify

### Create (New)
```
testdata/windows_os/test_windows.py          (600 LOC - main test file)
Naja.StdLib/NajaMmap.cs                      (200 LOC - mmap module)
Naja.StdLib/NajaUuid.cs                      (100 LOC - uuid module)
Naja.StdLib/NajaFnmatch.cs                   (150 LOC - fnmatch module)
Naja.StdLib/NajaMsvcrt.cs                    (50 LOC - msvcrt stubs)
Naja.StdLib/NajaSignal.cs                    (30 LOC - if new file)
Naja.CodeGen.Tests/LanguageCompliance/Win32WindowsOsTests.cs (50 LOC)
```

### Modify (Existing)
```
Naja.StdLib/NajaOs.cs                        (Add 3 new public methods)
Naja.StdLib/NajaSignal.cs                    (Add 4 constants)
Naja.StdLib/NajaSubprocess.cs                (Add 1 constant)
Naja.StdLib/NajaStat.cs                      (Add 3 constants)
Naja.StdLib/NajaWinapi.cs                    (Optional: add 2 functions)
Naja.CodeGen/StdLibResolver.cs               (Register new modules)
```

---

## Success Criteria

**Phase 1 Success (Week 1):**
- ✅ `testdata/windows_os/test_windows.py` exists and loads without import errors
- ✅ Win32WindowsOsTests.RunWindowsOsTests() executes
- ✅ At least 5/17 tests pass
- ✅ Build succeeds with no errors

**Phase 2 Success (Week 2):**
- ✅ At least 13/17 tests pass
- ✅ mmap, uuid, fnmatch, msvcrt modules functional
- ✅ os.listdrives/volumes/mounts working
- ✅ Build succeeds with no warnings

---

## Known Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|-----------|
| ctypes very large | Blocks Kill/NtTests | Start with stubs; escalate if needed |
| Symlink privilege | Some tests skip | OK - tests will self-skip with skipTest() |
| External tools (fsutil.exe) | May not exist on all systems | Wrap in try-except; tests skip gracefully |
| WinAPI complexity | WinAPI calls can be error-prone | Use P/Invoke carefully; test on real Windows |
| Handle lifecycle | Leaks possible | Ensure cleanup in try/finally |

---

## Quick Reference: Phase 1 Checklist

```
PHASE 1 (Basic Execution - ~8-9 hours)

Test Data File:
  [ ] Create testdata/windows_os/test_windows.py
  [ ] Port CPython test_windows.py
  [ ] Remove test.support imports
  [ ] Replace decorators with inline skipTest()
  [ ] Verify Python syntax

Signal Module:
  [ ] Add SIGTERM = 15
  [ ] Add SIGINT = 2
  [ ] Add CTRL_C_EVENT = 0
  [ ] Add CTRL_BREAK_EVENT = 1
  [ ] Register in StdLibResolver

Subprocess Module:
  [ ] Add CREATE_NEW_PROCESS_GROUP = 0x00000200

OS Module:
  [ ] Implement os.listdrives() → List[str]
  [ ] Implement os.listvolumes() → List[str]
  [ ] Implement os.listmounts(volume) → List[str]
  [ ] Test with simple cases

Test Runner:
  [ ] Create Naja.CodeGen.Tests/LanguageCompliance/Win32WindowsOsTests.cs
  [ ] Implement RunWindowsOsTests() method
  [ ] Add xUnit attributes

Build & Validate:
  [ ] dotnet build succeeds
  [ ] Win32WindowsOsTests runs without fatal errors
  [ ] At least 5/17 tests pass
  [ ] Debug any import errors
```

---

## Questions to Answer Before Starting

1. **Platform Support:** Should all new modules be Windows-only? (Recommend: Yes, with graceful fallbacks)

2. **Error Handling:** Should os.listdrives fail on non-Windows, or return empty list? (Recommend: Throw NotImplementedError or PlatformNotSupportedException)

3. **P/Invoke Risk:** Comfortable writing P/Invoke declarations? (Recommend: Use sparingly; consider subprocess fallbacks for fsutil)

4. **ctypes Priority:** Should ctypes be implemented early or deferred? (Recommend: Defer to Phase 3; stub for Phase 1)

5. **Test Verbosity:** Include console output in tests? (Recommend: Yes, match JsonTests.cs pattern)

---

## Next Steps

1. **Review the three reference documents** (saved as .md files in root)
   - DEPENDENCY_ANALYSIS.md - What needs what
   - IMPLEMENTATION_ROADMAP.md - How to do it
   - MODULE_CHECKLIST.md - Detailed checklist

2. **Start Phase 1:**
   - Begin with test data file creation
   - Follow the weekly breakdown

3. **Iterate & Report:**
   - Build after each step
   - Run tests incrementally
   - Adjust plan if blockers emerge

---

## Contact Points for Decisions

- **ctypes Implementation:** Decide in Phase 1 whether to stub or implement
- **WinAPI Usage:** Review P/Invoke requirements before writing
- **External Tool Fallbacks:** Decide fsutil.exe vs pure WinAPI approach
- **Test Infrastructure:** Decide how much test.support to replicate


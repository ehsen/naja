# CPython test_windows Implementation Roadmap

## Overview
This document defines the complete step-by-step plan to make CPython's `test_os/test_windows.py` 
executable in the Naja project. The test suite comprises 6 major test classes covering Windows-specific 
OS functionality, signal handling, and low-level WinAPI integration.

---

## Quick Reference: What Needs to Be Done

```
PRIORITY 1 (Must Have for Basic Execution):
├─ Create testdata/windows_os/test_windows.py (Python replica)
├─ Add signal module: SIGTERM, CTRL_C_EVENT, CTRL_BREAK_EVENT constants
├─ Extend subprocess: CREATE_NEW_PROCESS_GROUP flag
├─ Extend os module: listdrives(), listvolumes(), listmounts()
├─ Create Win32WindowsOsTests.cs test runner
└─ Validate build & basic test execution

PRIORITY 2 (Medium - Required for Full Test Coverage):
├─ Implement mmap module (memory-mapped IPC)
├─ Implement uuid module (uuid.uuid1 for test tagging)
├─ Implement fnmatch module (pattern matching)
├─ Add msvcrt module stubs (get_osfhandle)
├─ Extend stat module: IO_REPARSE_TAG_* constants
├─ Extend _winapi module: GetCurrentProcess, GetProcessHandleCount
└─ Implement nt._getfinalpathname(path)

PRIORITY 3 (Optional - Nice to Have):
├─ Full ctypes support (very high effort)
├─ Complete test.support decorator extraction
├─ Handle edge cases in symlink privilege detection
└─ Extended error recovery in process tests
```

---

## Detailed Implementation Steps

### STEP 1: Create Python Test Data File (testdata/windows_os/test_windows.py)

**Objective:** Port the CPython test_windows.py to pure Naja Python, removing test.support dependencies.

**Source:** `F:\Sources\cpython\Lib\test\test_os\test_windows.py` (~700 lines)

**Key Changes:**
1. Remove imports from `test.support`, `test.support.import_helper`, `test.support.os_helper`
2. Replace `@support.requires_subprocess()` with manual checks
3. Replace `@os_helper.skip_unless_symlink` with inline privilege detection
4. Replace `os_helper.TESTFN` with `os.path.join(os.getenv("TEMP"), "naja_test_")`
5. Inline helper functions: `sleeping_retry()`, `SHORT_TIMEOUT` → hardcoded 30 seconds
6. Keep external subprocess calls (fsutil.exe, icacls.exe) as-is
7. Simplify or remove tests requiring full ctypes (or stub them)

**Expected Output:**
- ~600 LOC Python file
- 6 test classes: Win32KillTests, Win32ListdirTests, Win32ListdriveTests, Win32SymlinkTests, Win32JunctionTests, Win32NtTests
- Self-contained (all imports from standard Naja stdlib)
- Tests marked with `self.skipTest(reason)` instead of decorators

**Location:** `Naja.CodeGen.Tests\testdata\windows_os\test_windows.py`

---

### STEP 2: Extend signal Module (Naja.StdLib)

**Objective:** Add Windows-specific signal constants.

**Current Status:** `Naja.StdLib\NajaSignal.cs` exists (if present) or needs creation

**Required Additions:**
```csharp
public const int SIGTERM = 15;           // Termination signal
public const int SIGINT = 2;             // Interrupt signal (Ctrl+C)
public const int CTRL_C_EVENT = 0;       // Windows console control event
public const int CTRL_BREAK_EVENT = 1;   // Windows CTRL+BREAK
```

**Implementation:**
- Add constants to signal module
- Ensure `os.kill(pid, sig)` handles these correctly
- Test with dummy subprocess (small validaton test)

**Affected Files:**
- `Naja.StdLib\NajaSignal.cs` (or create if missing)
- `Naja.CodeGen\StdLibResolver.cs` (register module)

---

### STEP 3: Extend subprocess Module (Naja.StdLib)

**Objective:** Add missing Windows process creation flags.

**Current Status:** `Naja.StdLib\NajaSubprocess.cs` exists

**Required Additions:**
```python
CREATE_NEW_PROCESS_GROUP = 0x00000200  # Windows constant
```

**Location:** Integrate into NajaSubprocess or as module-level constant

**Usage:** `subprocess.Popen(..., creationflags=subprocess.CREATE_NEW_PROCESS_GROUP)`

---

### STEP 4: Extend os Module - New Functions

**Objective:** Implement os.listdrives(), os.listvolumes(), os.listmounts() for Windows.

**Current Status:** `Naja.StdLib\NajaOs.cs` exists

**Implementation Details:**

#### 4.1 os.listdrives() → List[str]
- Returns: List of drive letters (`["C:\\", "D:\\", ...]`)
- Implementation: Call WinAPI GetLogicalDrives
- Fallback: Use DriveInfo.GetDrives() from System.IO

#### 4.2 os.listvolumes() → List[str]
- Returns: List of volume GUIDs (`["\\\\?\\Volume{...}\\", ...]`)
- Implementation: Enumerate via WinAPI FindFirstVolumeW/FindNextVolumeW
- Fallback: Parse fsutil output (subprocess call within the function)

#### 4.3 os.listmounts(volume: str) → List[str]
- Returns: List of mount paths for a volume
- Implementation: WinAPI GetVolumePathNamesForVolumeName
- Fallback: Parse fsutil output

**Affected Files:**
- `Naja.StdLib\NajaOs.cs` (add 3 new public methods)
- P/Invoke declarations (kernel32.dll)

---

### STEP 5: Create Test Runner Class (Win32WindowsOsTests.cs)

**Objective:** Mirror JsonTests.cs pattern for Windows OS tests.

**Location:** `Naja.CodeGen.Tests\LanguageCompliance\Win32WindowsOsTests.cs`

**Content:**
```csharp
[Collection("SerialConsole")]
public sealed class Win32WindowsOsTests
{
    private readonly ITestOutputHelper _output;
    private static readonly NajaEngine Engine = new();
    private const string TestFile = "testdata/windows_os/test_windows.py";

    public Win32WindowsOsTests(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Windows OS Tests"), 
     Trait("category", "os-windows"), 
     Trait("phase", "win32")]
    public void RunWindowsOsTests()
    {
        if (!OperatingSystem.IsWindows())
        {
            _output.WriteLine("SKIPPED: Windows-only tests");
            return;
        }

        _output.WriteLine($"Running: {TestFile}");
        try
        {
            Engine.Eval(TestFile);
            _output.WriteLine("✓ All Windows OS tests passed");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Test execution failed: {ex.Message}");
            throw;
        }
    }
}
```

---

### STEP 6: Implement stat Module Extensions

**Objective:** Add Windows reparse point tag constants.

**Current Status:** `Naja.StdLib` may have partial stat support

**Required Constants:**
```python
stat.IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003
stat.IO_REPARSE_TAG_SYMLINK = 0xA000000C
stat.IO_REPARSE_TAG_APPEXECLINK = 0x8000001B  # App Execution Links
```

**Also Needed:**
- `os.stat()` result should include `st_reparse_tag` attribute on Windows

**Affected Files:**
- `Naja.StdLib\NajaStat.cs` (create if missing) or extend existing stat support

---

### STEP 7: Implement mmap Module (Low Priority)

**Objective:** Minimal mmap for process synchronization tests.

**Core API Needed:**
```python
mmap.mmap(fileno, length, flags=0, prot=0, access=ACCESS_WRITE, offset=0, tagname=None)
# For win32: mmap.mmap(-1, size, tagname=str) → named memory-mapped file
```

**Key Methods:**
- `__getitem__(idx)` / `__setitem__(idx, val)`
- `__enter__()` / `__exit__()`
- `resize(newsize)`

**Complexity:** Medium - System.IO.MemoryMappedFiles available in .NET

**Location:** `Naja.StdLib\NajaMmap.cs` (new file)

---

### STEP 8: Implement uuid Module (Low Priority)

**Objective:** Generate unique test identifiers.

**Core API Needed:**
```python
uuid.uuid1() → UUID object (or str representation)
```

**Implementation:**
- `System.Guid.NewGuid()` → format as RFC 4122 UUID v1
- Return object with `.hex` property

**Location:** `Naja.StdLib\NajaUuid.cs` (new file)

---

### STEP 9: Implement fnmatch Module (Low Priority)

**Objective:** Wildcard pattern matching for test result filtering.

**Core API Needed:**
```python
fnmatch.filter(names: List[str], pattern: str) → List[str]
fnmatch.fnmatch(name: str, pattern: str) → bool
```

**Implementation:**
- Use System.IO.Glob or regex translation from fnmatch patterns
- Pattern syntax: `*` = any, `?` = single char, `[abc]` = character class

**Location:** `Naja.StdLib\NajaFnmatch.cs` (new file)

---

### STEP 10: Implement msvcrt Module (Low Priority)

**Objective:** Minimal Windows console/file I/O support.

**Core API Needed:**
```python
msvcrt.get_osfhandle(fd: int) → int  # File descriptor → OS handle
```

**Implementation:**
- P/Invoke to kernel32.dll (already available in System.Runtime.InteropServices)
- Simple wrapper around Windows handle conversion

**Location:** `Naja.StdLib\NajaMsvcrt.cs` (new file)

---

### STEP 11: Extend _winapi Module (Medium Priority)

**Objective:** Add process and handle management functions.

**Current Status:** `Naja.StdLib\NajaWinapi.cs` has CreateJunction

**Required Additions:**
```csharp
// ctypes simulation for handle tests
public static int GetCurrentProcess()  // Returns pseudo-handle
public static int GetProcessHandleCount(int processHandle)  // Returns count
```

**Note:** These may be tricky without full ctypes; stub or evaluate carefully.

---

### STEP 12: Implement nt._getfinalpathname(path: str) (Low Priority)

**Objective:** Resolve final canonicalized path (symlink expansion, junction resolution).

**Implementation:**
- P/Invoke to kernel32.GetFinalPathNameByHandle
- Or use System.IO.FileInfo / DirectoryInfo + symlink resolution

**Location:** New platform-specific nt module or extend NajaOs

---

### STEP 13: Build & Validate

**Objective:** Ensure all changes compile and basic tests run.

**Actions:**
1. Run `dotnet build` → must succeed with no errors
2. Run test runner in xUnit: `Win32WindowsOsTests.RunWindowsOsTests()`
3. Capture output and identify runtime failures
4. Iterate on missing implementations or broken assumptions

---

## Priority Execution Order

```
PHASE 1 (Immediate - Enable Basic Execution):
[ ] 1. Create testdata/windows_os/test_windows.py
[ ] 2. Add signal module constants
[ ] 3. Add subprocess CREATE_NEW_PROCESS_GROUP
[ ] 4. Add os.listdrives/listvolumes/listmounts
[ ] 5. Create Win32WindowsOsTests.cs
[ ] 6. Build & smoke test

PHASE 2 (Short Term - Unlock More Tests):
[ ] 7. Add stat reparse constants
[ ] 8. Implement mmap module
[ ] 9. Implement uuid module
[ ] 10. Implement fnmatch module
[ ] 11. Implement msvcrt module
[ ] 12. Build & validate

PHASE 3 (Medium Term - Optional Enhancements):
[ ] 13. Extend _winapi (GetCurrentProcess, etc.)
[ ] 14. Implement nt._getfinalpathname
[ ] 15. Full ctypes stubs (if critical test demands)
```

---

## Expected Test Coverage After Implementation

| Test Class | Phase 1 | Phase 2 | Notes |
|-----------|---------|---------|-------|
| Win32ListdirTests | ✅ 3/3 | ✅ 3/3 | Fully supported |
| Win32JunctionTests | ✅ 2/2 | ✅ 2/2 | CreateJunction already works |
| Win32SymlinkTests | ⚠️ Partial | ⚠️ Partial | Privilege-dependent |
| Win32ListdriveTests | ❌ 0/3 | ✅ 3/3 | After listdrives/volumes/mounts |
| Win32KillTests | ❌ 0/2 | ⚠️ 1/2 | Requires full ctypes or stubs |
| Win32NtTests | ❌ 0/5 | ⚠️ 2/5 | _getfinalpathname blocking |
| **TOTAL** | **5/17** | **13/17** | **76% coverage Phase 2** |

---

## Risk Mitigation

**Risk:** ctypes module (required by Kill/NtTests) is very large
- **Mitigation:** Start with stubs; only implement if tests demand it

**Risk:** Process privilege errors (symlink on older Windows)
- **Mitigation:** Gracefully skip tests that fail due to privilege

**Risk:** External tools (fsutil.exe, icacls.exe) may not exist
- **Mitigation:** Wrap in try-except; skip tests if tools unavailable

**Risk:** File handles exhaustion in handle tracking tests
- **Mitigation:** Ensure proper cleanup in test setup/teardown

---

## Success Criteria

1. ✅ `testdata/windows_os/test_windows.py` exists and loads without import errors
2. ✅ `Win32WindowsOsTests.RunWindowsOsTests()` executes (may have test failures, that's OK)
3. ✅ At least 50% of tests pass (>8/17) in Phase 1
4. ✅ At least 75% of tests pass (>13/17) after Phase 2
5. ✅ No blocking compilation errors in Naja.StdLib
6. ✅ All changes are Windows-safe (no crashes on non-Windows platforms)


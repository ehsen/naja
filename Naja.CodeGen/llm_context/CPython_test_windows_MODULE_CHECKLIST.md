# Module Implementation Checklist - CPython test_windows Dependencies

This checklist tracks every module, function, and constant needed for complete test_windows support.

---

## 1. SIGNAL MODULE

**Status:** Needs Extension  
**File:** `Naja.StdLib\NajaSignal.cs` (create if missing)

### Constants Required
- [ ] `SIGTERM = 15` - Termination signal
- [ ] `SIGINT = 2` - Keyboard interrupt
- [ ] `CTRL_C_EVENT = 0` - Windows Ctrl+C event
- [ ] `CTRL_BREAK_EVENT = 1` - Windows Ctrl+Break event

### Module Registration
- [ ] Register in `Naja.CodeGen\StdLibResolver.cs`
- [ ] Add import hook: `import signal`

### Tests Requiring This
- `Win32KillTests.test_kill_sigterm()`
- `Win32KillTests.test_kill_int()`
- `Win32KillTests.test_CTRL_C_EVENT()` (skipped)
- `Win32KillTests.test_CTRL_BREAK_EVENT()`

**Estimated Effort:** 1 hour

---

## 2. SUBPROCESS MODULE EXTENSION

**Status:** Partial (needs flag)  
**File:** `Naja.StdLib\NajaSubprocess.cs`

### Constants Required
- [ ] `CREATE_NEW_PROCESS_GROUP = 0x00000200`
- [ ] `PIPE` (may already exist)
- [ ] `TimeoutExpired` exception (may already exist)

### Methods/Properties Needed (Verify Existing)
- [ ] `Popen(..., creationflags=...)`
- [ ] `proc.poll()` - Check if still running
- [ ] `proc.wait(timeout=...)`
- [ ] `proc.communicate()`
- [ ] `proc.stdout`, `proc.stderr`, `proc.stdin`
- [ ] `proc.pid`
- [ ] `proc.kill()`

### Tests Requiring This
- All `Win32KillTests` methods
- Win32ListdriveTests (subprocess.check_output for fsutil.exe)
- Win32NtTests (subprocess.Popen for handle tests)

**Estimated Effort:** 30 minutes (mostly verification)

---

## 3. OS MODULE EXTENSIONS

**Status:** Needs Major Extension  
**File:** `Naja.StdLib\NajaOs.cs`

### New Functions Required

#### 3.1 os.listdrives() → List[str]
- [ ] Implementation using DriveInfo.GetDrives()
- [ ] Return format: ["C:\\", "D:\\", ...] or ["C:", "D:", ...]
- [ ] Handle UNC paths if applicable
- [ ] Test case: Returns non-empty list on Windows

#### 3.2 os.listvolumes() → List[str]
- [ ] Implementation using WinAPI FindFirstVolumeW/FindNextVolumeW
- [ ] Return format: ["\\\\?\\Volume{GUID}\\", ...]
- [ ] OR fallback to fsutil.exe subprocess call
- [ ] Test case: Returns list of volume GUIDs

#### 3.3 os.listmounts(volume: str) → List[str]
- [ ] Implementation using WinAPI GetVolumePathNamesForVolumeName
- [ ] Return format: ["C:\\", "D:\\mount\\", ...]
- [ ] OR fallback to fsutil.exe subprocess call
- [ ] Handle NotImplementedError gracefully
- [ ] Test case: Returns mount points for a volume

### Existing Functions to Verify/Extend
- [ ] `os.stat(path)` returns object with `st_reparse_tag` on Windows
- [ ] `os.lstat(path)` - currently implemented?
- [ ] `os.readlink(path)` - currently implemented?
- [ ] `os.path.islink(path)` - currently implemented?
- [ ] `os.symlink(src, dst, target_is_dir=False)` - currently implemented?
- [ ] `os.getcwd()` - currently implemented?
- [ ] `os.chdir(path)` - currently implemented?

### P/Invoke Declarations (if using WinAPI)
```csharp
[DllImport("kernel32.dll", SetLastError = true)]
private static extern bool GetLogicalDrives();

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern IntPtr FindFirstVolumeW(
    StringBuilder lpszVolumeName, uint cchBufferLength);

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern bool FindNextVolumeW(
    IntPtr hFindVolume, StringBuilder lpszVolumeName, uint cchBufferLength);

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern bool FindVolumeClose(IntPtr hFindVolume);

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern bool GetVolumePathNamesForVolumeNameW(
    string lpszVolumeName, char[] lpszVolumePathNames, 
    uint cchBufferLength, out uint lpcchReturnLength);
```

### Tests Requiring This
- `Win32ListdriveTests.test_listdrives()`
- `Win32ListdriveTests.test_listvolumes()`
- `Win32ListdriveTests.test_listmounts()`

**Estimated Effort:** 3-4 hours (includes WinAPI research)

---

## 4. STAT MODULE EXTENSIONS

**Status:** Needs Extension  
**File:** `Naja.StdLib\NajaStat.cs` or extend existing

### Constants Required
- [ ] `S_ISLNK(mode)` function - Check if symlink
- [ ] `IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003`
- [ ] `IO_REPARSE_TAG_SYMLINK = 0xA000000C`
- [ ] `IO_REPARSE_TAG_APPEXECLINK = 0x8000001B`

### os.stat() Result Extension (Windows-only)
- [ ] `st_reparse_tag` attribute on stat_result
- [ ] Should be 0 for normal files
- [ ] Should be IO_REPARSE_TAG_* for reparse points

### Tests Requiring This
- `Win32SymlinkTests.test_appexeclink()` (check st_reparse_tag)
- `Win32JunctionTests.test_create_junction()` (indirectly)
- `Win32NtTests` (edge cases with symlinks)

**Estimated Effort:** 2 hours

---

## 5. MMAP MODULE

**Status:** Not Implemented  
**File:** `Naja.StdLib\NajaMmap.cs` (new)

### Core API Required
```python
class mmap:
    def __init__(self, fileno: int, length: int, 
                 flags: int = 0, prot: int = 0, 
                 access: int = 1, offset: int = 0, 
                 tagname: str = None):
        # Windows: mmap(-1, size, tagname="foo") → named shared memory

    def __getitem__(self, index: int) -> int: ...
    def __setitem__(self, index: int, value: int) -> None: ...
    def __len__(self) -> int: ...
    def __enter__(self): ...
    def __exit__(self, *args): ...
    def close(self) -> None: ...
    def resize(self, newsize: int) -> None: ...
```

### Implementation Using
- [ ] `System.IO.MemoryMappedFiles.MemoryMappedFile`
- [ ] Named file support for IPC tests

### Tests Requiring This
- `Win32KillTests._kill_with_event()` (mmap for process sync)
- Actually only 1 test uses mmap; could be optional

**Estimated Effort:** 3-4 hours

---

## 6. UUID MODULE

**Status:** Not Implemented  
**File:** `Naja.StdLib\NajaUuid.cs` (new)

### Core API Required
```python
class UUID:
    def __init__(self, hex: str = None, ...): ...
    @property
    def hex(self) -> str: ...
    def __str__(self) -> str: ...

def uuid1(node: int = None, clock_seq: int = None) -> UUID: ...
```

### Implementation Using
- [ ] `System.Guid.NewGuid()` - v4 (random) UUID
- [ ] Format as RFC 4122 v1 or return v4 (tests only check format)

### Tests Requiring This
- `Win32KillTests._kill_with_event()` (tagname = "test_os_" + uuid.uuid1())
- Only 1 test; minimal functionality OK

**Estimated Effort:** 1-2 hours

---

## 7. FNMATCH MODULE

**Status:** Not Implemented  
**File:** `Naja.StdLib\NajaFnmatch.cs` (new)

### Core API Required
```python
def fnmatch(name: str, pattern: str) -> bool: ...
def filter(names: Iterable[str], pattern: str) -> List[str]: ...
```

### Pattern Syntax
- `*` - Match any sequence
- `?` - Match single char
- `[abc]` - Character class
- `[!abc]` - Negated class

### Tests Requiring This
- `Win32SymlinkTests.test_appexeclink()` (fnmatch.filter for exe filtering)
- Only 1 test; could manually filter instead

**Estimated Effort:** 1-2 hours

---

## 8. MSVCRT MODULE

**Status:** Not Implemented  
**File:** `Naja.StdLib\NajaMsvcrt.cs` (new)

### Core API Required
```python
def get_osfhandle(fd: int) -> int:
    """Convert file descriptor to OS handle."""
    # fd → HANDLE (on Windows)
```

### Implementation
- [ ] P/Invoke to kernel32.dll (if needed) OR
- [ ] Direct .NET System.IO handle conversion

### Tests Requiring This
- `Win32KillTests._kill()` (msvcrt.get_osfhandle for pipe inspection)
- Requires ctypes PeekNamedPipe call (very advanced)

**Estimated Effort:** 2-3 hours

---

## 9. CTYPES MODULE (OPTIONAL/ADVANCED)

**Status:** Not Implemented  
**File:** `Naja.StdLib\NajaCtypes.cs` (new, very large)

### WARNING: VERY HIGH EFFORT (50+ hours)

### Core Classes/Functions Needed (by test)

#### For Win32KillTests._kill():
```python
ctypes.windll.kernel32.PeekNamedPipe
ctypes.wintypes.BOOL, HANDLE, DWORD, LPDWORD
ctypes.POINTER(ctypes.c_char)
ctypes.POINTER(ctypes.wintypes.DWORD)
ctypes.create_string_buffer(size)
ctypes.sizeof(buffer)
```

#### For Win32NtTests.test_getfinalpathname_handles():
```python
ctypes.WinDLL('Kernel32.dll', use_last_error=True)
kernel.GetCurrentProcess.restype = ctypes.wintypes.HANDLE
kernel.GetProcessHandleCount.argtypes = (HANDLE, LPDWORD)
ctypes.byref(value)
ctypes.wintypes.DWORD
```

### Scope Analysis
- FFI framework → function pointers, P/Invoke mapping
- Type system → c_int, c_char, DWORD, HANDLE, POINTER, etc.
- Windows DLL loading and method binding
- Argument marshalling and return value conversion

### Decision Point
- **Option A:** Stub everything (10 hours) → Tests marked SKIP
- **Option B:** Implement select functions (20-30 hours) → Partial support
- **Option C:** Full implementation (50+ hours) → Complete support

**Recommendation:** Start with Option A (stubs); escalate if tests demand

---

## 10. _WINAPI MODULE EXTENSIONS

**Status:** Partial (CreateJunction exists)  
**File:** `Naja.StdLib\NajaWinapi.cs`

### Existing
- [ ] `_winapi.CreateJunction(source, junction)` ✅

### Required Additions
- [ ] `_winapi.GetCurrentProcess()` → pseudo-handle (-1 on Windows)
- [ ] `_winapi.GetProcessHandleCount(handle)` → count of open handles
- [ ] Possibly others (evaluate per test failure)

### Implementation
- [ ] P/Invoke to kernel32.dll functions
- [ ] Return values matching Windows behavior

### Tests Requiring This
- `Win32NtTests.test_getfinalpathname_handles()` (handle count tracking)

**Estimated Effort:** 2-3 hours

---

## 11. NT MODULE (PLATFORM-SPECIFIC)

**Status:** Not Implemented  
**File:** `Naja.StdLib\NajaNt.cs` (new, Windows-only) OR extend NajaOs

### Core API Required
```python
def _getfinalpathname(path: str) -> str:
    """Get final canonicalized path (resolves symlinks)."""
    # Use WinAPI GetFinalPathNameByHandle or equivalent
```

### Implementation Strategy
- [ ] Option A: P/Invoke to kernel32.GetFinalPathNameByHandle
- [ ] Option B: Use .NET DirectoryInfo/FileInfo + symlink resolution
- [ ] Option C: Return input path (stub) → test may fail

### Tests Requiring This
- `Win32NtTests.test_getfinalpathname_handles()` (verify no handle leaks)

**Estimated Effort:** 3-4 hours

---

## 12. TEST INFRASTRUCTURE (MANUAL WORKAROUNDS)

**Status:** Not Available (test.support, test.os.utils)  
**File:** `testdata/windows_os/test_windows.py`

### Replacements Needed in Test File

#### 12.1 Replace @support.requires_subprocess()
```python
# Before:
@support.requires_subprocess()
def test_CTRL_C_EVENT(self):
    ...

# After (inline check):
def test_CTRL_C_EVENT(self):
    if not hasattr(subprocess, 'Popen'):
        self.skipTest("subprocess not available")
    ...
```

#### 12.2 Replace @os_helper.skip_unless_symlink
```python
# Before:
@os_helper.skip_unless_symlink
def test_directory_link(self):
    ...

# After (inline check):
def test_directory_link(self):
    try:
        os.symlink(target, link)
    except (OSError, NotImplementedError) as e:
        self.skipTest(f"Symlink not available: {e}")
        return
    ...
```

#### 12.3 Replace os_helper.TESTFN
```python
# Before:
def setUp(self):
    self.test_dir = os_helper.TESTFN

# After:
def setUp(self):
    temp = os.getenv("TEMP") or os.getenv("TMP") or "C:\\Temp"
    self.test_dir = os.path.join(temp, f"naja_test_{os.getpid()}_{uuid.uuid1()}")
```

#### 12.4 Replace support.SHORT_TIMEOUT
```python
# Before:
proc.wait(timeout=support.SHORT_TIMEOUT)

# After:
proc.wait(timeout=30)  # Hardcoded 30 seconds
```

#### 12.5 Replace support.sleeping_retry()
```python
# Before:
for _ in support.sleeping_retry(support.SHORT_TIMEOUT):
    if proc.poll() is None:
        break

# After:
deadline = time.time() + 30
while time.time() < deadline:
    if proc.poll() is None:
        break
    time.sleep(0.1)
```

#### 12.6 Replace support.verbose
```python
# Before:
if support.verbose:
    print(...)

# After:
# Just remove or comment out verbose output
```

**Estimated Effort:** 2-3 hours (code migration)

---

## Summary by Implementation Phase

### PHASE 1 (Critical - Enables Execution)
| Item | Effort | Status | Priority |
|------|--------|--------|----------|
| signal constants | 1h | [ ] | P0 |
| subprocess CREATE_NEW_PROCESS_GROUP | 0.5h | [ ] | P0 |
| os.listdrives/listvolumes/listmounts | 3.5h | [ ] | P1 |
| Test data file (test_windows.py) | 2.5h | [ ] | P0 |
| Test runner (Win32WindowsOsTests.cs) | 1h | [ ] | P0 |
| **PHASE 1 TOTAL** | **8.5h** | | |

### PHASE 2 (Medium - Unlock More Tests)
| Item | Effort | Status | Priority |
|------|--------|--------|----------|
| stat constants (reparse tags) | 2h | [ ] | P1 |
| mmap module | 3.5h | [ ] | P2 |
| uuid module | 1.5h | [ ] | P2 |
| fnmatch module | 1.5h | [ ] | P2 |
| msvcrt module | 2.5h | [ ] | P2 |
| Test infrastructure workarounds | 2.5h | [ ] | P1 |
| **PHASE 2 TOTAL** | **13.5h** | | |

### PHASE 3 (Optional - Nice to Have)
| Item | Effort | Status | Priority |
|------|--------|--------|----------|
| _winapi extensions | 2.5h | [ ] | P3 |
| nt._getfinalpathname | 3.5h | [ ] | P3 |
| ctypes module (minimal stubs) | 10h | [ ] | P3 |
| ctypes module (full) | 50h | [ ] | P4 |
| **PHASE 3 TOTAL** | **16-66h** | | |

---

## Grand Total Estimate
- **Phase 1:** ~8-9 hours → Basic execution
- **Phase 2:** ~13-14 hours → ~75% test coverage
- **Phase 3:** ~16 hours minimum (up to 66+ for full ctypes)

**Recommended Target:** Complete Phase 1 + Phase 2 (24 hours) = 75% test coverage


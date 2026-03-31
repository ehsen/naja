# CPython test_windows.py Complete Dependency Analysis

**File analyzed:** `F:\Sources\cpython\Lib\test\test_os\test_windows.py` (~700 lines)
**Scope:** Windows-only OS tests requiring platform-specific modules and APIs

---

## Module Import Requirements

### Standard Library Modules (Already Available in Naja)
- `sys` ✅ (exists in Naja.StdLib)
- `os` ✅ (exists in Naja.StdLib)
- `unittest` ✅ (exists in Naja.StdLib)
- `subprocess` ✅ (partial - needs CREATE_NEW_PROCESS_GROUP flag)
- `shutil` ✅ (exists - used for rmtree)
- `stat` ✅ (exists in Naja.StdLib)
- `time` ✅ (exists in Naja.StdLib)
- `textwrap` ✅ (basic - used for dedent)

### Standard Library Modules (Missing/Partial)
- `signal` ❌ (needs SIGTERM, SIGINT, CTRL_C_EVENT, CTRL_BREAK_EVENT constants)
- `mmap` ❌ (needed for process synchronization in kill tests)
- `uuid` ❌ (uuid.uuid1() for tag generation)
- `fnmatch` ❌ (fnmatch.filter() for wildcard matching)
- `ctypes` ❌ (windll, wintypes, c_char, POINTER, etc. - extensive)
- `msvcrt` ❌ (msvcrt.get_osfhandle() for file descriptor conversion)

### CPython Test Infrastructure (Not Available)
- `test.support` ❌ (sleeping_retry, SHORT_TIMEOUT, requires_subprocess, verbose)
- `test.support.import_helper` ❌ (import_module)
- `test.support.os_helper` ❌ (TESTFN, skip_unless_symlink, rmtree, unlink)
- `test.os.utils` ❌ (create_file helper)

### Windows-Specific Modules (Partially Available)
- `_winapi` ⚠️ (CreateJunction exists, but missing many functions)

---

## Test Classes and Core Functionality

### 1. Win32KillTests
**Purpose:** Test os.kill() with signal delivery on Windows
**Key APIs:**
- `subprocess.Popen(..., stdout=PIPE, stderr=PIPE, stdin=PIPE)`
- `subprocess.TimeoutExpired` exception
- `os.kill(pid, sig)` 
- `proc.poll()`, `proc.wait()`, `proc.communicate()`
- Signal constants: `signal.SIGTERM`, `signal.CTRL_C_EVENT`, `signal.CTRL_BREAK_EVENT`
- `ctypes.windll.kernel32.PeekNamedPipe()` - P/Invoke to Windows DLLs
- `ctypes.create_string_buffer()`, `ctypes.sizeof()`
- `msvcrt.get_osfhandle()` - WinAPI file handle conversion
- Test support: `support.SHORT_TIMEOUT`, `support.requires_subprocess()`

**Gap:** Requires full ctypes module, signal constants, msvcrt integration

### 2. Win32ListdirTests
**Purpose:** Test os.listdir with normal and extended paths
**Key APIs:**
- `os.listdir(path)` - already supported
- `os.fsencode(path)` - byte path conversion
- Extended path syntax: `\\?\C:\path\...`
- `os_helper.TESTFN`, `os_helper.rmtree()`

**Gap:** Test infrastructure (TESTFN, rmtree from test.support)

### 3. Win32ListdriveTests
**Purpose:** Test os.listdrives(), os.listvolumes(), os.listmounts()
**Key APIs:**
- `os.listdrives()` ❌ (not in Naja)
- `os.listvolumes()` ❌ (not in Naja)
- `os.listmounts(volume)` ❌ (not in Naja)
- `subprocess.check_output()` - run fsutil.exe
- `support.verbose` - test verbose mode

**Gap:** Entire test class is new feature APIs; needs os module extensions

### 4. Win32SymlinkTests
**Purpose:** Test os.symlink(), os.readlink(), os.lstat(), symlink privilege detection
**Key APIs:**
- `os.symlink(src, dest)` ⚠️ (exists but may need refinement)
- `os.readlink(path)` ✅
- `os.path.islink(path)` ✅
- `os.lstat(path)` ✅
- `os.path.lexists(path)` ✅
- `os.path.relpath(path)` ✅
- `os.getcwd()`, `os.chdir(path)`
- `stat.S_ISLNK(mode)` - check if symlink
- `stat.IO_REPARSE_TAG_APPEXECLINK` - reparse point constant
- `os_helper.skip_unless_symlink` - decorator to skip if no symlink privilege

**Gap:** Symlink privilege detection, reparse tag constants, decorator/test infrastructure

### 5. Win32JunctionTests
**Purpose:** Test _winapi.CreateJunction() junction creation
**Key APIs:**
- `_winapi.CreateJunction(target, junction)` ✅ (already implemented)
- `os.path.lexists(path)`, `os.path.exists(path)` ✅
- `os.path.isdir(path)` ✅
- `os.stat()`, `os.lstat()` ✅
- `os.readlink()` ✅
- `os.path.islink()` ✅
- `os.unlink()` ✅
- `os.path.normcase()` ✅

**Gap:** Minor - infrastructure only (test helpers)

### 6. Win32NtTests
**Purpose:** Test nt module functions (_getfinalpathname), file handle lifecycle, symlink edge cases
**Key APIs:**
- `nt._getfinalpathname(path)` ❌ (not in Naja, platform-specific)
- `os.stat(path)` ✅
- `import_helper.import_module('nt')` ❌ (test infrastructure)
- `ctypes` library (full) ❌
- `subprocess.Popen(...)` ✅
- `support.requires_subprocess()` ❌
- `support.SHORT_TIMEOUT` ❌
- `support.verbose` ❌
- `ICACLS.exe` execution - external tool

**Gap:** nt module functions, ctypes, test.support utilities

---

## Dependency Coverage Matrix

| Category | Module/API | Status | Effort | Notes |
|----------|-----------|--------|--------|-------|
| **Core Modules** | sys | ✅ | Done | |
| | os | ⚠️ | High | Missing listdrives, listvolumes, listmounts |
| | unittest | ✅ | Done | |
| | subprocess | ⚠️ | Low | Need CREATE_NEW_PROCESS_GROUP |
| | signal | ❌ | Medium | Need 4 constants + module skeleton |
| | stat | ✅ | Done | Missing IO_REPARSE_TAG_APPEXECLINK |
| | time | ✅ | Done | |
| | textwrap | ✅ | Done | |
| | shutil | ✅ | Done | |
| **Windows APIs** | _winapi | ⚠️ | High | CreateJunction exists; need other functions |
| | msvcrt | ❌ | Medium | get_osfhandle(), likely stubs |
| | mmap | ❌ | Medium | Memory-mapped I/O |
| | uuid | ❌ | Low | uuid1() for test tagging |
| | fnmatch | ❌ | Low | Simple pattern matching |
| | ctypes | ❌ | Very High | Full FFI library - huge scope |
| **Test Support** | test.support | ❌ | Very High | Decorator-heavy, many helpers |
| | test.support.os_helper | ❌ | Medium | TESTFN, skip decorators |
| | nt module | ❌ | Medium | _getfinalpathname(), etc |

---

## Critical Gaps Blocking Full test_windows Execution

### 1. **ctypes Module (Blocking 3/6 test classes)**
   - Required by: Win32KillTests, Win32NtTests (heavy)
   - Scope: ctypes.windll, ctypes.wintypes, ctypes.POINTER, ctypes.c_char, ctypes.create_string_buffer, ctypes.sizeof, ctypes.byref
   - Complexity: **Very High** - FFI framework needed
   - Workaround: Stub most ctypes; extract P/Invoke to Naja stdlib

### 2. **test.support Infrastructure (Blocking all classes)**
   - Required by: All 6 test classes
   - Scope: support.SHORT_TIMEOUT, support.verbose, support.requires_subprocess, decorators
   - Complexity: **High** - decorator pattern required
   - Workaround: Inline or remove test decorators; use manual skip checks

### 3. **New os Module Functions**
   - `os.listdrives()`, `os.listvolumes()`, `os.listmounts()` for Win32ListdriveTests
   - Complexity: **High** - Requires Windows API calls
   - Workaround: Implement stubbed versions that fail gracefully

### 4. **Signal Module Constants**
   - `signal.SIGTERM`, `signal.CTRL_C_EVENT`, `signal.CTRL_BREAK_EVENT`
   - Complexity: **Low**
   - Workaround: Add to Naja.StdLib.NajaSignal

### 5. **nt Module (_getfinalpathname)**
   - Blocking: Win32NtTests advanced tests
   - Complexity: **Medium** - Direct WinAPI call
   - Workaround: Implement as P/Invoke wrapper

---

## Implementation Strategy

### Phase 1: Foundational (Test Infrastructure)
1. Create Python replica test file at `testdata/windows_os/test_windows.py`
   - Reuse CPython test_windows.py structure
   - Replace test.support imports with manual skip logic
   - Remove or inline decorators

2. Add missing signal constants to Naja signal module
   - SIGTERM, SIGINT, CTRL_C_EVENT, CTRL_BREAK_EVENT

3. Extend subprocess module with missing flags
   - CREATE_NEW_PROCESS_GROUP

### Phase 2: Core API Extensions (os module)
1. Add os.listdrives() - query drive letters
2. Add os.listvolumes() - query volume GUIDs
3. Add os.listmounts() - query mount points
4. Extend stat module with IO_REPARSE_TAG_* constants

### Phase 3: Windows Module Support
1. Implement minimal msvcrt stubs
   - get_osfhandle(fd) → int
2. Implement minimal uuid stubs
   - uuid1() → str
3. Implement minimal fnmatch
   - filter(names, pattern) → list
4. Implement minimal mmap
   - mmap(-1, size, tagname) for IPC

### Phase 4: Advanced (Optional, Lower Priority)
1. Extend _winapi module (beyond CreateJunction)
   - GetCurrentProcess, GetProcessHandleCount for handle tracking
   - (Evaluate scope with FFI requirements)

2. Implement nt._getfinalpathname(path)
   - P/Invoke to Windows GetFinalPathNameByHandle

3. Minimal ctypes stubs (if critical tests demand)
   - ctypes.windll.kernel32 reflection
   - Basic type system for argument passing

4. Extract test.support decorators
   - @requires_subprocess → manual check
   - @skip_unless_symlink → privilege detection
   - @skip(reason) → internal logic

---

## Phased Test Execution Plan

**Phase 1 (Immediate):**
- Win32JunctionTests: 2/2 tests should pass (CreateJunction already exists)
- Win32SymlinkTests: Partial (4/8 tests if symlink available)
- Win32ListdirTests: Full (3/3 tests - all os.listdir)

**Phase 2 (After os extensions):**
- Win32ListdriveTests: New tests for listdrives/volumes/mounts

**Phase 3 (Stretch):**
- Win32KillTests: Requires ctypes (stubs or full implementation)
- Win32NtTests: Requires nt._getfinalpathname (WinAPI wrapper)

---

## Known Constraints

1. **ctypes Complexity:** Full implementation ~500–1000 LOC; consider stubbing for now
2. **External Tools:** Some tests call icacls.exe, fsutil.exe — OK as external process
3. **Privilege Requirements:** Symlink tests require admin/developer mode on modern Windows
4. **Platform Exclusivity:** All tests skip on non-Windows; safe to build into Naja
5. **CPython test.support:** No direct porting; use inline logic instead


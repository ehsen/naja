# CPython test_windows.py - Real Gap Analysis Strategy

**Goal:** Run actual CPython test_windows.py against Naja to identify real failures, not work with stubs.

**Status:** ANALYSIS PHASE

---

## Critical Dependencies Found in CPython test_windows.py

### Infrastructure Imports (test.support)
```python
from test import support
from test.support import import_helper
from test.support import os_helper
from .utils import create_file
```

**What this means:**
- CPython tests depend on `test.support` module
- `os_helper.TESTFN` - managed temp directory
- `os_helper.skip_unless_symlink` - decorator checking for admin/developer mode
- `support.verbose` - reporting flag
- `support.requires_subprocess()` - decorator
- `support.SHORT_TIMEOUT` - constant
- `support.sleeping_retry()` - utility function

### Standard Modules Required
```python
import _winapi          # ✅ We have this
import fnmatch         # ✅ Phase 2
import mmap            # ✅ Phase 2
import os              # ✅ Core
import shutil          # ❓ Not checked yet
import signal          # ✅ Phase 1
import stat            # ✅ Phase 2
import subprocess      # ✅ Phase 1
import textwrap        # ❓ Not checked yet
import time            # ✅ Core
import uuid            # ✅ Phase 2
```

### Advanced Features Required
```python
# ctypes for Windows API access (complex)
import ctypes
from ctypes import wintypes
import msvcrt           # ✅ Phase 2 (get_osfhandle only)

# P/Invoke patterns
ctypes.windll.kernel32.PeekNamedPipe
ctypes.windll.kernel32.SetConsoleCtrlHandler
ctypes.wintypes.DWORD, HANDLE, BOOL, LPDWORD
ctypes.POINTER, c_char, c_int
ctypes.create_string_buffer
ctypes.byref
```

---

## Test Classes in CPython test_windows.py

### 1. Win32KillTests (3 test methods)
- **Purpose:** Test os.kill with signal handling
- **Infrastructure:** ctypes, msvcrt.get_osfhandle(), subprocess with pipes
- **Complexity:** ⭐⭐⭐⭐⭐ (uses advanced ctypes Windows API)
- **Status:** BLOCKED - requires ctypes implementation

**Tests:**
1. `test_kill_sigterm()` - os.kill with SIGTERM signal
2. `test_kill_int()` - os.kill with exit code as int
3. `test_CTRL_BREAK_EVENT()` - console control events
4. `test_CTRL_C_EVENT()` - (skipped, subprocesses issue)

### 2. Win32ListdirTests (2 test methods)
- **Purpose:** Test os.listdir with Unicode and extended paths
- **Infrastructure:** os_helper.TESTFN, shutil.rmtree
- **Complexity:** ⭐⭐ (basic filesystem)
- **Status:** LIKELY READY

**Tests:**
1. `test_listdir_no_extended_path()` - Normal paths (Unicode and bytes)
2. `test_listdir_extended_path()` - Extended \\?\ paths (Unicode and bytes)

**Key Assertion:**
```python
# Must handle both Unicode and bytes paths
os.listdir(path)  # str
os.listdir(os.fsencode(path))  # bytes
```

### 3. Win32ListdriveTests (3 test methods)
- **Purpose:** Test os.listdrives/listvolumes/listmounts
- **Infrastructure:** subprocess fsutil.exe query
- **Complexity:** ⭐⭐⭐ (subprocess interaction)
- **Status:** DEPENDS - if os.listdrives/listvolumes/listmounts implemented

**Tests:**
1. `test_listdrives()` - os.listdrives() returns drive list
2. `test_listvolumes()` - os.listvolumes() returns volume list
3. `test_listmounts()` - os.listmounts(volume) returns mount points

**Key Issue:** Uses subprocess to query fsutil for validation data

### 4. Win32SymlinkTests (7 test methods)
- **Purpose:** Test os.symlink / os.readlink / os.lstat
- **Infrastructure:** os_helper.skip_unless_symlink (requires admin/dev mode)
- **Complexity:** ⭐⭐⭐ (privilege checking, relative paths)
- **Status:** DEPENDS - on os.symlink implementation & privileges

**Tests:**
1. `test_directory_link()` - Create symlink to directory
2. `test_file_link()` - Create symlink to file
3. `test_remove_directory_link_to_missing_target()` - Broken dir link removal
4. `test_isdir_on_directory_link_to_missing_target()` - Broken link detection
5. `test_rmdir_on_directory_link_to_missing_target()` - Broken link rmdir
6. `test_12084()` - Relative symlinks across directories
7. `test_29248()` - All Users → ProgramData junction
8. `test_buffer_overflow()` - Long path handling
9. `test_appexeclink()` - Windows app execution aliases (APPEXECLINK reparse tag)

### 5. Win32JunctionTests (2 test methods)
- **Purpose:** Test _winapi.CreateJunction
- **Infrastructure:** Direct _winapi calls
- **Complexity:** ⭐⭐ (basic winapi)
- **Status:** LIKELY READY (we implemented CreateJunction)

**Tests:**
1. `test_create_junction()` - _winapi.CreateJunction creates junction
2. `test_unlink_removes_junction()` - os.unlink removes junction

### 6. Win32NtTests (2 test methods)
- **Purpose:** Test nt module and ctypes integration
- **Infrastructure:** ctypes, Kernel32.dll API, advanced Windows calls
- **Complexity:** ⭐⭐⭐⭐ (ctypes Windows API)
- **Status:** BLOCKED - requires ctypes

**Tests:**
1. `test_getfinalpathname_handles()` - nt._getfinalpathname, handle leak checking
2. `test_stat_unlink_race()` - race condition handling

---

## Blockers and Missing Infrastructure

### CRITICAL BLOCKERS
1. **test.support module** - Not available in Naja
   - `os_helper.TESTFN` - Managed temp directory path
   - `os_helper.skip_unless_symlink` - Requires decorator support
   - `os_helper.rmtree` - Remove directory tree
   - `support.verbose` - Reporting flag
   - `support.SHORT_TIMEOUT` - Test timeout constant
   - `support.sleeping_retry()` - Retry utility
   - `support.requires_subprocess()` - Decorator

2. **ctypes module** - Not in Phase 2/3 roadmap
   - All Win32KillTests blocked
   - test_getfinalpathname_handles blocked
   - Complex Windows API access via ctypes.windll

3. **Missing stdlib modules**
   - `shutil` - Directory/file operations (used by ListdirTests)
   - `textwrap` - Text formatting (imported but maybe not used directly)

### MODERATE BLOCKERS
4. **os module gaps**
   - `os.listdrives()` - Not yet implemented
   - `os.listvolumes()` - Not yet implemented
   - `os.listmounts(volume)` - Not yet implemented
   - `os.symlink()` - Not yet implemented
   - `os._getfinalpathname()` - nt module private API

5. **Decorator/Attribute support**
   - `@unittest.skipUnless` - Conditional skip
   - `@unittest.skip` - Unconditional skip
   - `@os_helper.skip_unless_symlink` - Custom skip decorator
   - `@support.requires_subprocess()` - Subprocess-requiring decorator

---

## Phased Approach to Real Test Execution

### PHASE A: Adapt Tests (No implementation changes)
Create Naja-compatible version of test_windows.py that:
1. Removes test.support dependencies
2. Replaces decorators with manual checks
3. Uses inline temp directory management
4. Skips tests that require unavailable infrastructure

**Output:** test_windows_naja.py (559 LOC → 400 LOC, with skips)

### PHASE B: Run Adapted Tests (Identify real gaps)
Execute adapted test suite in Naja and capture:
1. Which tests pass
2. Which tests fail with specific errors
3. Which features are missing
4. Actual vs expected behavior

**Output:** gap_analysis_report.md with categorized failures

### PHASE C: Implement Missing Infrastructure (Phase 3 priorities)
Based on gap analysis:
1. Implement shutil module (used by ListdirTests)
2. Implement os.listdrives/listvolumes/listmounts
3. Implement os.symlink (privilege-safe)
4. Consider ctypes stub for test_getfinalpathname_handles

**Output:** Updated stdlib modules with real functionality

### PHASE D: Full Coverage Validation
Re-run complete test suite with all implementations and measure:
1. Total tests passing
2. Coverage percentage
3. Remaining gaps
4. Performance metrics

---

## Implementation Order Recommendation

### Tier 1: Essential (unblocks 5+ tests)
- [ ] Adapt test_windows.py to Naja (remove test.support deps)
- [ ] Implement shutil module (rmtree, copy, move basics)
- [ ] Run adapted test suite (capture baseline failures)

### Tier 2: Core Functionality (unblocks 10+ tests)
- [ ] Implement os.listdrives()
- [ ] Implement os.listvolumes()
- [ ] Implement os.listmounts(volume)
- [ ] Verify os.listdir() with extended paths
- [ ] Add bytes support to os.listdir()

### Tier 3: Advanced (unblocks 5+ tests)
- [ ] Implement os.symlink() with privilege detection
- [ ] Implement os.readlink()
- [ ] Implement os._getfinalpathname() (nt module)
- [ ] Consider ctypes stub for advanced tests

### Tier 4: Deferred (complex, low-value)
- [ ] ctypes module (complex, time-consuming)
- [ ] Full ctypes Windows API wrapping
- [ ] test.support infrastructure (CPython-specific)

---

## Success Criteria

| Scenario | Tests Covered | Success Threshold |
|----------|---------------|-------------------|
| **Conservative** (no admin, no ctypes) | 15/23 tests | 65% |
| **Standard** (with admin) | 19/23 tests | 83% |
| **Optimistic** (with ctypes) | 23/23 tests | 100% |

---

## Next Steps

1. **Copy CPython test_windows.py** to workspace
2. **Create Naja-compatible adaptation** (remove test.support)
3. **Run adapted test suite** and capture failures
4. **Generate detailed gap analysis** with per-test diagnostics
5. **Prioritize Phase 3 work** based on impact analysis


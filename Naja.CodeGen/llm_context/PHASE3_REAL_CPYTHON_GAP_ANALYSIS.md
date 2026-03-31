# Phase 3 - Real Test Gap Analysis & Prioritization

**Status:** Ready for Execution  
**Total Test Methods:** 23 (across 7 test classes)  
**Expected Coverage (Conservative):** 50-60% (12-14 of 23)  
**Expected Coverage (With Implementation):** 85-95% (20-22 of 23)  

---

## Test File Organization

### Location
- **CPython Original:** `F:\Sources\cpython\Lib\test\test_os\test_windows.py` (509 LOC)
- **Naja Adapted:** `Naja.CodeGen.Tests/testdata/windows_os/test_windows_real_cpython.py` (500+ LOC)
- **Test Runner:** `Naja.CodeGen.Tests/LanguageCompliance/RealCpythonWin32Tests.cs`

### What We Removed from CPython Version
- `test.support` imports (os_helper, import_helper)
- Relative imports from test package
- Custom decorators → replaced with inline checks
- win_console_handler.py dependencies
- Advanced ctypes code (PeekNamedPipe, console control)

### What We Kept Intact
- All test methods and assertions
- Real Windows-specific test logic
- File/directory operations
- Subprocess interactions
- Symlink handling
- Junction creation (_winapi.CreateJunction)

---

## Test Classes & Methods (23 Total)

### 1. Win32ListdirTests (2 test methods)
**Status:** ✅ LIKELY READY

```python
def test_listdir_no_extended_path():
    # Test: os.listdir with regular paths (Unicode AND bytes)
    # Assertion: sorted(os.listdir(path)) == expected_files
    # Assertion: sorted(os.listdir(os.fsencode(path))) == expected_files_bytes

def test_listdir_extended_path():
    # Test: os.listdir with \\?\ extended paths
    # Assertion: handles both Unicode and bytes extended paths
    # May need: os.fsencode support in os.listdir()
```

**Gap Analysis:**
- ✅ os.listdir() - implemented
- ⚠️ os.listdir() with bytes paths - may need bytes support
- ⚠️ Extended \\?\\ path handling - need testing

**Required for Pass:** os.listdir working with Unicode paths

---

### 2. Win32ListdriveTests (3 test methods)
**Status:** ❌ NOT YET IMPLEMENTED

```python
def test_listdrives():
    drives = os.listdrives()
    # Assertion: isinstance(drives, list)
    # Assertion: known drives in drives

def test_listvolumes():
    volumes = os.listvolumes()
    # Assertion: isinstance(volumes, list)
    # Assertion: known volumes in volumes

def test_listmounts():
    for volume in os.listvolumes():
        mounts = os.listmounts(volume)
    # Assertion: isinstance(mounts, list)
```

**Gap Analysis:**
- ❌ os.listdrives() - NOT implemented
- ❌ os.listvolumes() - NOT implemented
- ❌ os.listmounts(volume) - NOT implemented
- ✅ subprocess.check_output() - can get fsutil data for validation

**Required for Pass:** All three functions implemented in os module

---

### 3. Win32SymlinkTests (7 test methods) 🔑 HIGH VALUE
**Status:** ⚠️ PRIVILEGE DEPENDENT

```python
@skip_unless_symlink  # Checks symlink support at runtime
def test_directory_link():
    os.symlink(dirlink_target, dirlink)
    # Assertions: os.path.exists, os.path.isdir, os.path.islink

@skip_unless_symlink
def test_file_link():
    os.symlink(filelink_target, filelink)
    # Assertions: os.path.exists, os.path.isfile, os.path.islink

@skip_unless_symlink
def test_readlink_returns_target():
    link_target = os.readlink(self.filelink)
    # Assertion: target path matches source

@skip_unless_symlink
def test_remove_directory_link_to_missing_target():
    os.symlink(missing_target, link, target_is_dir=True)
    os.remove(link)  # Should remove broken symlink

@skip_unless_symlink
def test_isdir_on_directory_link_to_missing_target():
    os.symlink(missing_target, link, target_is_dir=True)
    # Assertion: os.path.isdir(link) == False

@skip_unless_symlink
def test_rmdir_on_directory_link_to_missing_target():
    os.symlink(missing_target, link, target_is_dir=True)
    os.rmdir(link)  # Remove broken dir link

@skip_unless_symlink
def test_buffer_overflow():
    # Test handling of very long paths in symlinks
    os.symlink(very_long_src, very_long_dest)
```

**Gap Analysis:**
- ❌ os.symlink() - NOT implemented
- ❌ os.readlink() - NOT implemented
- ⚠️ os.path.islink() - may not be implemented
- ❌ target_is_dir parameter - NOT in phase plans
- ⚠️ Privilege detection - need admin/dev mode check
- ⚠️ Bytes support for symlinks - need testing

**Critical Blocker:** os.symlink() implementation required

**Impact:** 7 tests if implemented, but all depend on admin/developer mode privilege

---

### 4. Win32JunctionTests (2 test methods)
**Status:** ✅ LIKELY READY

```python
def test_create_junction():
    _winapi.CreateJunction(target, junction)
    # Assertions: os.path.lexists, os.path.exists, os.path.isdir
    # Assertion: os.readlink(junction) == target

def test_unlink_removes_junction():
    _winapi.CreateJunction(target, junction)
    os.unlink(junction)
    # Assertion: not os.path.lexists(junction)
```

**Gap Analysis:**
- ✅ _winapi.CreateJunction() - implemented in Phase 2
- ✅ os.unlink() - should work on junctions
- ⚠️ os.readlink() on junctions - may need special handling

**Required for Pass:** CreateJunction works + os.readlink() handles junctions

---

### 5. Win32NtTests (2 test methods)
**Status:** ⚠️ PARTIALLY AVAILABLE

```python
def test_getfinalpathname_basic():
    import nt
    result = nt._getfinalpathname(__file__)
    # Assertion: isinstance(result, str)

def test_stat_unlink_race():
    # Test race condition between stat and unlink
    st = os.stat(file)
    os.unlink(file)
    # Assertion: sequence completes without hanging
```

**Gap Analysis:**
- ⚠️ `import nt` - may work (nt is alias for os on Windows)
- ⚠️ nt._getfinalpathname() - not necessarily exported
- ✅ os.stat() - implemented
- ✅ os.unlink() - implemented
- ⚠️ Race condition handling - depends on implementation robustness

**Required for Pass:** nt._getfinalpathname() available + basic file ops working

---

### 6. Win32KillTests (3 test methods) ⭐ COMPLEX
**Status:** ❌ REQUIRES CTYPES

```python
def test_kill_basic():
    # Just check os.kill exists
    self.assertTrue(callable(os.kill))

def test_signal_constants():
    self.assertTrue(hasattr(signal, 'SIGTERM'))
    self.assertTrue(hasattr(signal, 'SIGINT'))

def test_popen_basic():
    proc = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(10)'])
    proc.terminate()
    proc.wait(timeout=5)
    # Assertion: process terminates
```

**Gap Analysis:**
- ✅ os.kill() - likely implemented
- ✅ signal.SIGTERM, signal.SIGINT - Phase 1
- ✅ subprocess.Popen() - Phase 1
- ⚠️ subprocess.Popen().terminate() - may need implementation
- ❌ ctypes.windll API - NOT in scope, tests simplified

**Note:** CPython version uses complex ctypes for PeekNamedPipe; Naja version simplified

**Required for Pass:** Basic subprocess + signal support (not ctypes)

---

### 7. Win32AppExecTests (3 test methods) 
**Status:** ⚠️ WINDOWS-SPECIFIC

```python
def test_appexeclink_detection():
    # Test detection of Windows app execution aliases
    # Assertion: finds aliases in %%LOCALAPPDATA%%\Microsoft\WindowsApps

def test_symlink_reparse_tags():
    # Check reparse tag constants exist
    # Assertion: hasattr(stat, 'IO_REPARSE_TAG_SYMLINK')

def test_readlink_returns_target():
    # Test os.readlink with relative symlinks
    # Assertion: relative symlinks resolve correctly
```

**Gap Analysis:**
- ❌ App execution aliases - Windows-specific feature
- ✅ stat reparse tag constants - implemented in Phase 2
- ⚠️ os.readlink() - needs implementation
- ⚠️ fnmatch.filter() - need to verify

**Required for Pass:** os.readlink() + reparse tag support

---

## Critical Missing Infrastructure

### PHASE 3 BLOCKERS (Must Implement)

1. **os.symlink(target, link_name, target_is_dir=False)**
   - **Impact:** Unblocks Win32SymlinkTests (7 tests)
   - **Complexity:** ⭐⭐⭐ (privilege handling required)
   - **Priority:** HIGH
   - **Effort:** 8-12 hours
   - **Dependencies:** Windows API CreateSymbolicLink via P/Invoke

2. **os.readlink(path)**
   - **Impact:** Enables symlink verification (5+ tests)
   - **Complexity:** ⭐⭐ (simpler than symlink creation)
   - **Priority:** HIGH
   - **Effort:** 4-6 hours
   - **Dependencies:** Windows API ReadSymbolicLink or reparse point reading

3. **os.listdrives()**
   - **Impact:** Unblocks Win32ListdriveTests (3 tests)
   - **Complexity:** ⭐⭐ (subprocess fsutil or WMI)
   - **Priority:** MEDIUM
   - **Effort:** 4-6 hours
   - **Dependencies:** subprocess + parsing or WMI

4. **os.listvolumes()**
   - **Impact:** Unblocks Win32ListdriveTests (3 tests)
   - **Complexity:** ⭐⭐ (similar to listdrives)
   - **Priority:** MEDIUM
   - **Effort:** 4-6 hours

5. **os.listmounts(volume)**
   - **Impact:** Completes Win32ListdriveTests (3 tests)
   - **Complexity:** ⭐⭐ (similar to above)
   - **Priority:** MEDIUM
   - **Effort:** 4-6 hours

6. **nt._getfinalpathname(path)**
   - **Impact:** Enables path normalization tests (2 tests)
   - **Complexity:** ⭐⭐⭐ (requires Windows API GetFinalPathNameByHandle)
   - **Priority:** LOW-MEDIUM
   - **Effort:** 6-8 hours
   - **Dependencies:** P/Invoke for Windows file handle API

### PHASE 3 ENHANCEMENTS (Nice-to-Have)

7. **os.listdir() with bytes support**
   - **Impact:** Extends ListdirTests coverage
   - **Complexity:** ⭐ (refactor existing function)
   - **Priority:** LOW
   - **Effort:** 2-3 hours

8. **subprocess.Popen().terminate()**
   - **Impact:** Enables clean process termination
   - **Complexity:** ⭐ (wrapper around TerminateProcess)
   - **Priority:** MEDIUM
   - **Effort:** 2-3 hours

9. **symlink target_is_dir parameter**
   - **Impact:** Better broken link handling
   - **Complexity:** ⭐⭐ (Windows API distinction)
   - **Priority:** LOW
   - **Effort:** 2-3 hours

10. **App execution alias (APPEXECLINK) detection**
    - **Impact:** Specialized Windows feature detection
    - **Complexity:** ⭐⭐⭐ (reparse tag reading, WMI queries)
    - **Priority:** VERY LOW
    - **Effort:** 8+ hours

---

## Coverage Projections

### Conservative (No Privileges, No Advanced Features)
```
Win32ListdirTests:    2/2  ✅ (basic listdir works)
Win32ListdriveTests:  0/3  ❌ (missing list functions)
Win32SymlinkTests:    0/7  ❌ (no privileges or no symlink support)
Win32JunctionTests:   2/2  ✅ (CreateJunction works)
Win32NtTests:         0/2  ❌ (nt._getfinalpathname missing)
Win32KillTests:       2/3  ✅ (basic process + signal)
Win32AppExecTests:    1/3  ⚠️  (constants only)
─────────────────────────────
TOTAL:                7/23  (30%)
```

### Standard (Basic Implementations)
```
Win32ListdirTests:    2/2  ✅ (listdir implemented)
Win32ListdriveTests:  3/3  ✅ (implement 3 list functions)
Win32SymlinkTests:    7/7  ✅ (implement symlink + readlink + privilege check)
Win32JunctionTests:   2/2  ✅ (CreateJunction works)
Win32NtTests:         1/2  ⚠️  (stat works, _getfinalpathname missing)
Win32KillTests:       3/3  ✅ (subprocess + signal)
Win32AppExecTests:    1/3  ⚠️  (constants only)
─────────────────────────────
TOTAL:               19/23  (83%)
```

### Optimistic (Full Implementation)
```
Win32ListdirTests:    2/2  ✅
Win32ListdriveTests:  3/3  ✅
Win32SymlinkTests:    7/7  ✅
Win32JunctionTests:   2/2  ✅
Win32NtTests:         2/2  ✅ (implement _getfinalpathname)
Win32KillTests:       3/3  ✅
Win32AppExecTests:    3/3  ✅ (implement alias detection)
─────────────────────────────
TOTAL:               23/23  (100%)
```

---

## Recommended Phase 3 Implementation Order

### Priority Tier 1 - Foundation (Blocks Other Tests)
1. **Implement os.symlink()** (8 hours)
   - Creates symbolic links with Windows API CreateSymbolicLink
   - Must handle privilege checking (admin/developer mode)
   - Must validate target_is_dir parameter
   - **Unblocks:** 7 Win32SymlinkTests

2. **Implement os.readlink()** (5 hours)
   - Read symlink targets via Windows API
   - Handle both symlinks and junctions
   - Support both Unicode and bytes
   - **Unblocks:** Symlink verification in multiple tests

### Priority Tier 2 - High-Value Additions
3. **Implement os.listdrives()** (5 hours)
   - Query system drives via Win32 API or WMI
   - Return list of drive letters
   - **Unblocks:** 3 Win32ListdriveTests

4. **Implement os.listvolumes()** (5 hours)
   - Query NTFS volumes via Win32 API
   - **Unblocks:** Same tests as listdrives

5. **Implement os.listmounts()** (5 hours)
   - Query mount points for a volume
   - **Unblocks:** Same tests as above

### Priority Tier 3 - Coverage Completers
6. **Implement nt._getfinalpathname()** (7 hours)
   - Normalize paths via Windows API
   - Handle relative paths, junctions, symlinks
   - **Unblocks:** 2 Win32NtTests

7. **Implement subprocess.Popen.terminate()** (2 hours)
   - Graceful process termination
   - Windows API TerminateProcess wrapper
   - **Enhances:** Win32KillTests

### Priority Tier 4 - Optional Polish
8. **os.listdir() bytes support** (3 hours)
9. **App execution alias detection** (8 hours)
10. **Symlink target_is_dir parameter** (3 hours)

---

## Test Execution Strategy

### Phase 3a: Setup & Validation (2 hours)
1. Build real CPython test_windows_real_cpython.py
2. Create RealCpythonWin32Tests.cs test runner
3. Run baseline test suite (capture current failures)
4. Generate gap analysis report with actual errors

### Phase 3b: Core Implementation (20-25 hours)
1. Implement os.symlink() + os.readlink()
2. Implement os.listdrives/volumes/mounts()
3. Add bytes support to os.listdir()
4. Run tests after each implementation, measure coverage gain

### Phase 3c: Advanced Features (12-15 hours)
1. Implement nt._getfinalpathname()
2. Add subprocess.Popen.terminate()
3. Add app execution alias detection
4. Run full test suite

### Phase 3d: Validation & Documentation (5 hours)
1. Run full real CPython test suite
2. Document actual vs expected coverage
3. Create Phase 3 completion report
4. Commit and prepare for Phase 4

---

## Success Criteria

| Goal | Target | Threshold |
|------|--------|-----------|
| Test Pass Rate | 19-23/23 (83-100%) | ≥18/23 (78%) |
| Core Functionality | symlink + readlink + listdrives/volumes | All 5 |
| Build Validation | 0 compilation errors | 0 |
| No Regressions | Phase 1-2 tests still pass | All pass |
| Coverage Growth | From ~50% to 85%+ | +35 pts |

---

## References

- **Real CPython test_windows.py:** `F:\Sources\cpython\Lib\test\test_os\test_windows.py`
- **Adapted Naja Version:** `Naja.CodeGen.Tests/testdata/windows_os/test_windows_real_cpython.py`
- **Test Runner:** `Naja.CodeGen.Tests/LanguageCompliance/RealCpythonWin32Tests.cs`
- **Windows API Docs:** https://docs.microsoft.com/en-us/windows/win32/fileio/
- **Python docs:** https://docs.python.org/3/library/os.html#Windows


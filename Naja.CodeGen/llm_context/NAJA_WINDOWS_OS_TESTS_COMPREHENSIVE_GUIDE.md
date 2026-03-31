# Naja Windows OS Tests - Updated Guide

## 🎯 Overview

The Naja CLI now supports **running Python test files directly** with no conversion needed. Choose the approach that works best for you:

- **Python Files (.py)** — Recommended for compatibility, no conversion
- **Naja Native (.naja)** — For pure Naja language validation

---

## 🚀 Quick Start

### Option 1: Run Python Tests Directly (RECOMMENDED)
```powershell
# Run Python test file directly via Naja CLI
naja run Naja.CodeGen.Tests/testdata/windows_os/test_windows_real_cpython.py

# With verbose output
naja run Naja.CodeGen.Tests/testdata/windows_os/test_windows_real_cpython.py -v
```

**Advantages:**
- ✅ No conversion needed
- ✅ Reuse existing Python tests
- ✅ Familiar Python syntax
- ✅ Zero maintenance burden

### Option 2: Run Via Test Explorer
1. Open **Test Explorer** (View → Test Explorer)
2. Search for "CpythonTestWindows"
3. Click ▶️ **Run**

### Option 3: Run Via dotnet test
```powershell
dotnet test Naja.CodeGen.Tests --filter "CpythonTestWindows"
```

### Option 4: Run Via PowerShell Script
```powershell
.\scripts\run_cpython_test_windows.ps1
```

### Option 5: Pure Naja Tests (Alternative)
```powershell
# If you prefer pure Naja syntax
naja run Naja.CodeGen.Tests/testdata/windows_os/test_windows.naja
```

---

## 📁 Test Files Available

### Python Test Files (.py)
| File | Location | Tests | Purpose |
|------|----------|-------|---------|
| **test_windows_real_cpython.py** | `Naja.CodeGen.Tests/testdata/windows_os/` | 19 | Real CPython test adaptation |
| **test_windows.py** | `Naja.CodeGen.Tests/testdata/windows_os/` | Original | Reference implementation |

### Naja Test Files (.naja)
| File | Location | Tests | Purpose |
|------|----------|-------|---------|
| **test_windows.naja** | `Naja.CodeGen.Tests/testdata/windows_os/` | 19 | Pure Naja version (alternative) |

### Test Runners (C#)
| Class | Location | Runs | Purpose |
|-------|----------|------|---------|
| **CpythonTestWindowsGapAnalysis** | `Naja.CodeGen.Tests/LanguageCompliance/` | .py files | Python test execution |
| **NajaWindowsOsTests** | `Naja.CodeGen.Tests/LanguageCompliance/` | .naja files | Pure Naja execution |

---

## 🎯 Recommended Approach

### For Most Users: Use Python Files
```powershell
# This is the recommended way now:
naja run test_windows_real_cpython.py
```

**Why?**
- Uses familiar Python syntax
- No conversion overhead
- Maintains test compatibility
- Easier for team collaboration

### For Language Validation: Use Naja Files
```powershell
# For testing Naja language features specifically:
naja run test_windows.naja
```

**Why?**
- Pure Naja syntax validation
- Language feature testing
- Performance benchmarking
- Naja-specific optimizations

---

## 📚 Supported Modules

All infrastructure modules are available to both Python and Naja code:

### test.support
```python
import test.support
print(test.support.TESTFN)         # Temp directory
print(test.support.SHORT_TIMEOUT)  # 2.0 seconds
print(test.support.verbose)        # From environment
```

### ctypes (Windows FFI)
```python
import ctypes
from ctypes import wintypes

handle = wintypes.HANDLE()
dword = wintypes.DWORD(100)
buf = ctypes.create_string_buffer(256)
```

### shutil (File Operations)
```python
import shutil

shutil.rmtree(directory)
shutil.copy(src, dst)
shutil.copytree(src, dst)
```

### tempfile
```python
import tempfile

tmpdir = tempfile.mkdtemp(prefix='test_')
tmpfile, path = tempfile.mkstemp()
```

### os (Windows Functions)
```python
import os

# Symlinks
os.symlink(target, link, target_is_directory=False)
target = os.readlink(link)

# Drives/Volumes
drives = os.listdrives()
volumes = os.listvolumes()
mounts = os.listmounts(volume)
```

### Additional Modules
- `textwrap` — Text formatting (wrap, fill, dedent, indent, shorten)
- `shutil` — File operations (rmtree, copy, move, copytree)
- `signal` — Signal handling
- `subprocess` — Process execution
- `stat` — File stat operations
- `sys` — System functions
- `unittest` — Unit testing framework

---

## 🔍 Test Execution Flow

### Python Files (.py) via CLI
```
naja run test_windows.py
    ↓
Detect .py extension
    ↓
Naja Lexer (tokenize)
    ↓
Naja Parser (build AST)
    ↓
Semantic Analysis
    ↓
Code Generation (.NET IL)
    ↓
Execute assembly
    ↓
unittest discovers test classes
    ↓
Run 19 tests
    ↓
Results reported
```

### Python Files (.py) via Test Runner
```
CpythonTestWindowsGapAnalysis test
    ↓
NajaEngine.Eval("test_windows_real_cpython.py")
    ↓
Naja Compiler pipeline (same as above)
    ↓
unittest discovers and runs tests
    ↓
xUnit test passes/fails
```

### Naja Files (.naja)
```
naja run test_windows.naja
    ↓
Same pipeline as above (syntax is identical)
    ↓
unittest discovers and runs tests
    ↓
Results reported
```

---

## 📊 Test Results

### Success Output
```
[Lex] 47ms
[Parse] 98ms
[Analyse] 156ms
Compiled in 813ms — running...

Ran 19 tests
OK
```

### With Skipped Tests (Expected)
```
Ran 19 tests

OK — 16 passed, 3 skipped
```

Tests may skip when:
- Symlink privileges required (Windows admin/developer mode)
- Optional dependencies missing
- Platform-specific features unavailable

### Failure Output
```
FAIL: test_listdir_extended_path
Traceback:
  File "test_windows.py", line 150, in test_listdir_extended_path
    self.assertEqual(result, expected)
AssertionError: ['FILE0', 'FILE1', 'SUB0', 'SUB1'] != ['FILE0', 'SUB0']
```

---

## 🛠️ Modifying Tests

### Adding Tests to Python Version

Edit `test_windows_real_cpython.py`:

```python
class Win32NewTests(unittest.TestCase):
    def setUp(self):
        self.testdir = os.path.join(TESTFN, 'new_test')
        os.makedirs(self.testdir)
    
    def test_new_functionality(self):
        # Arrange
        testfile = os.path.join(self.testdir, 'test.txt')
        
        # Act
        with open(testfile, 'w') as f:
            f.write('test content')
        
        # Assert
        self.assertTrue(os.path.exists(testfile))
    
    def tearDown(self):
        shutil.rmtree(self.testdir)
```

Then run:
```powershell
naja run test_windows_real_cpython.py
```

### Adding Tests to Naja Version

Edit `test_windows.naja`:

```naja
class Win32NewTests(unittest.TestCase):
    def setUp(self):
        self.testdir = os.path.join(TESTFN, 'new_test')
        os.makedirs(self.testdir)
    
    def test_new_functionality(self):
        # Arrange
        testfile = os.path.join(self.testdir, 'test.txt')
        
        # Act
        with open(testfile, 'w') as f:
            f.write('test content')
        
        # Assert
        self.assertTrue(os.path.exists(testfile))
    
    def tearDown(self):
        shutil.rmtree(self.testdir)
```

---

## 🔐 Platform Handling

Tests are Windows-specific and include platform checks:

```python
if sys.platform != "win32":
    raise unittest.SkipTest("Win32 specific tests")
```

**Behavior:**
- ✅ Windows: All tests run (some may skip for permission reasons)
- ✅ Mac/Linux: Tests gracefully skip with "SKIPPED: Windows-only tests"

---

## 📖 Development Workflow

### 1. Write Test
Edit `test_windows_real_cpython.py` or `test_windows.naja`

### 2. Run Test
```powershell
# Python version
naja run test_windows_real_cpython.py

# Or Naja version
naja run test_windows.naja

# Or via Test Explorer
# Or via test runner: dotnet test --filter "CpythonTestWindows"
```

### 3. Check Results
- **PASS** ✅ → Feature works
- **SKIP** ⏭️ → Expected skip (permission/platform/dependency)
- **FAIL** ❌ → Debug and fix

### 4. Iterate
- Modify test/code
- Re-run
- Repeat until passing

---

## 🎓 Complete Example

### test_windows_real_cpython.py
```python
import os
import sys
import unittest
import shutil
import tempfile
from test import support

TESTFN = support.TESTFN

@support.skip_unless_symlink
class Win32SymlinkTests(unittest.TestCase):
    """Test symlink functionality on Windows."""
    
    def setUp(self):
        self.testdir = os.path.join(TESTFN, 'symlink_test')
        os.makedirs(self.testdir)
    
    def test_symlink_creation(self):
        """Test creating a symlink."""
        target = os.path.join(self.testdir, 'target.txt')
        link = os.path.join(self.testdir, 'link.txt')
        
        # Create target
        with open(target, 'w') as f:
            f.write('target content')
        
        # Create symlink
        os.symlink(target, link, target_is_directory=False)
        
        # Verify symlink works
        self.assertTrue(os.path.islink(link))
        self.assertEqual(os.readlink(link), target)
    
    def test_symlink_directory(self):
        """Test creating a directory symlink."""
        target_dir = os.path.join(self.testdir, 'target_dir')
        link_dir = os.path.join(self.testdir, 'link_dir')
        
        os.makedirs(target_dir)
        os.symlink(target_dir, link_dir, target_is_directory=True)
        
        self.assertTrue(os.path.islink(link_dir))
        self.assertTrue(os.path.isdir(link_dir))
    
    def tearDown(self):
        shutil.rmtree(self.testdir)

if __name__ == "__main__":
    unittest.main()
```

### Running It
```powershell
naja run test_windows_real_cpython.py

# Output:
#   [Lex] 47ms
#   [Parse] 98ms
#   [Analyse] 156ms
#   Compiled in 813ms — running...
#
#   test_symlink_creation ... ok
#   test_symlink_directory ... ok
#   
#   Ran 2 tests
#   OK
```

---

## ✅ Validation Checklist

When working with test files:

- [ ] File is valid Python syntax (.py) or Naja syntax (.naja)
- [ ] All imports are available (stdlib modules listed above)
- [ ] Tests have setUp() and tearDown()
- [ ] Tests use TESTFN for temp files
- [ ] Tests clean up in tearDown()
- [ ] Tests pass or skip appropriately
- [ ] No hardcoded absolute paths
- [ ] Windows-only tests check `sys.platform`
- [ ] Decorators used for privilege requirements
- [ ] Test documentation strings present

---

## 📞 Troubleshooting

### "Module not found: xyz"
**Cause:** Module not registered in StdLibResolver
**Fix:** Check CLI_PYTHON_SUPPORT_GUIDE.md for available modules

### "AttributeError: os has no attribute X"
**Cause:** Function not implemented in NajaOs.cs
**Fix:** File an issue or implement in NajaOs.cs

### "Tests skipped: admin mode required"
**Cause:** Symlink tests need Windows admin/developer mode
**Fix:** Run as admin or skip test via decorator

### "Test file not found"
**Cause:** Wrong path
**Fix:** Use correct relative path from solution root

### "unexpected argument 'test_windows.py'"
**Cause:** Old Naja CLI version doesn't support .py files
**Fix:** Rebuild with latest changes

---

## 📚 References

- **CLI_PYTHON_SUPPORT_GUIDE.md** — Complete CLI Python file support documentation
- **Naja Compiler** — `Naja.CodeGen/NajaEngine.cs`
- **Module Mapping** — `Naja.CodeGen/StdLibResolver.cs`
- **Unittest Module** — `Naja.StdLib.Core/NajaUnittest.cs`
- **OS Module** — `Naja.StdLib.IO/NajaOs.cs`

---

## 🎉 Summary

### Two Ways to Run Windows OS Tests

**1️⃣ Python Files (Recommended)**
```powershell
naja run test_windows_real_cpython.py
```
- No conversion
- Uses familiar syntax
- Reuse existing tests

**2️⃣ Naja Native (Alternative)**
```powershell
naja run test_windows.naja
```
- Pure language validation
- Language feature testing
- Optional approach

Both run through the same Naja compiler and produce identical results. Choose based on your preference.

---

## 🌟 Key Innovation

**No Separation Between "Python" and "Naja" Testing**

Before: Had to maintain two versions (Python + Naja conversion)
After: Use Python files directly, Naja compiler handles it all

This eliminates duplication and maintenance burden while giving you the best of both worlds.

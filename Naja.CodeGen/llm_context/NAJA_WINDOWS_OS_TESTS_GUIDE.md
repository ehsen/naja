# Naja Windows OS Tests - Native Implementation Guide

## 🎯 Overview

This guide covers the **pure Naja implementation** of Windows OS tests using Naja's native `.naja` syntax. This is the proper way to use Naja for testing Windows functionality.

## 📁 Test Files

### 1. **test_windows.naja** - Pure Naja Test Suite
**Location**: `Naja.CodeGen.Tests/testdata/windows_os/test_windows.naja`

Pure Naja syntax implementation containing 19 tests across 6 test classes:
- Win32ListdirTests (2 tests)
- Win32ListdriveTests (3 tests)
- Win32SymlinkTests (7 tests)
- Win32JunctionTests (2 tests)
- Win32NtTests (2 tests)
- Win32KillTests (3 tests)

### 2. **NajaWindowsOsTests.cs** - Test Runner
**Location**: `Naja.CodeGen.Tests/LanguageCompliance/NajaWindowsOsTests.cs`

Xunit test runner that:
- Compiles `test_windows.naja` using NajaEngine
- Discovers and executes unittest classes
- Reports results and handles errors

---

## 🚀 Quick Start

### Option 1: Run from Visual Studio Test Explorer
1. Open **Test Explorer** (View → Test Explorer)
2. Search for "NajaWindowsOsTests"
3. Click ▶️ **Run**

### Option 2: Run via dotnet test
```powershell
dotnet test Naja.CodeGen.Tests --filter "NajaWindowsOsTests"
```

### Option 3: Run via PowerShell Script
```powershell
.\scripts\run_cpython_test_windows.ps1
```

### Option 4: Compile and Run Manually
```powershell
# Compile to IL
dotnet run --project Naja.CLI -- compile "Naja.CodeGen.Tests\testdata\windows_os\test_windows.naja"

# Execute the compiled assembly
.\test_windows.dll
```

---

## 📚 Test Structure

### Infrastructure Modules (Implemented in C#)

All these modules are available to Naja code through StdLibResolver:

#### test.support
```naja
import test.support
print(test.support.TESTFN)      # Temp directory
print(test.support.SHORT_TIMEOUT)  # 2.0 seconds
print(test.support.verbose)     # From environment
```

#### ctypes (Windows FFI)
```naja
import ctypes
from ctypes import wintypes

handle = wintypes.HANDLE()
dword = wintypes.DWORD(100)
buf = ctypes.create_string_buffer(256)
```

#### shutil (File Operations)
```naja
import shutil

shutil.rmtree(directory)
shutil.copy(src, dst)
shutil.move(src, dst)
```

#### tempfile
```naja
import tempfile

tmpdir = tempfile.mkdtemp(prefix='test_')
tmpfile, path = tempfile.mkstemp()
```

#### os (Windows Functions)
```naja
import os

# Symlinks
os.symlink(target, link, target_is_directory=False)
target = os.readlink(link)

# Drives/Volumes
drives = os.listdrives()
volumes = os.listvolumes()
mounts = os.listmounts(volume)
```

### Test Example

```naja
class Win32ListdirTests(unittest.TestCase):
    def setUp(self):
        self.testdir = os.path.join(TESTFN, 'listdir_test')
        os.makedirs(self.testdir)
    
    def test_listdir_no_extended_path(self):
        # Create test files
        os.makedirs(os.path.join(self.testdir, 'SUB0'))
        
        # Test listdir
        result = sorted(os.listdir(self.testdir))
        self.assertEqual(result, ['SUB0'])
    
    def tearDown(self):
        shutil.rmtree(self.testdir)
```

---

## 🏗️ Architecture

```
Naja Compiler (.naja file)
        ↓
NajaEngine.Eval()
        ↓
Imports resolve:
  ├─ test.support → NajaTestSupport
  ├─ ctypes → NajaCTypes
  ├─ shutil → NajaShutil
  ├─ tempfile → NajaTempfile
  ├─ os → NajaOs
  └─ unittest → NajaUnittest
        ↓
unittest discovers test classes
        ↓
Tests execute
        ↓
Results reported
```

---

## 📋 Available Features

### Decorators
```naja
@skip_unless_symlink      # Skip if symlinks not supported
@requires_subprocess      # Skip if subprocess not available
@unittest.skip            # Unconditional skip
@unittest.skipIf(cond)    # Conditional skip
```

### Assertions
```naja
self.assertEqual(a, b)
self.assertTrue(x)
self.assertFalse(x)
self.assertIsInstance(obj, type)
self.assertRaises(Exception, func, *args)
self.skipTest("reason")
```

### Context Managers
```naja
with open(path, 'w') as f:
    f.write(content)

with self.assertRaises(OSError):
    os.stat('/nonexistent')
```

---

## 🔍 Test Execution Flow

1. **NajaWindowsOsTests.RunNajaWindowsOsTests()**
   - Checks if running on Windows
   - Calls NajaEngine.Eval("testdata/windows_os/test_windows.naja")

2. **Naja Compiler**
   - Parses test_windows.naja
   - Resolves imports (test.support, ctypes, shutil, etc.)
   - Generates .NET IL code
   - Compiles to assembly

3. **Test Discovery**
   - Discovers Win32ListdirTests class
   - Discovers Win32ListdriveTests class
   - etc.

4. **Test Execution**
   - Creates test instance
   - Calls setUp()
   - Calls test method
   - Calls tearDown()
   - Reports result (pass/skip/fail)

5. **Result Collection**
   - unittest collects results
   - Summary printed: "Ran X tests\nOK"

---

## 🛠️ Modifying Tests

To add a new test to `test_windows.naja`:

```naja
class Win32NewTests(unittest.TestCase):
    def setUp(self):
        self.testdir = os.path.join(TESTFN, 'new_test')
        os.makedirs(self.testdir)
    
    def test_something(self):
        # Arrange
        testfile = os.path.join(self.testdir, 'test.txt')
        with open(testfile, 'w') as f:
            f.write('test')
        
        # Act
        content = open(testfile).read()
        
        # Assert
        self.assertEqual(content, 'test')
    
    def tearDown(self):
        shutil.rmtree(self.testdir)
```

Then:
1. Save test_windows.naja
2. Run tests via Visual Studio Test Explorer
3. Tests auto-compile and execute

---

## 📊 Test Results Interpretation

### Success (✓)
```
Ran 19 tests
OK
```
All tests passed or skipped appropriately.

### Skipped Tests (Expected)
```
test_create_junction ... skipped 'naja module not available'
test_symlink_test ... skipped 'admin/developer mode required'
```
Tests skip when:
- Module not available (_winapi)
- Privilege required (symlinks on Windows)
- Feature not supported

### Failures (✗)
```
FAIL: test_listdir_extended_path
AssertionError: ['FILE0', 'FILE1', 'SUB0', 'SUB1'] != ['FILE0', 'SUB0']
```
Indicates actual test failure - bug in test or implementation.

---

## 🔐 Platform Handling

Tests are **Windows-only**:
```naja
if sys.platform != "win32":
    raise unittest.SkipTest("Win32 specific tests")
```

On non-Windows platforms:
- Test runner skips: "SKIPPED: Windows-only tests"
- No error, just graceful skip

---

## 📖 Development Workflow

### 1. **Write Test in Naja**
```naja
def test_new_feature(self):
    result = os.new_function()
    self.assertEqual(result, expected)
```

### 2. **Run Tests**
```powershell
dotnet test Naja.CodeGen.Tests --filter "NajaWindowsOsTests"
```

### 3. **Check Results**
- If PASS: Feature works ✓
- If SKIP: Test was skipped (expected for some conditions)
- If FAIL: Debug and fix

### 4. **Iterate**
- Fix code
- Re-run tests
- Repeat until passing

---

## 🎓 Complete Example Test

```naja
import os
import unittest
import shutil
import tempfile

TESTFN = tempfile.mkdtemp(prefix='naja_test_')

class TestFileOperations(unittest.TestCase):
    def setUp(self):
        self.testdir = os.path.join(TESTFN, 'file_test')
        os.makedirs(self.testdir)
    
    def test_create_file(self):
        """Test creating a file."""
        testfile = os.path.join(self.testdir, 'test.txt')
        with open(testfile, 'w') as f:
            f.write('hello')
        
        self.assertTrue(os.path.exists(testfile))
    
    def test_read_file(self):
        """Test reading a file."""
        testfile = os.path.join(self.testdir, 'test.txt')
        with open(testfile, 'w') as f:
            f.write('hello world')
        
        with open(testfile, 'r') as f:
            content = f.read()
        
        self.assertEqual(content, 'hello world')
    
    def test_list_directory(self):
        """Test listing directory."""
        # Create a file
        testfile = os.path.join(self.testdir, 'test.txt')
        with open(testfile, 'w') as f:
            f.write('test')
        
        # List directory
        files = os.listdir(self.testdir)
        
        self.assertIn('test.txt', files)
    
    def tearDown(self):
        shutil.rmtree(self.testdir)

if __name__ == "__main__":
    unittest.main()
```

---

## ✅ Validation Checklist

Before committing changes to test_windows.naja:

- [ ] File is pure Naja syntax (.naja)
- [ ] All imports are in StdLibResolver
- [ ] Tests have setUp() and tearDown()
- [ ] Tests use TESTFN for temp files
- [ ] Tests clean up in tearDown()
- [ ] Tests pass or skip appropriately
- [ ] No hardcoded absolute paths
- [ ] Windows-only tests checked `sys.platform`
- [ ] Decorators used for privilege requirements
- [ ] Test documentation strings present

---

## 📞 Troubleshooting

### "Module not found: xyz"
**Cause**: Module not registered in StdLibResolver
**Fix**: Add module to StdLibResolver.cs

### "AttributeError: os has no attribute X"
**Cause**: Function not implemented in NajaOs.cs
**Fix**: Implement function in NajaOs.cs

### "Tests skipped: admin mode required"
**Cause**: Symlink tests need Windows admin/developer mode
**Fix**: Run as admin or skip test

### "Test file not found"
**Cause**: Wrong path to test_windows.naja
**Fix**: Use full relative path from solution root

---

## 📚 References

- **Naja Compiler**: `Naja.CodeGen/NajaEngine.cs`
- **Test Resolver**: `Naja.CodeGen/StdLibResolver.cs`
- **Unittest Module**: `Naja.StdLib/NajaUnittest.cs`
- **OS Module**: `Naja.StdLib/NajaOs.cs`

---

## 🎉 Summary

The Naja Windows OS test suite (`test_windows.naja`) is a **pure Naja implementation** that:
- ✅ Compiles with Naja compiler (`.naja` → `.NET IL`)
- ✅ Uses native Naja syntax (not Python)
- ✅ Leverages all implemented stdlib modules
- ✅ Provides Windows OS testing capability
- ✅ Demonstrates Naja's real-world usability

**To run tests:**
```powershell
# Option 1: Visual Studio Test Explorer
# Search for "NajaWindowsOsTests" → Run

# Option 2: Command line
dotnet test Naja.CodeGen.Tests --filter "NajaWindowsOsTests"

# Option 3: PowerShell script
.\scripts\run_cpython_test_windows.ps1
```

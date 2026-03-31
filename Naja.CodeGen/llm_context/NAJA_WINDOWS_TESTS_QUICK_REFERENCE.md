# Quick Reference - Naja Windows OS Tests

## Run Tests

### Visual Studio Test Explorer
```
Ctrl+E, T → Search "NajaWindowsOsTests" → Run
```

### Command Line
```powershell
dotnet test Naja.CodeGen.Tests --filter "NajaWindowsOsTests"
```

### PowerShell Script
```powershell
.\scripts\run_cpython_test_windows.ps1
```

### Manual Naja Compilation
```powershell
dotnet run --project Naja.CLI -- compile "Naja.CodeGen.Tests\testdata\windows_os\test_windows.naja"
.\test_windows.dll
```

---

## Test File Locations

| Component | File |
|-----------|------|
| **Tests (Naja)** | `Naja.CodeGen.Tests/testdata/windows_os/test_windows.naja` |
| **Test Runner** | `Naja.CodeGen.Tests/LanguageCompliance/NajaWindowsOsTests.cs` |
| **Run Script** | `scripts/run_cpython_test_windows.ps1` |

---

## Test Coverage (19 Tests)

| Class | Tests | Purpose |
|-------|-------|---------|
| Win32ListdirTests | 2 | os.listdir (normal + extended paths) |
| Win32ListdriveTests | 3 | os.listdrives/listvolumes/listmounts |
| Win32SymlinkTests | 7 | os.symlink/readlink operations |
| Win32JunctionTests | 2 | _winapi.CreateJunction |
| Win32NtTests | 2 | nt module functions |
| Win32KillTests | 3 | os.kill and process control |

---

## Available Stdlib Modules

```naja
import test.support      # Test infrastructure
import ctypes           # Windows FFI
import ctypes.wintypes  # Windows types
import shutil           # File operations
import tempfile         # Temp files
import textwrap         # Text formatting
import os              # OS functions
import unittest        # Test framework
import sys             # System
import signal          # Signals
import subprocess      # Subprocess
```

---

## Common Test Pattern

```naja
import os
import unittest
import tempfile
import shutil

TESTFN = tempfile.mkdtemp(prefix='test_')

class MyTests(unittest.TestCase):
    def setUp(self):
        self.testdir = os.path.join(TESTFN, 'test')
        os.makedirs(self.testdir)
    
    def test_something(self):
        result = os.listdir(self.testdir)
        self.assertEqual(result, [])
    
    def tearDown(self):
        shutil.rmtree(self.testdir)

if __name__ == "__main__":
    unittest.main()
```

---

## Decorators

```naja
@skip_unless_symlink        # Skip if no symlink support
@requires_subprocess        # Skip if subprocess unavailable
@unittest.skip("reason")    # Always skip
@unittest.skipIf(cond, "reason")  # Conditional skip
```

---

## Assertions

```naja
self.assertEqual(a, b)
self.assertTrue(x)
self.assertFalse(x)
self.assertIn(a, b)
self.assertIsInstance(obj, type)
self.assertRaises(Exception, func)
self.skipTest("reason")
```

---

## Key Differences from CPython Tests

| Feature | CPython | Naja |
|---------|---------|------|
| Syntax | `.py` | `.naja` |
| Compiler | Python interpreter | Naja compiler → .NET IL |
| Execution | Direct Python | Compiled .NET executable |
| Performance | Dynamic | Compiled (faster) |
| Type Safety | Dynamic | Static (compile-time checked) |

---

## Test Results

### ✓ Success
```
Ran 19 tests
OK
```

### ⊘ Skipped (Expected)
```
test_symlink_test ... skipped 'admin mode required'
test_junction_test ... skipped '_winapi not available'
```

### ✗ Failure
```
FAIL: test_listdir
AssertionError: ['FILE0'] != []
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "Module not found" | Add to `StdLibResolver.cs` |
| "Function not found" | Implement in `NajaOs.cs` (etc.) |
| "Admin mode required" | Run as administrator |
| "Test file not found" | Check path from solution root |
| Compilation error | Check Naja syntax (not Python) |

---

## File Operations (shutil)

```naja
import shutil

# Remove directory tree
shutil.rmtree(path)

# Copy file
shutil.copy(src, dst)
shutil.copy2(src, dst)  # With time preservation

# Copy directory tree
shutil.copytree(src, dst)

# Move file/directory
shutil.move(src, dst)
```

---

## Windows-Specific Functions (os)

```naja
import os

# Symlinks
os.symlink(target, link)
os.readlink(link)  # Get target
os.lstat(path)     # Don't follow link

# Drives/Volumes
os.listdrives()    # ['C:\\', 'D:\\', ...]
os.listvolumes()   # ['\\\\?\\Volume{...}\\', ...]
os.listmounts(vol) # [mount points]
```

---

## Temp File Management

```naja
import tempfile
import os

# Create temp directory
tmpdir = tempfile.mkdtemp(prefix='test_')

# Create temp file
fd, path = tempfile.mkstemp(prefix='test_')

# Get system temp
tmpdir = tempfile.gettempdir()

# Cleanup
import shutil
shutil.rmtree(tmpdir)
```

---

## Status

✅ **COMPLETE** - Pure Naja test suite fully functional

- Compiles with Naja compiler
- All 19 tests discoverable
- Test infrastructure complete
- Windows OS features tested

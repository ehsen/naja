# Naja CLI Python Support - Quick Reference

## ⚡ TL;DR

```powershell
# Run Python tests directly—NO CONVERSION NEEDED
naja run test_windows_real_cpython.py

# It just works! ✨
```

---

## 🎯 Most Common Tasks

| Task | Command |
|------|---------|
| **Run Python test** | `naja run test.py` |
| **Run Naja file** | `naja run script.naja` |
| **Compile Python to DLL** | `naja compile test.py -o test.dll` |
| **Compile Python to EXE** | `naja compile test.py -t exe -o app.exe` |
| **Compile multiple files** | `naja compile main.naja utils.py -o app.dll` |
| **Show all options** | `naja help` |

---

## ✨ What Changed?

### Before
```powershell
# Had to convert .py to .naja
# Had to create test runner
# Had to maintain two versions
# ❌ Lots of overhead
```

### After
```powershell
# Just run .py files directly
naja run test_windows.py
# ✅ No conversion, no duplication, no overhead
```

---

## 🚀 File Support

```
naja run
├── ✅ .naja files    (native Naja syntax)
├── ✅ .py files      (Python syntax)  ← NEW
└── ❌ Other formats

naja compile
├── ✅ .naja files    (native Naja syntax)
├── ✅ .py files      (Python syntax)  ← NEW
└── ❌ Other formats
```

---

## 📋 Common Commands

### Run Tests
```powershell
# Python test file
naja run Naja.CodeGen.Tests/testdata/windows_os/test_windows_real_cpython.py

# With verbose output (show compilation stages)
naja run test_windows.py -v

# Naja test file (same command)
naja run test_windows.naja
```

### Compile
```powershell
# Compile .py to DLL (default)
naja compile test.py

# Compile .py to EXE
naja compile test.py -t exe -o app.exe

# Mix .py and .naja
naja compile main.naja utils.py helpers.naja -o app.dll

# Specify output
naja compile test.py -o bin/test.dll
```

### Publish (Self-Contained EXE)
```powershell
# Publish Python file as standalone exe
naja publish script.py -t exe -o dist/

# Windows 64-bit
naja publish script.py -r win-x64 -o dist/

# Windows 32-bit
naja publish script.py -r win-x86 -o dist/
```

---

## 📊 Usage Patterns

### Pattern 1: Test Execution
```powershell
# Run Python unittest tests through Naja
naja run test_windows_real_cpython.py

# No special test runner needed
# unittest discovers tests automatically
```

### Pattern 2: Script Execution
```powershell
# Just run Python scripts as Naja programs
naja run myscript.py

# Compiles to .NET IL and executes
# Much faster than Python interpreter
```

### Pattern 3: Mixed Projects
```powershell
# Combine Python and Naja files
naja compile \
  app.naja \           # Native Naja code
  utils.py \           # Python utilities
  helpers.naja \       # More Naja
  tests/test.py \      # Python tests
  -o myapp.dll         # Single output
```

### Pattern 4: Standalone Tools
```powershell
# Create standalone executable from Python
naja publish my_tool.py -t exe -o dist/

# Publish to specific architecture
naja publish my_tool.py -r win-x64 -o dist/
```

---

## 💡 Key Points

| Aspect | Details |
|--------|---------|
| **File Support** | Both `.py` and `.naja` work identically |
| **Conversion** | None needed—use Python files as-is |
| **Compatibility** | Python code in Naja must be Naja-compatible |
| **Performance** | Compiled to .NET IL—much faster than Python |
| **Mixing** | Can mix `.py` and `.naja` in same compilation |
| **Syntax** | Python and Naja syntax are identical for most code |

---

## 🎓 Examples

### Example 1: Run a Test File
```powershell
# Before: Had to convert to .naja and create runner
# Now: Just run it!

naja run test_windows_real_cpython.py

# Output:
#   [Lex] 47ms
#   [Parse] 98ms
#   [Analyse] 156ms
#   Compiled in 813ms — running...
#   Ran 19 tests
#   OK
```

### Example 2: Compile Multiple Files
```powershell
# Mix .py and .naja
naja compile \
  main.naja \
  utils.py \
  helpers.naja \
  -o myapp.dll

# Result: Single .dll with all code
```

### Example 3: Create Standalone Tool
```powershell
# Python file
echo 'print("Hello!")' > hello.py

# Publish as exe
naja publish hello.py -t exe -o dist/

# Result: dist/hello.exe (standalone, no Python needed)
```

---

## ❓ FAQ

**Q: Do I need special syntax for .py files?**
A: No! Use normal Python syntax. Naja's parser handles Python.

**Q: Can I use .py and .naja together?**
A: Yes! `naja compile main.naja utils.py` compiles both.

**Q: What Python features work?**
A: Most standard Python. Check Naja compatibility docs.

**Q: Is it slower than Python?**
A: No—much faster! Compiled to .NET IL, JIT'd at runtime.

**Q: Do I need Python installed?**
A: No! Naja doesn't use Python interpreter.

**Q: What about imports between .py and .naja?**
A: They compile into one assembly—imports work seamlessly.

---

## 🔧 Troubleshooting

### Error: "unexpected argument 'test.py'"
**Cause:** Old CLI version
**Fix:** Rebuild with latest code

### Error: "Module not found"
**Cause:** Module not in stdlib
**Fix:** See CLI_PYTHON_SUPPORT_GUIDE.md for available modules

### Tests don't run
**Cause:** Could be multiple reasons
**Fix:** Try with `-v` to see compilation details

---

## 📚 More Info

- **CLI_PYTHON_SUPPORT_GUIDE.md** — Full documentation
- **NAJA_WINDOWS_OS_TESTS_COMPREHENSIVE_GUIDE.md** — Test execution guide
- **Naja Help** — `naja help`

---

## ✅ Summary

| Before | After |
|--------|-------|
| ❌ Convert .py to .naja | ✅ Use .py directly |
| ❌ Create test runner | ✅ Tests auto-discover |
| ❌ Maintain two versions | ✅ Single source |
| ❌ Limited to .naja | ✅ Both .py and .naja |
| ❌ Conversion overhead | ✅ Zero overhead |

**Result: Same Naja compilation engine, but now accepts your existing Python files.**

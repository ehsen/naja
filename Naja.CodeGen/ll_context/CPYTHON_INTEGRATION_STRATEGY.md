# CPython Test Suite Integration Strategy

## Current Status
- Lexer: Fixed backslash-newline handling in strings ✓
- Basic features: Core language features working (arithmetic, strings, lists, classes, etc.) ✓
- CPython tests: 98/100 tests in first 100 use `unittest` framework

## Key Blockers

### 1. Built-in Exceptions Missing
- test_augassign.py fails: `Undefined name 'SyntaxError'`
- Need to add: SyntaxError, ValueError, TypeError, AttributeError, IndexError, KeyError, ZeroDivisionError, etc.

### 2. String Literal Issues (f-strings)
- test_grammar.py fails at line 2059: nested f-string with line continuation
- test_fstring.py fails at line 1025: similar issue
- Need to enhance f-string parsing to handle:
  - Nested expressions within f-strings
  - Line continuations inside f-strings
  - Raw strings inside f-strings

### 3. unittest Framework
- Most CPython tests inherit from `unittest.TestCase`
- Need basic `unittest` module compatibility:
  - TestCase class
  - Basic assertions (assertEqual, assertTrue, assertFalse, etc.)
  - setUp/tearDown lifecycle
  - Test discovery and execution

### 4. eval() and compile() Built-ins
- Many tests use `compile()` to test syntax validation
- `eval()` used to evaluate string expressions at runtime

## Recommended Immediate Actions (Priority Order)

### Phase 1: Built-in Exceptions (1-2 hours)
1. Add all Python built-in exceptions to NajaBuiltins.cs
2. Make them accessible as module-level names
3. Verify test_augassign.py passes

### Phase 2: Basic unittest Support (2-3 hours)
1. Create minimal NajaUnittest module
2. Implement TestCase with basic assertion methods
3. Add test discovery via Module.RunTests()
4. Get 5-10 CPython tests passing

### Phase 3: eval() and compile() (1-2 hours)
1. Implement eval(string) as Engine.Eval()
2. Implement compile(string, filename, mode) 
3. This unblocks syntax validation tests

### Phase 4: F-string Parsing (2-3 hours)
1. Review f-string tokenization
2. Handle line continuations in f-strings
3. Get test_grammar.py and test_fstring.py to pass

## Success Metrics
- Phase 1: 1-2 tests pass (test_augassign minimal subset)
- Phase 2: 10-20 tests pass
- Phase 3: 20-30 tests pass (syntax validation tests)
- Phase 4: 50+ tests pass

## Test Execution Notes
- CPython tests in F:\Sources\cpython\Lib\test\
- Most are unittest-based, but can be executed via Engine.Eval()
- Each test file runs as a module with __main__ guard

# Naja Compiler - Comprehensive Refactoring Plan

## Executive Summary

The Naja compiler codebase currently suffers from monolithic file organization with several files exceeding 1500-2400 lines. This makes maintenance, feature development, and bug fixes challenging as developers must navigate through large files to find relevant code. This document provides a comprehensive, developer-friendly refactoring plan to reorganize the codebase into logical, feature-focused modules.

---

## Current State Analysis

### File Size Distribution

| File | Lines | Project | Status |
|------|-------|---------|--------|
| ExpressionEmitter.cs | 2375 | Naja.CodeGen | 🔴 Critical - Needs refactoring |
| NajaBuiltins.cs | 2183 | Naja.CodeGen | 🔴 Critical - Needs refactoring |
| AssemblyEmitter.cs | 1646 | Naja.CodeGen | 🟠 High Priority |
| StatementEmitter.cs | 1551 | Naja.CodeGen | 🟠 High Priority |
| Parser.cs | 1288 | Naja.Parser | 🟡 Medium Priority |
| SemanticAnalyzer.cs | 602 | Naja.Semantics | 🟢 Acceptable |
| Lexer.cs | 419 | Naja.Lexer | 🟢 Good |

### Problem Areas

1. **ExpressionEmitter.cs (2375 lines)** - Contains all expression IL emission logic in one file
2. **NajaBuiltins.cs (2183 lines)** - All Python builtin functions in a single static class
3. **AssemblyEmitter.cs (1646 lines)** - Handles module, class, method, and PE assembly generation
4. **StatementEmitter.cs (1551 lines)** - All statement types in one emitter
5. **Parser.cs (1288 lines)** - All parsing logic for expressions, statements, and patterns

---

## Refactoring Strategy

### Guiding Principles

1. **Feature-Oriented Organization** - Group code by feature/domain, not by technical layer
2. **Single Responsibility** - Each file should have one clear purpose
3. **Discoverability** - Developers should instantly know which file to edit for a feature
4. **Maintainability** - Keep files between 200-500 lines (max 600)
5. **Testability** - Smaller, focused files are easier to unit test
6. **Backward Compatibility** - Maintain public APIs during refactoring

---

## Detailed Refactoring Plans

## 1. ExpressionEmitter.cs → Expression Emitters Module

### Current Responsibility
Single 2375-line file handling all expression types (literals, operators, calls, comprehensions, etc.)

### Target Structure
```
Naja.CodeGen/
└── Emitters/
    └── Expressions/
        ├── ExpressionEmitterBase.cs          (~150 lines) - Base class with shared utilities
        ├── LiteralEmitters.cs                (~200 lines) - Int, Float, String, Bool, None, Ellipsis
        ├── CollectionEmitters.cs             (~300 lines) - List, Tuple, Dict, Set
        ├── OperatorEmitters.cs               (~400 lines) - Binary, Unary, BoolOp, Compare
        ├── CallEmitters.cs                   (~350 lines) - Call, MethodCall, BuiltinCall
        ├── AttributeEmitters.cs              (~200 lines) - Attribute, Subscript, Slice
        ├── ControlFlowEmitters.cs            (~180 lines) - IfExpr, Walrus
        ├── ComprehensionEmitters.cs          (~350 lines) - ListComp, SetComp, DictComp
        ├── GeneratorEmitters.cs              (~200 lines) - GeneratorExpr, Yield, YieldFrom
        └── LambdaEmitter.cs                  (~150 lines) - Lambda expression handling
```

### Key Methods Migration

**LiteralEmitters.cs**
- `EmitInt()`, `EmitFloat()`, `EmitString()`, `EmitFString()`
- `EmitBool()`, `EmitNone()`, `EmitEllipsis()`

**CollectionEmitters.cs**
- `EmitList()`, `EmitTuple()`, `EmitDict()`, `EmitSet()`
- `EmitStarred()` - starred expressions in collections

**OperatorEmitters.cs**
- `EmitBinary()`, `EmitUnary()`, `EmitBoolOp()`
- `EmitCompare()`, `EmitSingleComparison()`, `EmitCompareOp()`

**CallEmitters.cs**
- `EmitCall()`, `EmitBuiltinCall()`, `EmitMethodCall()`
- Parameter packing/unpacking logic

**AttributeEmitters.cs**
- `EmitAttribute()`, `EmitSubscript()`
- `EmitSlice()`, `EmitSlicePart()`

**ControlFlowEmitters.cs**
- `EmitIfExpr()` - ternary expressions
- `EmitWalrus()` - walrus operator (:=)

**ComprehensionEmitters.cs**
- `EmitListComp()`, `EmitSetComp()`, `EmitDictComp()`
- Shared comprehension iteration logic

**GeneratorEmitters.cs**
- `EmitGenerator()`, `EmitYield()`, `EmitYieldFrom()`

**LambdaEmitter.cs**
- `EmitLambda()` - inline function creation

### Migration Strategy
1. Create `ExpressionEmitterBase` with shared context and utilities
2. Create partial classes first to test compilation
3. Move methods group by group with tests after each
4. Update main `ExpressionEmitter` to dispatch to specialized emitters

---

## 2. NajaBuiltins.cs → Builtins Module

### Current Responsibility
Single 2183-line static class with all Python builtin function implementations

### Target Structure
```
Naja.CodeGen/
└── Builtins/
    ├── NajaBuiltins.cs                   (~100 lines) - Main entry point & registration
    ├── DynamicOperators.cs               (~400 lines) - DynamicAdd, DynamicSub, DynamicMul, etc.
    ├── TypeSystem.cs                     (~300 lines) - Type resolution, coercion, isinstance
    ├── Collections.cs                    (~350 lines) - len, range, enumerate, zip, map, filter
    ├── Iterators.cs                      (~200 lines) - GetEnumerator, iterator protocols
    ├── IOFunctions.cs                    (~200 lines) - print, input, open, file operations
    ├── StringFunctions.cs                (~150 lines) - chr, ord, format, string operations
    ├── MathFunctions.cs                  (~250 lines) - abs, round, pow, divmod, min, max, sum
    ├── ComparisonOperators.cs            (~200 lines) - DynamicEq, DynamicLt, DynamicGt, etc.
    └── ReflectionHelpers.cs              (~200 lines) - GetAttr, SetAttr, DynamicCall, etc.
```

### Key Functions Migration

**DynamicOperators.cs**
- `DynamicAdd()`, `DynamicSub()`, `DynamicMul()`, `DynamicMod()`
- `PyFloorDiv()`, `PyFloorDivF()`, `PyMod()`, `PyModF()`
- Dunder method dispatch logic (__add__, __radd__, etc.)

**TypeSystem.cs**
- `ResolveTypeByName()`, `CoerceValue()`
- `CreateDotNet()` - .NET object instantiation
- Type checking and conversion utilities

**Collections.cs**
- `Len()`, `Range()`, `Enumerate()`, `Zip()`
- `Map()`, `Filter()`, `Any()`, `All()`
- `Reversed()`, `Sorted()`

**Iterators.cs**
- `GetEnumerator()`, `GetIteratorFromResult()`
- Iterator protocol implementations
- `StopIteration` handling

**IOFunctions.cs**
- `Print()`, `Input()`, `Open()`
- File operations and context managers

**StringFunctions.cs**
- `Chr()`, `Ord()`, `Hex()`, `Bin()`, `Oct()`
- String formatting and manipulation

**MathFunctions.cs**
- `Abs()`, `Round()`, `Pow()`, `DivMod()`
- `Min()`, `Max()`, `Sum()`

**ComparisonOperators.cs**
- `DynamicEq()`, `DynamicNotEq()`
- `DynamicLt()`, `DynamicLtEq()`, `DynamicGt()`, `DynamicGtEq()`

**ReflectionHelpers.cs**
- `GetAttr()`, `SetAttr()`, `GetItem()`, `SetItem()`
- `DynamicCall()`, `StaticCall()`
- `AddEventHandler()`, `RemoveEventHandler()`

### Migration Strategy
1. Create domain-specific static classes
2. Move functions in logical groups
3. Update IL emitter call sites to use new class names
4. Keep `NajaBuiltins` as a facade with references to new classes (optional)

---

## 3. AssemblyEmitter.cs → Assembly Emission Module

### Current Responsibility
Single 1646-line class handling module, class, method emission and PE generation

### Target Structure
```
Naja.CodeGen/
└── Emitters/
    └── Assembly/
        ├── AssemblyEmitter.cs            (~300 lines) - Orchestrator & public API
        ├── ModuleEmitter.cs              (~300 lines) - Module-level code emission
        ├── ClassEmitter.cs               (~400 lines) - Class declaration & body emission
        ├── MethodEmitter.cs              (~350 lines) - Method/function body emission
        ├── TypeDeclaration.cs            (~200 lines) - Pass 1: Type stub declarations
        └── PEBuilder.cs                  (~300 lines) - PE file generation & assembly building
```

### Key Responsibilities Migration

**AssemblyEmitter.cs** (Orchestrator)
- Public API: `EmitToFile()`, `EmitToMemory()`
- Coordinate three-pass compilation
- Hold shared registries (_classTypes, _classMethods, etc.)

**ModuleEmitter.cs**
- `EmitModule()` - Module-level TypeBuilder creation
- Module field and method declaration
- Main() entry point generation
- WinForms preamble emission

**ClassEmitter.cs**
- `DeclareClass()` - Pass 1 class stub creation
- `EmitClassBody()` - Pass 3 class implementation
- Constructor emission and completion
- Instance field handling

**MethodEmitter.cs**
- `EmitMethodBody()` - Class method bodies
- `EmitFunctionBody()` - Module-level functions
- Parameter handling and locals management

**TypeDeclaration.cs**
- Pass 1 logic: Declare all stubs
- Field, method signature declarations
- Forward reference resolution
- Symbol registry population

**PEBuilder.cs**
- `PersistedAssemblyBuilder` configuration
- Metadata generation
- Reference assembly resolution
- PE file writing

### Migration Strategy
1. Extract PE building logic first (most independent)
2. Extract type declaration pass
3. Split class and method emission
4. Update orchestrator to coordinate new classes

---

## 4. StatementEmitter.cs → Statement Emitters Module

### Current Responsibility
Single 1551-line class handling all statement types

### Target Structure
```
Naja.CodeGen/
└── Emitters/
    └── Statements/
        ├── StatementEmitterBase.cs       (~200 lines) - Base dispatcher & utilities
        ├── AssignmentEmitters.cs         (~300 lines) - Assign, AnnAssign, AugAssign
        ├── ControlFlowEmitters.cs        (~350 lines) - If, While, For, Match
        ├── ExceptionEmitters.cs          (~300 lines) - Try, Raise, Assert
        ├── DefinitionEmitters.cs         (~250 lines) - FunctionDef, ClassDef
        └── ScopeEmitters.cs              (~200 lines) - With, Global, Nonlocal
```

### Key Methods Migration

**StatementEmitterBase.cs**
- Main `Emit()` dispatcher
- `EmitAll()` statement list processor
- Shared utilities: `CollectAssignedNames()`, `CollectReferencedNames()`

**AssignmentEmitters.cs**
- `Emit(AssignStatement)`
- `Emit(AnnAssignStatement)`, `Emit(AugAssignStatement)`
- `EmitStore()`, `EmitUnpackTarget()`

**ControlFlowEmitters.cs**
- `Emit(IfStatement)`, `Emit(WhileStatement)`, `Emit(ForStatement)`
- `Emit(BreakStatement)`, `Emit(ContinueStatement)`
- `Emit(MatchStatement)`, `EmitPatternCheck()`

**ExceptionEmitters.cs**
- `Emit(TryStatement)` - try/except/finally handling
- `Emit(RaiseStatement)` - exception raising
- `Emit(AssertStatement)` - assertion checking

**DefinitionEmitters.cs**
- `Emit(FunctionDef)` - function declarations
- `Emit(ClassDef)` - class declarations
- Decorator handling

**ScopeEmitters.cs**
- `Emit(WithStatement)` - context manager protocol
- `Emit(GlobalStatement)`, `Emit(NonlocalStatement)`
- Scope management logic

### Migration Strategy
1. Create base class with dispatcher
2. Move statement handlers group by group
3. Test each category independently
4. Update dispatcher to route to specialized emitters

---

## 5. Parser.cs → Parser Module

### Current Responsibility
Single 1288-line class parsing all constructs

### Target Structure
```
Naja.Parser/
├── Parser.cs                         (~200 lines) - Main orchestrator & utilities
├── ExpressionParser.cs               (~400 lines) - All expression parsing
├── StatementParser.cs                (~350 lines) - All statement parsing
├── PatternParser.cs                  (~200 lines) - Pattern matching (match/case)
└── ParserHelpers.cs                  (~200 lines) - Shared utilities & token management
```

### Key Methods Migration

**Parser.cs** (Orchestrator)
- `ParseModule()` - Entry point
- Token management: `Current()`, `Advance()`, `Match()`, `Check()`
- `ParseStatementList()`, `ParseBlock()`

**ExpressionParser.cs**
- `ParseExpression()`, `ParseTernary()`, `ParseOr()`, `ParseAnd()`
- `ParseComparison()`, `ParseBitOr()`, `ParseBitXor()`, `ParseBitAnd()`
- `ParseShift()`, `ParseArith()`, `ParseTerm()`, `ParseFactor()`
- `ParsePower()`, `ParsePrimary()`, `ParseAtom()`
- `ParseCall()`, `ParseSubscript()`, `ParseSlice()`

**StatementParser.cs**
- `ParseStatement()` - Main dispatcher
- `ParseIf()`, `ParseWhile()`, `ParseFor()`, `ParseTry()`
- `ParseFunctionDef()`, `ParseClassDef()`
- `ParseReturn()`, `ParseRaise()`, `ParseAssert()`
- `ParseImport()`, `ParseFromImport()`

**PatternParser.cs**
- `ParseMatch()`, `ParseMatchCase()`
- `ParsePattern()`, `ParseOrPattern()`, `ParseAsPattern()`
- `ParseCapturePattern()`, `ParseLiteralPattern()`

**ParserHelpers.cs**
- `Expect()`, `SkipNewlines()`, `SkipNewline()`
- `IsAtEnd()`, `CheckNewlineOrEof()`
- Error reporting utilities
- Precedence tables and operator mappings

### Migration Strategy
1. Extract helper methods first (most reused)
2. Create partial Parser class to maintain state
3. Move parsing methods to specialized classes
4. Update public API to delegate to specialists

---

## Implementation Phases

### Phase 1: Foundation (Week 1)
- Create new directory structures
- Set up base classes and interfaces
- Create template files with method signatures

### Phase 2: Code Generation Layer (Week 2-3)
- Refactor ExpressionEmitter (largest file, highest priority)
- Refactor NajaBuiltins (independent, clear boundaries)
- Update call sites and test

### Phase 3: Assembly Layer (Week 4)
- Refactor AssemblyEmitter and StatementEmitter
- These are closely coupled, handle together
- Ensure three-pass compilation still works

### Phase 4: Parser Layer (Week 5)
- Refactor Parser.cs (most independent of CodeGen changes)
- Update parser tests

### Phase 5: Validation & Documentation (Week 6)
- Run full test suite (unit + integration)
- Performance benchmarking
- Update documentation and architecture diagrams

---

## Testing Strategy

### During Refactoring
1. **Unit Test per Module** - Write tests for each new extracted class
2. **Incremental Validation** - Run tests after each file split
3. **Integration Checkpoints** - Full test suite after each phase

### Test Coverage Requirements
- Existing test suite must pass 100%
- No behavioral changes during refactoring
- Add tests for any uncovered edge cases discovered

### Regression Prevention
- Run Naja.CodeGen.Tests suite after each change
- Run Naja.Parser.Tests suite for parser changes
- Run Naja.WinForms.Tests for integration validation

---

## Migration Checklist per File

- [ ] Create new file with appropriate namespace
- [ ] Copy methods to new file
- [ ] Update using statements
- [ ] Update internal method calls
- [ ] Update references in original file (or delete original if fully migrated)
- [ ] Compile and fix errors
- [ ] Run relevant tests
- [ ] Update any documentation
- [ ] Review with team member

---

## Risk Mitigation

### Risks
1. **Breaking Changes** - Refactoring might introduce bugs
2. **Performance Regression** - Multiple files might impact load time
3. **Merge Conflicts** - Active development might conflict
4. **Test Coverage Gaps** - Missing tests might not catch regressions

### Mitigation Strategies
1. **Feature Branch** - Do all work in dedicated branch
2. **Incremental Commits** - Small, testable commits
3. **Automated Testing** - CI/CD pipeline runs all tests
4. **Code Review** - Team review before merging
5. **Performance Benchmarks** - Before/after metrics
6. **Rollback Plan** - Keep main branch stable

---

## Expected Benefits

### Developer Experience
- ✅ **Fast File Discovery** - Know exactly which file to edit for a feature
- ✅ **Smaller Pull Requests** - Changes touch fewer lines, easier reviews
- ✅ **Parallel Development** - Multiple developers, less merge conflicts
- ✅ **Easier Onboarding** - New developers understand structure quickly

### Code Quality
- ✅ **Testability** - Smaller files = easier unit testing
- ✅ **Maintainability** - Clear responsibilities per file
- ✅ **Reusability** - Extracted utilities can be reused
- ✅ **Debuggability** - Shorter call stacks, clearer traces

### Long-term Benefits
- ✅ **Scalability** - Easy to add new expression/statement types
- ✅ **Documentation** - File organization self-documents architecture
- ✅ **Performance** - Easier to optimize specific emitters
- ✅ **Extensibility** - Plugin system becomes possible

---

## Success Metrics

### Quantitative
- No files > 600 lines (target: 200-500)
- Test coverage maintained or improved (current: >80%, target: >85%)
- Build time unchanged (±5%)
- Test execution time unchanged (±10%)

### Qualitative
- Developer feedback: "I can find code faster"
- Code review velocity improved
- Bug fix time reduced
- Feature development time improved

---

## File Organization Reference

### Before Refactoring
```
Naja.CodeGen/
├── NajaEngine.cs               (163 lines)
├── ExpressionEmitter.cs        (2375 lines) ❌
├── StatementEmitter.cs         (1551 lines) ❌
├── AssemblyEmitter.cs          (1646 lines) ❌
├── NajaBuiltins.cs             (2183 lines) ❌
├── TypeMapper.cs               (220 lines)
├── EmitContext.cs              (135 lines)
├── LocalsManager.cs            (72 lines)
├── ILDumper.cs                 (329 lines)
├── NajaFunction.cs             (41 lines)
└── NajaSlice.cs                (55 lines)
```

### After Refactoring
```
Naja.CodeGen/
├── NajaEngine.cs
├── TypeMapper.cs
├── EmitContext.cs
├── LocalsManager.cs
├── ILDumper.cs
├── NajaFunction.cs
├── NajaSlice.cs
├── Builtins/
│   ├── NajaBuiltins.cs
│   ├── DynamicOperators.cs
│   ├── TypeSystem.cs
│   ├── Collections.cs
│   ├── Iterators.cs
│   ├── IOFunctions.cs
│   ├── StringFunctions.cs
│   ├── MathFunctions.cs
│   ├── ComparisonOperators.cs
│   └── ReflectionHelpers.cs
└── Emitters/
    ├── Assembly/
    │   ├── AssemblyEmitter.cs
    │   ├── ModuleEmitter.cs
    │   ├── ClassEmitter.cs
    │   ├── MethodEmitter.cs
    │   ├── TypeDeclaration.cs
    │   └── PEBuilder.cs
    ├── Expressions/
    │   ├── ExpressionEmitterBase.cs
    │   ├── LiteralEmitters.cs
    │   ├── CollectionEmitters.cs
    │   ├── OperatorEmitters.cs
    │   ├── CallEmitters.cs
    │   ├── AttributeEmitters.cs
    │   ├── ControlFlowEmitters.cs
    │   ├── ComprehensionEmitters.cs
    │   ├── GeneratorEmitters.cs
    │   └── LambdaEmitter.cs
    └── Statements/
        ├── StatementEmitterBase.cs
        ├── AssignmentEmitters.cs
        ├── ControlFlowEmitters.cs
        ├── ExceptionEmitters.cs
        ├── DefinitionEmitters.cs
        └── ScopeEmitters.cs

Naja.Parser/
├── Parser.cs
├── ExpressionParser.cs
├── StatementParser.cs
├── PatternParser.cs
├── ParserHelpers.cs
├── Expressions.cs
├── Statements.cs
└── ParseException.cs
```

---

## Next Steps

1. **Team Review** - Review this plan with the team, get feedback
2. **Timeline Agreement** - Agree on implementation timeline
3. **Branch Creation** - Create `refactor/codebase-organization` branch
4. **Phase 1 Start** - Begin with foundation work
5. **Weekly Checkpoints** - Review progress, adjust as needed

---

## Appendix: Quick Reference

### Finding Code After Refactoring

| I need to... | Old File | New File |
|-------------|----------|----------|
| Emit `x + y` | ExpressionEmitter.cs | Emitters/Expressions/OperatorEmitters.cs |
| Emit `[1, 2, 3]` | ExpressionEmitter.cs | Emitters/Expressions/CollectionEmitters.cs |
| Emit `print()` call | ExpressionEmitter.cs | Emitters/Expressions/CallEmitters.cs |
| Implement `len()` | NajaBuiltins.cs | Builtins/Collections.cs |
| Implement `print()` | NajaBuiltins.cs | Builtins/IOFunctions.cs |
| Implement `abs()` | NajaBuiltins.cs | Builtins/MathFunctions.cs |
| Emit `if` statement | StatementEmitter.cs | Emitters/Statements/ControlFlowEmitters.cs |
| Emit `try/except` | StatementEmitter.cs | Emitters/Statements/ExceptionEmitters.cs |
| Emit `x = 5` | StatementEmitter.cs | Emitters/Statements/AssignmentEmitters.cs |
| Parse expression | Parser.cs | ExpressionParser.cs |
| Parse statement | Parser.cs | StatementParser.cs |
| Parse match/case | Parser.cs | PatternParser.cs |
| Emit class body | AssemblyEmitter.cs | Emitters/Assembly/ClassEmitter.cs |
| Build PE file | AssemblyEmitter.cs | Emitters/Assembly/PEBuilder.cs |

---

*Document Version: 1.0*  
*Created: January 2026*  
*Author: Naja Development Team*  
*Status: Proposal - Pending Team Review*

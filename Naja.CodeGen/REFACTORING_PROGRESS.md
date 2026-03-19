# Naja Compiler Refactoring - Implementation Progress

## Overview

This document tracks the implementation of the Naja compiler refactoring outlined in `REFACTORING_PLAN.md`. The goal is to break down monolithic files (2300-2400 lines) into focused, maintainable modules.

## Current Status

### Phase 1: Foundation ✅ (Completed)

**Goal**: Create directory structures and base classes for refactoring.

- ✅ Created `Naja.CodeGen/Emitters/Expressions/` directory
- ✅ Created `Naja.CodeGen/Emitters/Statements/` directory  
- ✅ Created `Naja.CodeGen/Emitters/Assembly/` directory
- ✅ Created `Naja.CodeGen/Builtins/` directory
- ✅ Created `ExpressionEmitterBase.cs` - Base class for expression emitters
- ✅ Created `LiteralEmitters.cs` - Int, Float, String, Bool, None, Ellipsis literals
- ✅ Created `CollectionEmitters.cs` - List, Tuple, Dict, Set collections

### Phase 2: Expression Emission Layer (🔄 In Progress)

**Goal**: Refactor `ExpressionEmitter.cs` (2375 lines → ~1800 lines) into focused specialists.

#### Completed Extractions

| File | Purpose | Lines | Status | Commit |
|------|---------|-------|--------|--------|
| `ExpressionEmitterBase.cs` | Base class with IL utilities | 60 | ✅ Created | de2620f |
| `LiteralEmitters.cs` | Int, Float, String, Bool, None, Ellipsis | 85 | ✅ Created | de2620f |
| `CollectionEmitters.cs` | List, Tuple, Dict, Set | 90 | ✅ Created | de2620f |
| `OperatorEmitters.cs` | Binary, Unary, BoolOp, Compare | 475 | ✅ Done | 4b3bf51 |

#### Planned Extractions

| File | Purpose | Est. Lines | Status |
|------|---------|-----------|--------|
| `CallEmitters.cs` | Call, MethodCall, BuiltinCall | 400 | 📋 Next |
| `AttributeEmitters.cs` | Attribute, Subscript, Slice | 250 | 📋 Planned |
| `ControlFlowEmitters.cs` | IfExpr, Walrus | 180 | 📋 Planned |
| `ComprehensionEmitters.cs` | ListComp, SetComp, DictComp | 500 | 📋 Planned |
| `GeneratorEmitters.cs` | GeneratorExpr, Yield, YieldFrom | 200 | 📋 Planned |
| `LambdaEmitter.cs` | Lambda expression handling | 150 | 📋 Planned |
| `FStringEmitter.cs` | FString, format spec parsing | 200 | 📋 Planned |
| `NameEmitter.cs` | Name resolution, scope handling | 200 | 📋 Planned |

#### Phase 2 Progress

- **Commits Completed**: 2 (de2620f: foundation, 4b3bf51: operators)
- **Lines Removed from Monolith**: 540 lines
- **Lines Added to Specialists**: 475 lines (OperatorEmitters)
- **Test Results**: 397 passed (↑ +6 from baseline), 66 failed (↓ -6), 4 skipped
- **Current ExpressionEmitter Size**: ~1835 lines (was 2375)
- **Reduction Progress**: 23% complete (540 of 2375 lines extracted)

**Next Step**: Extract `CallEmitters.cs` (EmitCall, EmitBuiltinCall, EmitMethodCall methods)

### Phase 3: Builtin Functions (🔄 In Progress)

**Goal**: Refactor `NajaBuiltins.cs` (2183 lines) into domain-specific modules.

#### Files Created

| File | Purpose | Est. Lines | Status |
|------|---------|-----------|--------|
| `DynamicOperators.cs` | Dynamic arithmetic with dunder dispatch | 150 | ✅ Created |
| `Collections.cs` | len, range, enumerate, zip, map, filter, any, all, sorted, reversed | 250 | ✅ Created |
| `ComparisonOperators.cs` | ==, !=, <, >, <=, >= with container support | 80 | ✅ Created |
| `TypeSystem.cs` | Type resolution, coercion, isinstance | 300 | 📋 Planned |
| `Iterators.cs` | Iterator protocol implementations | 200 | 📋 Planned |
| `IOFunctions.cs` | print, input, open, file operations | 200 | 📋 Planned |
| `StringFunctions.cs` | chr, ord, format, string operations | 150 | 📋 Planned |
| `MathFunctions.cs` | abs, round, pow, divmod, min, max, sum | 250 | 📋 Planned |
| `ReflectionHelpers.cs` | getattr, setattr, dynamic calls | 200 | 📋 Planned |

### Phase 4: Statement Emission (📋 Planned)

**Goal**: Refactor `AssemblyEmitter.cs` (1646 lines) and `StatementEmitter.cs` (1551 lines).

### Phase 5: Parser Refactoring (📋 Planned)

**Goal**: Refactor `Parser.cs` (1288 lines) into specialized parsers.

---

## Key Design Decisions

### 1. **Keep Existing Files During Transition**
   - The original `ExpressionEmitter.cs`, `NajaBuiltins.cs`, etc. remain intact while new modules are created
   - This ensures the build stays green throughout refactoring
   - Original files gradually transition to "orchestrator" roles

### 2. **Incremental Extraction**
   - One specialized emitter per commit
   - Full test coverage for each extraction
   - Minimal risk of regressions

### 3. **Shared Base Classes**
   - `ExpressionEmitterBase` - Common IL generation utilities
   - `StatementEmitterBase` - Common dispatcher and utilities  
   - Reduces code duplication

### 4. **Dependency on Main Emitter**
   - Specialized emitters accept the main `ExpressionEmitter` instance
   - Allows calling `mainEmitter.Emit()` for nested expressions
   - Avoids circular dependencies

---

## Testing Strategy

### Before Refactoring Each Module

```bash
cd F:\Sources\Naja
dotnet build
dotnet test Naja.CodeGen.Tests
dotnet test Naja.CodeGen.Tests/LanguageCompliance
```

### After Each Extraction

1. Run full build: `dotnet build`
2. Run unit tests: `dotnet test`
3. Verify no behavioral changes

---

## File Structure After Refactoring

```
Naja.CodeGen/
├── NajaEngine.cs
├── TypeMapper.cs
├── EmitContext.cs
├── LocalsManager.cs
├── ILDumper.cs
├── NajaFunction.cs
├── NajaSlice.cs
├── INDEX.md                                 ✅ Navigation guide
├── REFACTORING_PROGRESS.md                  ✅ This file
├── Emitters/
│   ├── Expressions/
│   │   ├── ExpressionEmitterBase.cs          ✅
│   │   ├── LiteralEmitters.cs                ✅
│   │   ├── CollectionEmitters.cs             ✅
│   │   ├── OperatorEmitters.cs               📋
│   │   ├── CallEmitters.cs                   📋
│   │   ├── AttributeEmitters.cs              📋
│   │   ├── ControlFlowEmitters.cs            📋
│   │   ├── ComprehensionEmitters.cs          📋
│   │   ├── GeneratorEmitters.cs              📋
│   │   └── LambdaEmitter.cs                  📋
│   ├── Statements/
│   │   ├── StatementEmitterBase.cs           ✅
│   │   ├── AssignmentEmitters.cs             📋
│   │   ├── ControlFlowEmitters.cs            📋
│   │   ├── ExceptionEmitters.cs              📋
│   │   ├── DefinitionEmitters.cs             📋
│   │   └── ScopeEmitters.cs                  📋
│   └── Assembly/
│       ├── AssemblyEmitter.cs                📋
│       ├── ModuleEmitter.cs                  📋
│       ├── ClassEmitter.cs                   📋
│       ├── MethodEmitter.cs                  📋
│       ├── TypeDeclaration.cs                📋
│       └── PEBuilder.cs                      📋
└── Builtins/
    ├── DynamicOperators.cs                   ✅
    ├── Collections.cs                        ✅
    ├── ComparisonOperators.cs                ✅
    ├── TypeSystem.cs                         📋
    ├── Iterators.cs                          📋
    ├── IOFunctions.cs                        📋
    ├── StringFunctions.cs                    📋
    ├── MathFunctions.cs                      📋
    └── ReflectionHelpers.cs                  📋
```

---

## How to Contribute

When extracting methods from large files:

1. **Identify logical groups** - Methods that work together (e.g., all binary operators)
2. **Create specialist class** - New file in appropriate subdirectory
3. **Copy methods** - Extract with full logic intact
4. **Update calling sites** - Change main emitter to delegate
5. **Test thoroughly** - Run full suite after each extraction
6. **Commit incrementally** - One extraction per commit

---

## References

- Main Plan: `REFACTORING_PLAN.md`
- Branch: `refactor/codebase-organization`
- Target Framework: .NET 10

---

## Legend

| Symbol | Status |
|--------|--------|
| ✅ | Completed |
| 🔄 | In Progress |
| 📋 | Planned |
| ❌ | Blocked |

---

*Last Updated: 2026-03-19*  
*Phase: 2-3 (Expression & Builtin Layers - Foundation Complete)*  
*Completed: 9 specialist modules + 2 base classes + documentation*  
*Build Status: ✅ Successful*

# Naja Compiler Codebase Organization Index

## Quick Navigation

### Expression Emission (`Naja.CodeGen/Emitters/Expressions/`)

| File | Purpose | Methods | Status |
|------|---------|---------|--------|
| `ExpressionEmitterBase.cs` | Base class for expression emitters | IL utilities (EmitLabel, DefineLabel, EmitBranch*) | ✅ |
| `LiteralEmitters.cs` | Literal values: int, float, string, bool, None | EmitInt, EmitFloat, EmitString, EmitBool, EmitNone | ✅ |
| `CollectionEmitters.cs` | Collections: list, tuple, dict, set | EmitList, EmitTuple, EmitDict, EmitSet | ✅ |
| `OperatorEmitters.cs` *(Planned)* | Operators: binary, unary, bool, compare | EmitBinary, EmitUnary, EmitBoolOp, EmitCompare | 📋 |
| `CallEmitters.cs` *(Planned)* | Function calls and method calls | EmitCall, EmitBuiltinCall, EmitMethodCall | 📋 |
| `AttributeEmitters.cs` *(Planned)* | Attribute access, subscripts, slices | EmitAttribute, EmitSubscript, EmitSlice | 📋 |
| `ControlFlowEmitters.cs` *(Planned)* | Control flow: if-else, walrus operator | EmitIfExpr, EmitWalrus | 📋 |
| `ComprehensionEmitters.cs` *(Planned)* | List/dict/set comprehensions | EmitListComp, EmitSetComp, EmitDictComp | 📋 |
| `GeneratorEmitters.cs` *(Planned)* | Generators and yield expressions | EmitGenerator, EmitYield, EmitYieldFrom | 📋 |
| `LambdaEmitter.cs` *(Planned)* | Lambda expressions | EmitLambda | 📋 |

### Statement Emission (`Naja.CodeGen/Emitters/Statements/`)

| File | Purpose | Methods | Status |
|------|---------|---------|--------|
| `StatementEmitterBase.cs` | Base class for statement emitters | Emit, EmitAll, IL utilities | ✅ |
| `AssignmentEmitters.cs` *(Planned)* | Assignments: simple, annotated, augmented | EmitAssign, EmitAnnAssign, EmitAugAssign | 📋 |
| `ControlFlowEmitters.cs` *(Planned)* | Control flow: if, while, for, break, continue | EmitIf, EmitWhile, EmitFor, EmitBreak, EmitContinue | 📋 |
| `ExceptionEmitters.cs` *(Planned)* | Exception handling: try/except, raise, assert | EmitTry, EmitRaise, EmitAssert | 📋 |
| `DefinitionEmitters.cs` *(Planned)* | Function and class definitions | EmitFunctionDef, EmitClassDef | 📋 |
| `ScopeEmitters.cs` *(Planned)* | Scope management: with, global, nonlocal | EmitWith, EmitGlobal, EmitNonlocal | 📋 |

### Assembly Emission (`Naja.CodeGen/Emitters/Assembly/`)

| File | Purpose | Status |
|------|---------|--------|
| `AssemblyEmitter.cs` *(Planned)* | Main orchestrator for assembly emission | 📋 |
| `ModuleEmitter.cs` *(Planned)* | Module-level code emission | 📋 |
| `ClassEmitter.cs` *(Planned)* | Class declaration and body emission | 📋 |
| `MethodEmitter.cs` *(Planned)* | Method/function body emission | 📋 |
| `TypeDeclaration.cs` *(Planned)* | Type stub declarations (Pass 1) | 📋 |
| `PEBuilder.cs` *(Planned)* | PE file generation and assembly building | 📋 |

### Builtin Functions (`Naja.CodeGen/Builtins/`)

| File | Purpose | Status |
|------|---------|--------|
| `DynamicOperators.cs` | Dynamic arithmetic: +, -, *, /, %, ** with dunder dispatch | ✅ |
| `Collections.cs` | Collections: len, range, enumerate, zip, map, filter, any, all, reversed, sorted | ✅ |
| `ComparisonOperators.cs` | Dynamic comparisons: ==, !=, <, >, <=, >= | ✅ |
| `TypeSystem.cs` *(Planned)* | Type resolution, coercion, isinstance | 📋 |
| `Iterators.cs` *(Planned)* | Iterator protocol implementations | 📋 |
| `IOFunctions.cs` *(Planned)* | I/O: print, input, open, file operations | 📋 |
| `StringFunctions.cs` *(Planned)* | String functions: chr, ord, format, etc. | 📋 |
| `MathFunctions.cs` *(Planned)* | Math: abs, round, pow, divmod, min, max, sum | 📋 |
| `ReflectionHelpers.cs` *(Planned)* | Reflection: getattr, setattr, dynamic calls | 📋 |

### Original Files (Still in Root)

| File | Lines | Purpose | Planned Refactoring |
|------|-------|---------|---------------------|
| `ExpressionEmitter.cs` | 2375 | ❌ **TO BE REFACTORED** | ⚠️ Will delegate to Expressions/* |
| `NajaBuiltins.cs` | 2183 | ❌ **TO BE REFACTORED** | ⚠️ Will delegate to Builtins/* |
| `AssemblyEmitter.cs` | 1646 | ❌ **TO BE REFACTORED** | ⚠️ Will delegate to Assembly/* |
| `StatementEmitter.cs` | 1551 | ❌ **TO BE REFACTORED** | ⚠️ Will delegate to Statements/* |
| `NajaEngine.cs` | 163 | ✅ Good size | — |
| `TypeMapper.cs` | 220 | ✅ Good size | — |
| `EmitContext.cs` | 135 | ✅ Good size | — |
| `LocalsManager.cs` | 72 | ✅ Good size | — |
| `ILDumper.cs` | 329 | ✅ Acceptable | — |
| `NajaFunction.cs` | 41 | ✅ Minimal | — |
| `NajaSlice.cs` | 55 | ✅ Minimal | — |

---

## Finding Code After Refactoring

### I need to find / modify...

| Task | Old File | New File |
|------|----------|----------|
| Emit integer literal `42` | ExpressionEmitter.cs | Expressions/LiteralEmitters.cs |
| Emit list `[1,2,3]` | ExpressionEmitter.cs | Expressions/CollectionEmitters.cs |
| Emit binary operation `x + y` | ExpressionEmitter.cs | Expressions/OperatorEmitters.cs *(planned)* |
| Emit function call `f(x)` | ExpressionEmitter.cs | Expressions/CallEmitters.cs *(planned)* |
| Emit attribute access `obj.attr` | ExpressionEmitter.cs | Expressions/AttributeEmitters.cs *(planned)* |
| Emit if-expression `x if cond else y` | ExpressionEmitter.cs | Expressions/ControlFlowEmitters.cs *(planned)* |
| Emit list comprehension `[x for x in ...]` | ExpressionEmitter.cs | Expressions/ComprehensionEmitters.cs *(planned)* |
| Emit yield statement `yield x` | ExpressionEmitter.cs | Expressions/GeneratorEmitters.cs *(planned)* |
| Emit lambda `lambda x: x+1` | ExpressionEmitter.cs | Expressions/LambdaEmitter.cs *(planned)* |
| Implement `+` operator | NajaBuiltins.cs | Builtins/DynamicOperators.cs |
| Implement `len()` function | NajaBuiltins.cs | Builtins/Collections.cs |
| Implement comparison `==` | NajaBuiltins.cs | Builtins/ComparisonOperators.cs |
| Implement `range()` | NajaBuiltins.cs | Builtins/Collections.cs |
| Emit if statement `if x: ...` | StatementEmitter.cs | Statements/ControlFlowEmitters.cs *(planned)* |
| Emit assignment `x = 5` | StatementEmitter.cs | Statements/AssignmentEmitters.cs *(planned)* |
| Emit try/except | StatementEmitter.cs | Statements/ExceptionEmitters.cs *(planned)* |
| Emit for loop | StatementEmitter.cs | Statements/ControlFlowEmitters.cs *(planned)* |

---

## Refactoring Strategy

### Phase-by-Phase Approach

1. **Phase 1: Foundation** ✅
   - Create directory structures
   - Create base classes with utilities
   
2. **Phase 2: Expression Layer** 🔄 In Progress
   - Extract expression emitters to Expressions/*
   - Update ExpressionEmitter to delegate
   
3. **Phase 3: Statement Layer** 📋 Planned
   - Extract statement emitters to Statements/*
   - Update StatementEmitter to delegate
   
4. **Phase 4: Assembly Layer** 📋 Planned
   - Extract assembly/module/class/method emitters
   - Update AssemblyEmitter to delegate
   
5. **Phase 5: Builtins Layer** 📋 Planned
   - Extract builtin functions to Builtins/*
   - Update NajaBuiltins to delegate
   
6. **Phase 6: Parser Layer** 📋 Planned
   - Extract parser specialists to Naja.Parser/*
   - Update Parser to delegate

---

## Key Principles

### Incremental Refactoring
- ✅ Keep original files intact during refactoring
- ✅ New modules created before migration
- ✅ Build stays green throughout
- ✅ Tests verify behavior after each step

### Code Organization
- ✅ Each file handles one domain/feature
- ✅ Base classes provide shared utilities
- ✅ Clear responsibility boundaries
- ✅ Easy to find related code

### Dependency Management
- ✅ Minimize circular dependencies
- ✅ Use dependency injection (EmitContext)
- ✅ Share IL generation utilities via base classes
- ✅ Avoid deep inheritance chains

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Completed / Ready |
| 🔄 | In Progress |
| 📋 | Planned |
| ❌ | Critical - Needs Refactoring |
| ⚠️ | Will be updated during refactoring |

---

## Related Documentation

- `REFACTORING_PROGRESS.md` - Detailed progress tracking
- `REFACTORING_PLAN.md` - Original comprehensive plan
- `.najaproj` - Project configuration guide

---

*Last Updated: 2026-03-19*  
*Current Phase: 2 (Expression Emission Layer)*  
*Target: Complete modularization while maintaining 100% test coverage*

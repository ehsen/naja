# Phase 4 Refactoring: Before & After Architecture

## BEFORE: Monolithic StatementEmitter

```
StatementEmitter.cs (1551 lines)
├─ public void Emit(Statement stmt)
│  └─ 25 case branches (AssignStatement, AnnAssignStatement, ... MatchStatement)
│
├─ private void Emit(AssignStatement s)
├─ private void Emit(AnnAssignStatement s)
├─ private void Emit(AugAssignStatement s)
│  └─ 14 operator handlers (Add, Sub, Mul, Div, Mod, FloorDiv, Pow, ...)
├─ private void EmitStore(Expression target, NajaType valueType)
├─ private void EmitUnpackTarget(...)
│
├─ private void Emit(IfStatement s)
├─ private void Emit(WhileStatement s)
├─ private void Emit(ForStatement s)
├─ private void Emit(BreakStatement s)
├─ private void Emit(ContinueStatement s)
├─ private Stack<Label> _breakLabels
├─ private Stack<Label> _continueLabels
│
├─ private void Emit(ExprStatement s)
├─ private void Emit(ReturnStatement s)
├─ private void Emit(TryStatement s)
│  └─ Exception block management
├─ private void Emit(RaiseStatement s)
├─ private void Emit(AssertStatement s)
├─ private void Emit(WithStatement s)
├─ private List<Type> ResolveCatchTypes(ExceptHandler handler)
│
├─ private void Emit(FunctionDef s)
│  └─ Closure/hoisting helpers
├─ private void Emit(ClassDef s)
├─ private void Emit(NonlocalStatement s)
├─ private void Emit(MatchStatement s)
├─ private void EmitPatternCheck(Pattern pattern, ...)
│
├─ public static HashSet<string> CollectAssignedNames(...)
├─ public static HashSet<string> CollectReferencedNames(...)
├─ public static HashSet<string> CollectNamesReferencedByNestedFunctions(...)
├─ public static bool ContainsYield(...)
│
└─ [Helper methods: name collection, yield detection, scope analysis]

Problems:
⚠️ 1551 lines in single file
⚠️ Mixed concerns (assignments, control flow, exceptions, definitions)
⚠️ Complex label stack management interleaved
⚠️ Hard to find specific statement handler
⚠️ Difficult to unit test individual statement types
⚠️ High cognitive load when making changes
```

## AFTER: Refactored with Specialist Emitters

```
StatementEmitter.cs (220 lines - Dispatcher)
├─ public void Emit(Statement stmt)
│  ├─ case AssignStatement s:
│  │  └─ GetAssignmentEmitters().EmitAssign(s)
│  ├─ case AnnAssignStatement s:
│  │  └─ GetAssignmentEmitters().EmitAnnAssign(s)
│  ├─ case AugAssignStatement s:
│  │  └─ GetAssignmentEmitters().EmitAugAssign(s)
│  ├─ case IfStatement s:
│  │  └─ GetControlFlowEmitters().EmitIf(s)
│  ├─ case WhileStatement s:
│  │  └─ GetControlFlowEmitters().EmitWhile(s)
│  ├─ case ForStatement s:
│  │  └─ GetControlFlowEmitters().EmitFor(s)
│  ├─ case BreakStatement s:
│  │  └─ GetControlFlowEmitters().EmitBreak(s)
│  ├─ case ContinueStatement s:
│  │  └─ GetControlFlowEmitters().EmitContinue(s)
│  ├─ case ExprStatement s:
│  │  └─ GetScopeEmitters().EmitExprStatement(s)
│  ├─ case ReturnStatement s:
│  │  └─ GetScopeEmitters().EmitReturn(s)
│  ├─ case TryStatement s:
│  │  └─ GetExceptionEmitters().EmitTry(s)
│  ├─ case RaiseStatement s:
│  │  └─ GetExceptionEmitters().EmitRaise(s)
│  ├─ case AssertStatement s:
│  │  └─ GetExceptionEmitters().EmitAssert(s)
│  ├─ case WithStatement s:
│  │  └─ GetExceptionEmitters().EmitWith(s)
│  ├─ case FunctionDef s:
│  │  └─ GetDefinitionEmitters().EmitFunctionDef(s)
│  ├─ case ClassDef s:
│  │  └─ GetDefinitionEmitters().EmitClassDef(s)
│  ├─ case NonlocalStatement s:
│  │  └─ GetScopeEmitters().EmitNonlocal(s)
│  ├─ case MatchStatement s:
│  │  └─ EmitMatch(s)  [Handled locally - simple]
│  └─ [... other cases ...]
│
├─ private EmitMatch(MatchStatement s)
├─ private EmitPatternCheck(Pattern pattern, ...)
│
├─ private AssignmentEmitters GetAssignmentEmitters()
├─ private ControlFlowEmitters GetControlFlowEmitters()
├─ private ExceptionEmitters GetExceptionEmitters()
├─ private DefinitionEmitters GetDefinitionEmitters()
├─ private ScopeEmitters GetScopeEmitters()
│
└─ public void EmitAll(IReadOnlyList<Statement> stmts)

───────────────────────────────────────────────────────────

AssignmentEmitters.cs (430 lines)
├─ public void EmitAssign(AssignStatement s)
├─ public void EmitAnnAssign(AnnAssignStatement s)
├─ public void EmitAugAssign(AugAssignStatement s)
│  ├─ BinaryOp.Add handling
│  ├─ BinaryOp.Sub handling
│  ├─ BinaryOp.Mul handling
│  ├─ BinaryOp.Mod handling (with Python semantics)
│  ├─ BinaryOp.Div handling
│  ├─ BinaryOp.FloorDiv handling
│  ├─ Bitwise operations (And, Or, Xor, LShift, RShift)
│  └─ BinaryOp.Pow handling
├─ private void EmitStore(Expression target, NajaType valueType)
│  ├─ NameExpr handling
│  ├─ AttributeExpr handling
│  ├─ SubscriptExpr handling
│  └─ Unpacking (TupleExpr, ListExpr)
├─ private void EmitUnpackTarget(IReadOnlyList<Expression> elems, ...)
│  ├─ Simple unpack: a, b, c = iter
│  └─ Extended unpack: a, *rest, b = iter
└─ public override void Emit(Statement stmt) [Not used]

───────────────────────────────────────────────────────────

ControlFlowEmitters.cs (240 lines)
├─ public void EmitIf(IfStatement s)
│  └─ Handles if/elif/else chains
├─ public void EmitWhile(WhileStatement s)
│  ├─ Manages break labels
│  ├─ Manages continue labels
│  └─ Handles Python while/else semantics
├─ public void EmitFor(ForStatement s)
│  ├─ Enumerator pattern generation
│  ├─ Loop variable storage (value types vs object)
│  └─ Python for/else semantics
├─ public void EmitBreak(BreakStatement s)
│  └─ Validates loop context
├─ public void EmitContinue(ContinueStatement s)
│  └─ Validates loop context
├─ private Stack<Label> _breakLabels
├─ private Stack<Label> _continueLabels
├─ public Stack<Label> BreakLabels { get; }
├─ public Stack<Label> ContinueLabels { get; }
├─ private void EmitStoreInFor(Expression target, NajaType valueType)
└─ public override void Emit(Statement stmt) [Not used]

───────────────────────────────────────────────────────────

ExceptionEmitters.cs (290 lines)
├─ public void EmitTry(TryStatement s)
│  ├─ Nested exception block management
│  ├─ Handler filtering by exception type
│  ├─ Else clause semantics
│  └─ Finally block guarantee
├─ private List<Type> ResolveCatchTypes(ExceptHandler handler)
│  └─ Maps Python exception names to CLR types
├─ public void EmitRaise(RaiseStatement s)
│  ├─ Exception instance handling
│  ├─ Exception type handling
│  └─ Cause chain support
├─ public void EmitAssert(AssertStatement s)
│  └─ Message generation with source location
├─ public void EmitWith(WithStatement s)
│  ├─ Context manager protocol (__enter__ / __exit__)
│  ├─ Exception suppression handling
│  └─ Multiple context managers support
└─ public override void Emit(Statement stmt) [Not used]

───────────────────────────────────────────────────────────

DefinitionEmitters.cs (420 lines)
├─ public void EmitFunctionDef(FunctionDef s)
│  ├─ Nested function creation
│  ├─ Closure variable hoisting
│  ├─ Nonlocal declaration handling
│  ├─ Generator detection
│  └─ Yield initialization
├─ public void EmitClassDef(ClassDef s)
│  └─ Placeholder for inline classes
│
├─ Helper: public static bool ContainsYieldStatic(...)
├─ Helper: private static bool ContainsYield(...)
├─ Helper: private static bool ContainsYieldInStatement(...)
├─ Helper: private static bool ContainsYieldInExpression(...)
├─ Helper: private static bool ContainsYieldInComprehension(...)
│
├─ Helper: private static HashSet<string> CollectAssignedNames(...)
├─ Helper: private static HashSet<string> CollectReferencedNames(...)
├─ Helper: private static void CollectReferencedNamesInStmt(...)
├─ Helper: private static void CollectNamesInExpr(...)
├─ Helper: private static HashSet<string> CollectNamesReferencedByNestedFunctions(...)
├─ Helper: private static HashSet<string> CollectNonlocalNames(...)
├─ Helper: private static void CollectNonlocalNamesInStmt(...)
│
└─ public override void Emit(Statement stmt) [Not used]

───────────────────────────────────────────────────────────

ScopeEmitters.cs (110 lines)
├─ public void EmitNonlocal(NonlocalStatement s)
│  └─ Promotes variables to module-level fields
├─ public void EmitReturn(ReturnStatement s)
│  ├─ Generator return handling
│  └─ Type conversion for return values
├─ public void EmitExprStatement(ExprStatement s)
│  └─ Simple stack pop after expression
│
└─ public override void Emit(Statement stmt) [Not used]

───────────────────────────────────────────────────────────

StatementAnalyzer.cs (280 lines)
├─ public static HashSet<string> CollectAssignedNames(...)
├─ public static HashSet<string> CollectReferencedNames(...)
├─ public static HashSet<string> CollectNamesReferencedByNestedFunctions(...)
├─ public static bool ContainsYield(...)
│
└─ [Helper methods: analysis utilities]

Used by: AssemblyEmitter.MethodGeneration.cs
Purpose: Shared analysis for function body preparation

───────────────────────────────────────────────────────────

Benefits:
✅ 1551 lines → 220 lines main dispatcher (-86%)
✅ Clear separation of concerns
✅ Each specialist has single responsibility
✅ Easier to locate statement handlers
✅ Better for unit testing
✅ Reduced cognitive load
✅ Consistent pattern for future extraction
✅ Lazy initialization avoids unused allocations
```

## Metrics Comparison

| Aspect | Before | After | Improvement |
|--------|--------|-------|-------------|
| Main File | 1551 lines | 220 lines | **-86%** |
| Max File | 1551 | 430 | **-72%** |
| Avg Specialist | N/A | 280 | ✅ Focused |
| Separation | Mixed | Clear | ✅ 5 categories |
| Testability | Hard | Easy | ✅ Per specialist |
| Maintainability | Low | High | ✅ Clear intent |

## Conclusion

The refactoring transforms a single 1551-line file into a well-organized architecture with clear separation of concerns. Each specialist emitter is focused, testable, and maintainable while the dispatcher provides a clean routing layer. The lazy initialization pattern ensures efficient resource usage for simple programs.


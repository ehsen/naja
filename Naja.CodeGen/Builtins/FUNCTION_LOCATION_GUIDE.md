# Phase 3 - Function Location Reference Guide

Quick reference for finding builtin functions after Phase 3 refactoring.

## Type System & Object Creation

| Function | Module | Purpose |
|----------|--------|---------|
| `CoerceValue()` | **TypeSystem.cs** | Coerce values to target types (for property assignment) |
| `ResolveTypeByName()` | **TypeSystem.cs** | Resolve types by name from all loaded assemblies |
| `CreateDotNet()` | **TypeSystem.cs** | Create .NET objects via reflection |
| `ToStr()` | **TypeConversion.cs** | Convert objects to strings |
| `ToBool()` | **TypeConversion.cs** | Convert objects to booleans |
| `ToInt()` | **TypeConversion.cs** | Convert objects to integers |
| `ToFloat()` | **TypeConversion.cs** | Convert objects to floats |
| `Repr()` | **TypeConversion.cs** | Get repr() string representation |
| `TypeOf()` | **NajaBuiltins.cs** | Get type of an object |

## Iterator & Enumeration

| Function | Module | Purpose |
|----------|--------|---------|
| `GetEnumerator()` | **Iterators.cs** | Get IEnumerator from Python objects |
| `GetIteratorFromResult()` | **Iterators.cs** | Convert __iter__() results to enumerators |
| `NajaIteratorAdapter` | **Iterators.cs** | Adapter class for objects with __next__() |

## Dynamic Arithmetic Operators

| Function | Module | Purpose |
|----------|--------|---------|
| `DynamicAdd()` | **DynamicOperators.cs** | Dynamic addition with dunder dispatch |
| `DynamicSub()` | **DynamicOperators.cs** | Dynamic subtraction |
| `DynamicMul()` | **DynamicOperators.cs** | Dynamic multiplication |
| `DynamicMod()` | **DynamicOperators.cs** | Dynamic modulo |
| `PyFloorDiv()` | **NajaBuiltins.cs** | Python-style floor division (long) |
| `PyFloorDivF()` | **NajaBuiltins.cs** | Python-style floor division (float) |
| `PyMod()` | **NajaBuiltins.cs** | Python-style modulo (long) |
| `PyModF()` | **NajaBuiltins.cs** | Python-style modulo (float) |

## Dynamic Comparison Operators

| Function | Module | Purpose |
|----------|--------|---------|
| `DynamicEq()` | **ComparisonOperators.cs** | Dynamic equality with dunder dispatch |
| `DynamicNotEq()` | **ComparisonOperators.cs** | Dynamic inequality |
| `DynamicLt()` | **ComparisonOperators.cs** | Dynamic less than |
| `DynamicLtEq()` | **ComparisonOperators.cs** | Dynamic less than or equal |
| `DynamicGt()` | **ComparisonOperators.cs** | Dynamic greater than |
| `DynamicGtEq()` | **ComparisonOperators.cs** | Dynamic greater than or equal |

## Reflection & Attribute Access

| Function | Module | Purpose |
|----------|--------|---------|
| `GetAttr()` | **ReflectionHelpers.cs** | Get instance attribute (property or field) |
| `SetAttr()` | **ReflectionHelpers.cs** | Set instance attribute |
| `GetStaticAttr()` | **ReflectionHelpers.cs** | Get static attribute from type |
| `GetItem()` | **ReflectionHelpers.cs** | Get item from container (list[i], dict[key]) |
| `SetItem()` | **ReflectionHelpers.cs** | Set item in container |
| `DynamicCall()` | **ReflectionHelpers.cs** | Call method dynamically on object |
| `StaticCall()` | **ReflectionHelpers.cs** | Call static method on type |
| `AddEventHandler()` | **ReflectionHelpers.cs** | Subscribe handler to event |
| `RemoveEventHandler()` | **ReflectionHelpers.cs** | Unsubscribe handler from event |

## String Operations

| Function | Module | Purpose |
|----------|--------|---------|
| `Chr()` | **StringFunctions.cs** | Convert character code to string |
| `Ord()` | **StringFunctions.cs** | Get character code from string |
| `Hex()` | **StringFunctions.cs** | Convert to hex string (0x prefix) |
| `Bin()` | **StringFunctions.cs** | Convert to binary string (0b prefix) |
| `Oct()` | **StringFunctions.cs** | Convert to octal string (0o prefix) |
| `StrUpper()` | **StringFunctions.cs** | String.upper() |
| `StrLower()` | **StringFunctions.cs** | String.lower() |
| `StrStrip()` | **StringFunctions.cs** | String.strip() |
| `StrLStrip()` | **StringFunctions.cs** | String.lstrip() |
| `StrRStrip()` | **StringFunctions.cs** | String.rstrip() |
| `StrStartsWith()` | **StringFunctions.cs** | String.startswith() |
| `StrEndsWith()` | **StringFunctions.cs** | String.endswith() |
| `StrIsDigit()` | **StringFunctions.cs** | String.isdigit() |
| `StrIsAlpha()` | **StringFunctions.cs** | String.isalpha() |
| `StrIsAlNum()` | **StringFunctions.cs** | String.isalnum() |
| `StrFind()` | **StringFunctions.cs** | String.find() |
| `StrIndex()` | **StringFunctions.cs** | String.index() |
| `StrReplace()` | **StringFunctions.cs** | String.replace() |
| `StrCenter()` | **StringFunctions.cs** | String.center() |
| `StrLJust()` | **StringFunctions.cs** | String.ljust() |
| `StrRJust()` | **StringFunctions.cs** | String.rjust() |
| `StrZFill()` | **StringFunctions.cs** | String.zfill() |
| `StrCount()` | **StringFunctions.cs** | String.count() |
| `StrJoin()` | **StringFunctions.cs** | String.join() |
| `StrSplit()` | **StringFunctions.cs** | String.split() |

## Mathematical Operations

| Function | Module | Purpose |
|----------|--------|---------|
| `Abs()` | **MathFunctions.cs** | Absolute value |
| `Min()` | **MathFunctions.cs** | Minimum value |
| `Max()` | **MathFunctions.cs** | Maximum value |
| `Sum()` | **MathFunctions.cs** | Sum of values |
| `Round()` | **MathFunctions.cs** | Round to nearest or N digits |
| `DivMod()` | **MathFunctions.cs** | Quotient and remainder |
| `Pow()` | **MathFunctions.cs** | Base raised to power |

## I/O Operations

| Function | Module | Purpose |
|----------|--------|---------|
| `Print()` | **IOFunctions.cs** | Print to console |
| `Input()` | **IOFunctions.cs** | Read from console |
| `Open()` | **IOFunctions.cs** | Open file (not yet implemented) |

## Collection Operations

| Function | Module | Purpose |
|----------|--------|---------|
| `Len()` | **Collections.cs** | Length of sequence/collection |
| `Range()` | **Collections.cs** | Generate range of integers |
| `Enumerate()` | **Collections.cs** | Enumerate with indices |
| `Zip()` | **Collections.cs** | Zip multiple iterables |
| `Map()` | **Collections.cs** | Apply function to iterable |
| `Filter()` | **Collections.cs** | Filter iterable with function |
| `Any()` | **Collections.cs** | Test if any element is true |
| `All()` | **Collections.cs** | Test if all elements are true |
| `Sorted()` | **Collections.cs** | Sort collection |
| `MakeList()` | **Collections.cs** | Create list from iterable or args |
| `MakeDict()` | **Collections.cs** | Create dict from key-value pairs |

## Callable & Exception Handling

| Function | Module | Purpose |
|----------|--------|---------|
| `CallCallable()` | **NajaBuiltins.cs** | Call any Python callable (delegate, MethodInfo, __call__) |
| `Raise()` | **NajaBuiltins.cs** | Raise an exception |
| `RaiseFrom()` | **NajaBuiltins.cs** | Raise exception with cause (raise ... from ...) |
| `ContextEnter()` | **NajaBuiltins.cs** | Call __enter__() for context manager |
| `ContextExit()` | **NajaBuiltins.cs** | Call __exit__() for context manager |
| `ContextExitWithException()` | **NajaBuiltins.cs** | Call __exit__() with exception |

## Formatting

| Function | Module | Purpose |
|----------|--------|---------|
| `Format()` | **NajaBuiltins.cs** | Format string with Python format spec |
| `FormatValue()` | **NajaBuiltins.cs** | Format single value |
| (and 10+ internal formatting helpers) | **NajaBuiltins.cs** | Various format spec parsing |

---

## Migration Notes for New Code

When adding new functionality:

1. **Type coercion needed?** → `TypeSystem.cs` or `TypeConversion.cs`
2. **Iterator/enumerable protocol?** → `Iterators.cs`
3. **String operations?** → `StringFunctions.cs`
4. **Math operations?** → `MathFunctions.cs`
5. **Collection operations?** → `Collections.cs`
6. **Object attribute/property access?** → `ReflectionHelpers.cs`
7. **Dynamic operators?** → `DynamicOperators.cs` or `ComparisonOperators.cs`
8. **I/O operations?** → `IOFunctions.cs`

All modules are in: `Naja.CodeGen/Builtins/`

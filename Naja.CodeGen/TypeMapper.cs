using Naja.Semantics;
using System.Reflection;
using System.Reflection.Emit;
using static Naja.CodeGen.NajaBuiltins;

namespace Naja.CodeGen;

/// <summary>
/// Maps Naja types to CLR types and emits type conversion opcodes.
/// The IL emitter queries this before emitting any value operation.
/// </summary>
public static class TypeMapper
{
    // ── Naja type → CLR type ──────────────────────────────────────────────────

    public static Type ToClrType(NajaType type) => type switch
    {
        IntType => typeof(long),
        FloatType => typeof(double),
        BoolType => typeof(bool),
        StrType => typeof(string),
        BytesType => typeof(byte[]),
        NoneType => typeof(void),
        ListType => typeof(System.Collections.Generic.List<object>),
        DictType => typeof(System.Collections.Generic.Dictionary<object, object>),
        SetType => typeof(System.Collections.Generic.HashSet<object>),
        TupleType => typeof(object[]),
        FunctionType => typeof(object),
        ClassType => typeof(object),
        UnionType => typeof(object),
        TypeofType => typeof(Type),
        ComplexType => typeof(System.Numerics.Complex),
        DecimalType => typeof(decimal),
        IntPtrType => typeof(IntPtr),
        UIntPtrType => typeof(UIntPtr),
        UnknownType => typeof(object),
        _ => typeof(object)
    };

    /// <summary>CLR return type — void for None, otherwise the mapped type.</summary>
    public static Type ToReturnType(NajaType type) =>
        type is NoneType ? typeof(void) : ToClrType(type);

    // ── CLR type → Naja type ──────────────────────────────────────────────────

    public static NajaType FromClrType(Type type)
    {
        if (type == typeof(long) || type == typeof(int) || type == typeof(short) || type == typeof(byte)) return NajaTypes.Int;
        if (type == typeof(double) || type == typeof(float)) return NajaTypes.Float;
        if (type == typeof(bool)) return NajaTypes.Bool;
        if (type == typeof(string)) return NajaTypes.Str;
        if (type == typeof(byte[])) return NajaTypes.Bytes;
        if (type == typeof(void)) return NajaTypes.None;
        if (type == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
        if (type == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
        if (type == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
        if (type == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());
        if (typeof(Delegate).IsAssignableFrom(type)) return new FunctionType(System.Array.Empty<NajaType>(), NajaTypes.Unknown);
        return NajaTypes.Unknown; // Default mapping for unboxing constraints
    }

    // ── Conversion opcodes ────────────────────────────────────────────────────

    /// <summary>
    /// Emit opcodes to convert whatever is on the stack to the target type.
    /// Called when the semantic type and the needed type differ.
    /// </summary>
    public static void EmitConversion(ILGenerator il, NajaType from, NajaType to)
    {
        if (from == to) return;

        // int → float
        if (from is IntType && to is FloatType)
        {
            il.Emit(OpCodes.Conv_R8);
            return;
        }

        // float → int (truncation)
        if (from is FloatType && to is IntType)
        {
            il.Emit(OpCodes.Conv_I8);
            return;
        }

        // bool → int
        if (from is BoolType && to is IntType)
        {
            il.Emit(OpCodes.Conv_I8);
            return;
        }

        // anything → string (call ToString)
        if (to is StrType)
        {
            var toStr = typeof(object).GetMethod("ToString")!;
            if (from is not StrType)
            {
                // box value types first
                if (from is IntType or FloatType or BoolType)
                    il.Emit(OpCodes.Box, ToClrType(from));
                il.Emit(OpCodes.Callvirt, toStr);
            }
            return;
        }

        // anything → object (box value types)
        if (to is UnknownType)
        {
            if (from is IntType or FloatType or BoolType)
                il.Emit(OpCodes.Box, ToClrType(from));
            return;
        }
    }

    /// <summary>
    /// Box a value type to object if needed — used when passing to object parameters.
    /// NEVER boxes reference types or unknown (already on heap).
    /// </summary>
    public static void EmitBox(ILGenerator il, NajaType type)
    {
        switch (type)
        {
            case IntType: il.Emit(OpCodes.Box, typeof(long)); break;
            case FloatType: il.Emit(OpCodes.Box, typeof(double)); break;
            case BoolType: il.Emit(OpCodes.Box, typeof(bool)); break;
            case DecimalType: il.Emit(OpCodes.Box, typeof(decimal)); break;
            case ComplexType: il.Emit(OpCodes.Box, typeof(System.Numerics.Complex)); break;
            case IntPtrType: il.Emit(OpCodes.Box, typeof(IntPtr)); break;
            case UIntPtrType: il.Emit(OpCodes.Box, typeof(UIntPtr)); break;
                // StrType, NoneType, UnknownType, ListType etc are already references — no box needed
        }
    }

    /// <summary>
    /// Unbox from object to a specific value type.
    /// </summary>
    public static void EmitUnbox(ILGenerator il, NajaType type)
    {
        if (type is IntType or FloatType or BoolType)
        {
            il.Emit(OpCodes.Unbox_Any, ToClrType(type));
        }
        else if (type is StrType)
        {
            il.Emit(OpCodes.Castclass, typeof(string));
        }
    }

    // ── Builtin method lookup ─────────────────────────────────────────────────

    /// <summary>
    /// Resolve a built-in function name to a CLR MethodInfo.
    /// The IL emitter calls this for CallExpr on known builtins.
    /// </summary>
    public static MethodInfo? ResolveBuiltin(string name) => name switch
    {
        "print" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Print))!,
        "input" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Input))!,
        "len" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Len))!,
        "range" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Range))!,
        "int" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToInt))!,
        "float" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!,
        "str" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToStr))!,
        "bool" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToBool))!,
        "repr" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Repr))!,
        "abs" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Abs))!,
        "max" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Max))!,
        "min" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Min))!,
        "sum" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Sum))!,
        "round" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Round))!,
        "pow" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Pow))!,
        "divmod" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DivMod))!,
        "sorted" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Sorted))!,
        "reversed" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Reversed))!,
        "enumerate" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Enumerate))!,
        "zip" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Zip))!,
        "map" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Map))!,
        "filter" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Filter))!,
        "any" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Any))!,
        "all" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.All))!,
        "list" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeList))!,
        "dict" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeDict))!,
        "set" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeSet))!,
        "frozenset" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeFrozenSet))!,
        "tuple" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeTuple))!,
        "chr" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Chr))!,
        "ord" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Ord))!,
        "hex" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Hex))!,
        "bin" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Bin))!,
        "oct" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Oct))!,
        "open" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Open))!,
        "isinstance" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.IsInstance))!,
        "type" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.TypeOf))!,
        "id" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Id))!,
        "hash" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Hash))!,
        "getattr" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetAttr))!,
        "setattr" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.SetAttr))!,
        "hasattr" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.HasAttr))!,
        "callable" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Callable))!,
        "vars" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Vars))!,
        "dir" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Dir))!,
        "format" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Format))!,
        "iter" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Iter))!,
        // Select the 1-argument overload of Next to avoid ambiguity.
        "next" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Next), new[] { typeof(object) })!,
        "assert" => typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Assert))!,
        _ => null
    };

    // ── Exception resolution ──────────────────────────────────────────────────
    
    public static Type? ResolveExceptionType(string name) => name switch
    {
        "Exception" => typeof(Exception),
        "ValueError" => typeof(ArgumentException),
        "TypeError" => typeof(InvalidCastException),
        "KeyError" => typeof(System.Collections.Generic.KeyNotFoundException),
        "IndexError" => typeof(IndexOutOfRangeException),
        "AttributeError" => typeof(MissingMemberException),
        "RuntimeError" => typeof(InvalidOperationException),
        "NotImplementedError" => typeof(NotImplementedException),
        "OSError" or "IOError" or "FileNotFoundError" => typeof(System.IO.IOException),
        "PermissionError" => typeof(UnauthorizedAccessException),
        "StopIteration" => typeof(InvalidOperationException),
        "OverflowError" => typeof(OverflowException),
        "ZeroDivisionError" => typeof(DivideByZeroException),
        "MemoryError" => typeof(OutOfMemoryException),
        "RecursionError" => typeof(StackOverflowException),
        "ImportError" => typeof(TypeLoadException),
        "NameError" => typeof(MissingFieldException),
        "SystemExit" => typeof(Exception),
        "KeyboardInterrupt" => typeof(Exception),
        "BaseException" => typeof(Exception),
        "ExceptionGroup" or "BaseExceptionGroup" => typeof(NajaExceptionGroup),
        _ => null
    };
}
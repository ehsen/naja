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
            if (from is not StrType)
            {
                // box value types first
                if (from is IntType or FloatType or BoolType)
                    il.Emit(OpCodes.Box, ToClrType(from));
                il.Emit(OpCodes.Callvirt, FrameworkMethodCache.Object_ToString_Method);
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
        "print" => NajaBuiltinsMethodCache.Print_Method,
        "input" => NajaBuiltinsMethodCache.Input_Method,
        "len" => NajaBuiltinsMethodCache.Len_Method,
        "range" => NajaBuiltinsMethodCache.Range_Method,
        "int" => NajaBuiltinsMethodCache.ToInt_Method,
        "float" => NajaBuiltinsMethodCache.ToFloat_Method,
        "str" => NajaBuiltinsMethodCache.ToStr_Method,
        "bool" => NajaBuiltinsMethodCache.ToBool_Method,
        "repr" => NajaBuiltinsMethodCache.Repr_Method,
        "abs" => NajaBuiltinsMethodCache.Abs_Method,
        "max" => NajaBuiltinsMethodCache.Max_Method,
        "min" => NajaBuiltinsMethodCache.Min_Method,
        "sum" => NajaBuiltinsMethodCache.Sum_Method,
        "round" => NajaBuiltinsMethodCache.Round_Method,
        "pow" => NajaBuiltinsMethodCache.Pow_Method,
        "divmod" => NajaBuiltinsMethodCache.DivMod_Method,
        "sorted" => NajaBuiltinsMethodCache.Sorted_Method,
        "reversed" => NajaBuiltinsMethodCache.Reversed_Method,
        "enumerate" => NajaBuiltinsMethodCache.Enumerate_Method,
        "zip" => NajaBuiltinsMethodCache.Zip_Method,
        "map" => NajaBuiltinsMethodCache.Map_Method,
        "filter" => NajaBuiltinsMethodCache.Filter_Method,
        "any" => NajaBuiltinsMethodCache.Any_Method,
        "all" => NajaBuiltinsMethodCache.All_Method,
        "list" => NajaBuiltinsMethodCache.MakeList_Method,
        "dict" => NajaBuiltinsMethodCache.MakeDict_Method,
        "set" => NajaBuiltinsMethodCache.MakeSet_Method,
        "frozenset" => NajaBuiltinsMethodCache.MakeFrozenSet_Method,
        "tuple" => NajaBuiltinsMethodCache.MakeTuple_Method,
        "chr" => NajaBuiltinsMethodCache.Chr_Method,
        "ord" => NajaBuiltinsMethodCache.Ord_Method,
        "hex" => NajaBuiltinsMethodCache.Hex_Method,
        "bin" => NajaBuiltinsMethodCache.Bin_Method,
        "oct" => NajaBuiltinsMethodCache.Oct_Method,
        "open" => NajaBuiltinsMethodCache.Open_Method,
        "isinstance" => NajaBuiltinsMethodCache.IsInstance_Method,
        "type" => NajaBuiltinsMethodCache.TypeOf_Method,
        "id" => NajaBuiltinsMethodCache.Id_Method,
        "hash" => NajaBuiltinsMethodCache.Hash_Method,
        "getattr" => NajaBuiltinsMethodCache.GetAttr_Method,
        "setattr" => NajaBuiltinsMethodCache.SetAttr_Method,
        "hasattr" => NajaBuiltinsMethodCache.HasAttr_Method,
        "callable" => NajaBuiltinsMethodCache.Callable_Method,
        "vars" => NajaBuiltinsMethodCache.Vars_Method,
        "dir" => NajaBuiltinsMethodCache.Dir_Method,
        "format" => NajaBuiltinsMethodCache.Format_Method,
        "iter" => NajaBuiltinsMethodCache.Iter_Method,
        // NextVararg handles both next(g) and next(g, default) via variadic object[] args.
        "next" => NajaBuiltinsMethodCache.NextVararg_Method,
        "assert" => NajaBuiltinsMethodCache.Assert_Method,
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
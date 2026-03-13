namespace Naja.Semantics;

// ── Base ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Every type in Naja's type system.
/// These map directly to CLR types during IL emission.
/// </summary>
public abstract record NajaType
{
    /// <summary>The CLR type this Naja type maps to.</summary>
    public abstract Type ClrType { get; }

    public override string ToString() => GetType().Name;
}

// ── Primitive types ───────────────────────────────────────────────────────────

/// <summary>Python int → CLR long (int64)</summary>
public sealed record IntType : NajaType
{
    public static readonly IntType Instance = new();
    public override Type ClrType => typeof(long);
    public override string ToString() => "int";
}

/// <summary>Python float → CLR double</summary>
public sealed record FloatType : NajaType
{
    public static readonly FloatType Instance = new();
    public override Type ClrType => typeof(double);
    public override string ToString() => "float";
}

/// <summary>Python bool → CLR bool</summary>
public sealed record BoolType : NajaType
{
    public static readonly BoolType Instance = new();
    public override Type ClrType => typeof(bool);
    public override string ToString() => "bool";
}

/// <summary>Python str → CLR string</summary>
public sealed record StrType : NajaType
{
    public static readonly StrType Instance = new();
    public override Type ClrType => typeof(string);
    public override string ToString() => "str";
}

/// <summary>Python bytes → CLR byte[]</summary>
public sealed record BytesType : NajaType
{
    public static readonly BytesType Instance = new();
    public override Type ClrType => typeof(byte[]);
    public override string ToString() => "bytes";
}

/// <summary>Python None → CLR void (in return position) or null</summary>
public sealed record NoneType : NajaType
{
    public static readonly NoneType Instance = new();
    public override Type ClrType => typeof(void);
    public override string ToString() => "None";
}

/// <summary>Python complex → CLR System.Numerics.Complex</summary>
public sealed record ComplexType : NajaType
{
    public static readonly ComplexType Instance = new();
    public override Type ClrType => typeof(System.Numerics.Complex);
    public override string ToString() => "complex";
}

/// <summary>Python decimal → CLR System.Decimal</summary>
public sealed record DecimalType : NajaType
{
    public static readonly DecimalType Instance = new();
    public override Type ClrType => typeof(decimal);
    public override string ToString() => "decimal";
}

/// <summary>Python nint → CLR IntPtr</summary>
public sealed record IntPtrType : NajaType
{
    public static readonly IntPtrType Instance = new();
    public override Type ClrType => typeof(IntPtr);
    public override string ToString() => "nint";
}

/// <summary>Python nuint → CLR UIntPtr</summary>
public sealed record UIntPtrType : NajaType
{
    public static readonly UIntPtrType Instance = new();
    public override Type ClrType => typeof(UIntPtr);
    public override string ToString() => "nuint";
}

// ── Collection types ──────────────────────────────────────────────────────────

/// <summary>Python list[T] → CLR List&lt;T&gt;</summary>
public sealed record ListType(NajaType ElementType) : NajaType
{
    public override Type ClrType => typeof(System.Collections.Generic.List<>)
        .MakeGenericType(ElementType.ClrType);
    public override string ToString() => $"list[{ElementType}]";
}

/// <summary>Python dict[K, V] → CLR Dictionary&lt;K,V&gt;</summary>
public sealed record DictType(NajaType KeyType, NajaType ValueType) : NajaType
{
    public override Type ClrType => typeof(System.Collections.Generic.Dictionary<,>)
        .MakeGenericType(KeyType.ClrType, ValueType.ClrType);
    public override string ToString() => $"dict[{KeyType}, {ValueType}]";
}

/// <summary>Python tuple → CLR ValueTuple or object[]</summary>
public sealed record TupleType(IReadOnlyList<NajaType> ElementTypes) : NajaType
{
    public override Type ClrType => typeof(object[]);  // simplified for now
    public override string ToString() =>
        $"tuple[{string.Join(", ", ElementTypes)}]";
}

/// <summary>Python set[T] → CLR HashSet&lt;T&gt;</summary>
public sealed record SetType(NajaType ElementType) : NajaType
{
    public override Type ClrType => typeof(System.Collections.Generic.HashSet<>)
        .MakeGenericType(ElementType.ClrType);
    public override string ToString() => $"set[{ElementType}]";
}

/// <summary>Python frozenset[T] → CLR ImmutableHashSet&lt;object&gt; (or T)</summary>
public sealed record FrozenSetType(NajaType ElementType) : NajaType
{
    public override Type ClrType => typeof(System.Collections.Immutable.ImmutableHashSet<object>);
    public override string ToString() => $"frozenset[{ElementType}]";
}

// ── Callable types ────────────────────────────────────────────────────────────

/// <summary>A function or method type.</summary>
public sealed record FunctionType(
    IReadOnlyList<NajaType> ParamTypes,
    NajaType ReturnType
) : NajaType
{
    public override Type ClrType => typeof(Delegate);
    public override string ToString() =>
        $"({string.Join(", ", ParamTypes)}) -> {ReturnType}";
}

/// <summary>A class type — represents the class itself (not an instance).</summary>
public sealed record ClassType(string Name) : NajaType
{
    public override Type ClrType => typeof(object);
    public override string ToString() => Name;
}

// ── Special types ─────────────────────────────────────────────────────────────

/// <summary>
/// Unknown / not yet inferred — used as a placeholder during analysis.
/// The IL emitter will treat this as object.
/// </summary>
public sealed record UnknownType : NajaType
{
    public static readonly UnknownType Instance = new();
    public override Type ClrType => typeof(object);
    public override string ToString() => "?";
}

/// <summary>
/// A union of possible types — used when branches may return different types.
/// e.g. x = 1 if cond else "hello"  →  int | str
/// </summary>
public sealed record UnionType(IReadOnlyList<NajaType> Types) : NajaType
{
    public override Type ClrType => typeof(object);
    public override string ToString() =>
        string.Join(" | ", Types);
}

/// <summary>
/// The type of a type annotation expression itself.
/// e.g. in  x: int = 5  the annotation 'int' has type TypeofType(IntType)
/// </summary>
public sealed record TypeofType(NajaType Inner) : NajaType
{
    public override Type ClrType => typeof(Type);
    public override string ToString() => $"type[{Inner}]";
}

// ── Helpers ───────────────────────────────────────────────────────────────────

public static class NajaTypes
{
    public static readonly IntType     Int     = IntType.Instance;
    public static readonly FloatType   Float   = FloatType.Instance;
    public static readonly BoolType    Bool    = BoolType.Instance;
    public static readonly StrType     Str     = StrType.Instance;
    public static readonly BytesType   Bytes   = BytesType.Instance;
    public static readonly NoneType    None    = NoneType.Instance;
    public static readonly ComplexType Complex = ComplexType.Instance;
    public static readonly DecimalType Decimal = DecimalType.Instance;
    public static readonly IntPtrType  NInt    = IntPtrType.Instance;
    public static readonly UIntPtrType NUInt   = UIntPtrType.Instance;
    public static readonly UnknownType Unknown = UnknownType.Instance;

    /// <summary>
    /// Resolve a Python annotation name to a NajaType.
    /// e.g.  "int" → IntType,  "str" → StrType
    /// </summary>
    public static NajaType FromAnnotation(string name) => name switch
    {
        "int"     => Int,
        "float"   => Float,
        "bool"    => Bool,
        "str"     => Str,
        "bytes"   => Bytes,
        "None"    => None,
        "complex" => Complex,
        "decimal" => Decimal,
        "nint"    => NInt,
        "nuint"   => NUInt,
        _         => new ClassType(name)
    };

    /// <summary>
    /// Widen two types to their common supertype.
    /// Used when merging types from different branches.
    /// </summary>
    public static NajaType Widen(NajaType a, NajaType b)
    {
        if (a == b) return a;
        if (a is IntType   && b is FloatType) return Float;
        if (a is FloatType && b is IntType)   return Float;
        if (a is BoolType  && b is IntType)   return Int;
        if (a is IntType   && b is BoolType)  return Int;
        if (a is UnknownType) return b;
        if (b is UnknownType) return a;
        return new UnionType([a, b]);
    }
}

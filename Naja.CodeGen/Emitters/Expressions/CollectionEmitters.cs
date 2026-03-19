using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Emits IL for collection expressions: lists, tuples, dicts, sets.
/// </summary>
public sealed class CollectionEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public CollectionEmitters(EmitContext ctx, ExpressionEmitter mainEmitter) 
        : base(ctx) 
    {
        _mainEmitter = mainEmitter;
    }

    /// <summary>
    /// Main dispatch for collection expressions.
    /// </summary>
    public NajaType EmitCollection(Expression expr)
    {
        return expr switch
        {
            ListExpr e => EmitList(e),
            TupleExpr e => EmitTuple(e),
            SetExpr e => EmitSet(e),
            DictExpr e => EmitDict(e),
            _ => throw new CodeGenException($"Not a collection: {expr.GetType().Name}", expr.Line, expr.Column)
        };
    }

    private NajaType EmitList(ListExpr e)
    {
        var listType = typeof(System.Collections.Generic.List<object>);
        var ctor = listType.GetConstructor(Type.EmptyTypes)!;
        var addMethod = listType.GetMethod("Add")!;

        IL.Emit(OpCodes.Newobj, ctor);

        foreach (var elem in e.Elements)
        {
            IL.Emit(OpCodes.Dup);
            var elemType = _mainEmitter.Emit(elem);
            TypeMapper.EmitBox(IL, elemType);
            IL.Emit(OpCodes.Callvirt, addMethod);
        }

        return new ListType(NajaTypes.Unknown);
    }

    private NajaType EmitTuple(TupleExpr e)
    {
        // Tuples as object[]
        IL.Emit(OpCodes.Ldc_I4, e.Elements.Count);
        IL.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < e.Elements.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            var elemType = _mainEmitter.Emit(e.Elements[i]);
            TypeMapper.EmitBox(IL, elemType);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        return new TupleType([]);
    }

    private NajaType EmitDict(DictExpr e)
    {
        var dictType = typeof(System.Collections.Generic.Dictionary<object, object>);
        var ctor = dictType.GetConstructor(Type.EmptyTypes)!;
        var setItem = dictType.GetMethod("set_Item")!;

        IL.Emit(OpCodes.Newobj, ctor);

        foreach (var (key, val) in e.Pairs)
        {
            if (key is null) continue;  // **unpack not supported yet
            IL.Emit(OpCodes.Dup);
            var kt = _mainEmitter.Emit(key); TypeMapper.EmitBox(IL, kt);
            var vt = _mainEmitter.Emit(val); TypeMapper.EmitBox(IL, vt);
            IL.Emit(OpCodes.Callvirt, setItem);
        }

        return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
    }

    private NajaType EmitSet(SetExpr e)
    {
        var setType = typeof(System.Collections.Generic.HashSet<object>);
        var ctor = setType.GetConstructor(Type.EmptyTypes)!;
        var add = setType.GetMethod("Add")!;

        IL.Emit(OpCodes.Newobj, ctor);
        foreach (var elem in e.Elements)
        {
            IL.Emit(OpCodes.Dup);
            var t = _mainEmitter.Emit(elem);
            TypeMapper.EmitBox(IL, t);
            IL.Emit(OpCodes.Callvirt, add);
            IL.Emit(OpCodes.Pop);   // Add returns bool
        }
        return new SetType(NajaTypes.Unknown);
    }

    /// <summary>
    /// Not implemented in this emitter - used by ExpressionEmitter for dispatch
    /// </summary>
    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException("Use EmitCollection() for collection expressions");
    }
}

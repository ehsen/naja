using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Specialist emitter for attribute access, subscripting, and slice expressions.
/// Handles property/field access, dynamic attribute lookup, and collection indexing.
/// </summary>
public sealed class AttributeEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public AttributeEmitters(EmitContext ctx, ExpressionEmitter mainEmitter)
        : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    /// <summary>
    /// Emits IL for attribute access: obj.attr or Module.Type or namespace.type
    /// Handles namespace type resolution, .NET static/instance properties and fields,
    /// instance field access, and dynamic fallback to NajaBuiltins.GetAttr.
    /// </summary>
    public NajaType EmitAttribute(AttributeExpr e)
    {
        // ── Namespace.Type access: System.DateTime, etc. ──
        if (e.Object is NameExpr nsName &&
            _ctx.NamespaceImports.TryGetValue(nsName.Name, out var nsAsmName))
        {
            // Build full type name: Namespace.Type
            var fullTypeName = nsName.Name + "." + e.Attribute;

            // Try to resolve the type from loaded assemblies
            Type? resolvedType = null;

            if (string.IsNullOrEmpty(nsAsmName))
            {
                // No specific assembly - search all loaded assemblies
                resolvedType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(fullTypeName, throwOnError: false, ignoreCase: true))
                    .FirstOrDefault(t => t is not null);
            }
            else
            {
                // Try with the specified assembly name first
                resolvedType = Type.GetType(fullTypeName + ", " + nsAsmName, throwOnError: false, ignoreCase: true);
            }

            // Fallback: try without assembly qualification
            resolvedType ??= Type.GetType(fullTypeName, throwOnError: false, ignoreCase: true);

            if (resolvedType is not null)
            {
                // Push the Type token directly
                IL.Emit(OpCodes.Ldtoken, resolvedType);
                var getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle")!;
                IL.Emit(OpCodes.Call, getTypeFromHandle);
                return NajaTypes.Unknown;
            }

            // If we can't resolve the type, fall through to dynamic handling
        }

        // ── Imported .NET type static member access: Color.White, FontStyle.Bold, etc. ──
        if (e.Object is NameExpr importedName &&
            _ctx.ImportMap.TryGetValue(importedName.Name, out var importEntry))
        {
            var dotnetType =
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(importEntry.TypeName, throwOnError: false, ignoreCase: true))
                    .FirstOrDefault(t => t is not null)
                ?? Type.GetType(importEntry.TypeName + ", " + importEntry.AssemblyName)
                ?? Type.GetType(importEntry.TypeName);

            if (dotnetType is not null)
            {
                _mainEmitter.Emit(e.Object);
                IL.Emit(OpCodes.Pop);

                // IMPORTANT: Check for enum types FIRST before static field lookup.
                // Enum members are stored as literal fields in metadata, but they cannot
                // be accessed via Ldsfld like regular static fields. We must parse and
                // emit the integer value directly.
                // Enum value by name (FormBorderStyle.Fixed3D, DockStyle.Fill, etc.)
                if (dotnetType.IsEnum)
                {
                    try
                    {
                        var enumVal = Enum.Parse(dotnetType, e.Attribute, ignoreCase: true);
                        var underlying = Convert.ToInt64(enumVal);
                        IL.Emit(OpCodes.Ldc_I8, underlying);
                        // Removed Conv_I4: NajaTypes.Int is Int64, so leave it as an 8-byte integer
                        return NajaTypes.Int;
                    }
                    catch { /* fall through to static field/property lookup */ }
                }

                // Try static property first (Color.White, Color.FromArgb as property, etc.)
                var staticProp = dotnetType.GetProperty(e.Attribute,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (staticProp?.GetGetMethod() is { } staticGetter)
                {
                    IL.Emit(OpCodes.Call, staticGetter);
                    var pt = staticProp.PropertyType;
                    if (pt == typeof(long)) return NajaTypes.Int;
                    if (pt == typeof(double)) return NajaTypes.Float;
                    if (pt == typeof(bool)) return NajaTypes.Bool;
                    if (pt == typeof(string)) return NajaTypes.Str;

                    // FIX: Immediately box any native struct so it becomes a valid System.Object
                    if (pt.IsValueType) IL.Emit(OpCodes.Box, pt);
                    return NajaTypes.Unknown;
                }

                // Try static field (Color.Empty, IntPtr.Zero, etc. - but NOT enum members)
                var staticField = dotnetType.GetField(e.Attribute,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (staticField is not null && !staticField.FieldType.IsEnum)
                {
                    IL.Emit(OpCodes.Ldsfld, staticField);
                    var ft = staticField.FieldType;
                    if (ft == typeof(long)) return NajaTypes.Int;
                    if (ft == typeof(double)) return NajaTypes.Float;
                    if (ft == typeof(bool)) return NajaTypes.Bool;
                    if (ft == typeof(string)) return NajaTypes.Str;

                    // FIX: Immediately box any native struct so it becomes a valid System.Object
                    if (ft.IsValueType) IL.Emit(OpCodes.Box, ft);
                    return NajaTypes.Unknown;
                }

                IL.Emit(OpCodes.Ldtoken, dotnetType);
                IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                IL.Emit(OpCodes.Ldstr, e.Attribute);
                var staticGetAttr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetStaticAttr),
                    new[] { typeof(Type), typeof(string) });
                if (staticGetAttr is not null)
                {
                    IL.Emit(OpCodes.Call, staticGetAttr);
                    return NajaTypes.Unknown;
                }
                IL.Emit(OpCodes.Call, typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetAttr))!);
                return NajaTypes.Unknown;
            }
        }

        var objType = _mainEmitter.Emit(e.Object);

        bool isNativeProperty = false;
        if (e.Object is NameExpr { Name: var n1 } && n1 == (_ctx.SelfName ?? "self"))
        {
            Type? currentBase = _ctx.TypeBuilder.BaseType;
            while (currentBase != null && currentBase != typeof(object))
            {
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                if (currentBase.GetProperty(e.Attribute, flags) != null)
                {
                    isNativeProperty = true;
                    break;
                }
                currentBase = currentBase.BaseType;
            }
        }

        if (!isNativeProperty &&
            e.Object is NameExpr { Name: var n } &&
            n == (_ctx.SelfName ?? "self") &&
            _ctx.InstanceFields.TryGetValue(e.Attribute, out var iField))
        {
            IL.Emit(OpCodes.Ldfld, iField);
            if (iField.FieldType == typeof(long)) return NajaTypes.Int;
            if (iField.FieldType == typeof(double)) return NajaTypes.Float;
            if (iField.FieldType == typeof(bool)) return NajaTypes.Bool;
            if (iField.FieldType == typeof(string)) return NajaTypes.Str;
            if (iField.FieldType == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
            if (iField.FieldType == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
            if (iField.FieldType == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
            if (iField.FieldType == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());
            return NajaTypes.Unknown;
        }

        var clrType = TypeMapper.ToClrType(objType);

        var prop = clrType.GetProperty(e.Attribute, BindingFlags.Public | BindingFlags.Instance);
        if (prop?.GetGetMethod() is { } getter)
        {
            IL.Emit(OpCodes.Callvirt, getter);
            var pt = prop.PropertyType;
            if (pt == typeof(long)) return NajaTypes.Int;
            if (pt == typeof(double)) return NajaTypes.Float;
            if (pt == typeof(bool)) return NajaTypes.Bool;
            if (pt == typeof(string)) return NajaTypes.Str;
            if (pt == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
            if (pt == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
            if (pt == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
            if (pt == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());

            // FIX: Box instance property structs
            if (pt.IsValueType) IL.Emit(OpCodes.Box, pt);
            return NajaTypes.Unknown;
        }

        var field = clrType.GetField(e.Attribute, BindingFlags.Public | BindingFlags.Instance);
        if (field is not null)
        {
            IL.Emit(OpCodes.Ldfld, field);
            var ft = field.FieldType;
            if (ft == typeof(long)) return NajaTypes.Int;
            if (ft == typeof(double)) return NajaTypes.Float;
            if (ft == typeof(bool)) return NajaTypes.Bool;
            if (ft == typeof(string)) return NajaTypes.Str;
            if (ft == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
            if (ft == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
            if (ft == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
            if (ft == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());

            // FIX: Box instance field structs
            if (ft.IsValueType) IL.Emit(OpCodes.Box, ft);
            return NajaTypes.Unknown;
        }

        TypeMapper.EmitBox(IL, objType);
        IL.Emit(OpCodes.Ldstr, e.Attribute);
        var getAttr = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetAttr))!;
        IL.Emit(OpCodes.Call, getAttr);
        return NajaTypes.Unknown;
    }

    /// <summary>
    /// Emits IL for subscript operations: obj[index] and obj[slice].
    /// Handles list/string/dict indexing with type dispatch and slice syntax.
    /// </summary>
    public NajaType EmitSubscript(SubscriptExpr e)
    {
        // Slice indexing: a[1:10:2]
        if (e.Index is SliceExpr slice)
        {
            // Emit the object and store it — we need NajaSlice (this) on stack first
            // for the instance method call, then the object as argument.
            var objType2 = _mainEmitter.Emit(e.Object);
            TypeMapper.EmitBox(IL, objType2);
            var objLocal = _ctx.Locals.Declare($"__slobj_{e.Line}", typeof(object));
            IL.Emit(OpCodes.Stloc, objLocal);

            // Build NajaSlice (this for the instance call)
            EmitSlicePart(slice.Lower);
            EmitSlicePart(slice.Upper);
            EmitSlicePart(slice.Step);
            var sliceCtor = typeof(NajaSlice).GetConstructor(
                new[] { typeof(object), typeof(object), typeof(object) })!;
            IL.Emit(OpCodes.Newobj, sliceCtor);  // stack: [NajaSlice]

            // Call appropriate apply method: instance call needs (this=NajaSlice, arg=obj)
            if (objType2 is StrType)
            {
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Castclass, typeof(string));  // stack: [NajaSlice, string]
                var applyStr = typeof(NajaSlice).GetMethod("ApplyToString")!;
                IL.Emit(OpCodes.Call, applyStr);
                return NajaTypes.Str;
            }
            else
            {
                IL.Emit(OpCodes.Ldloc, objLocal);
                IL.Emit(OpCodes.Castclass, typeof(System.Collections.Generic.List<object>));  // stack: [NajaSlice, List]
                var applyList = typeof(NajaSlice).GetMethod("ApplyToList")!;
                IL.Emit(OpCodes.Call, applyList);
                return new ListType(NajaTypes.Unknown);
            }
        }

        // Regular indexing
        var objType = _mainEmitter.Emit(e.Object);
        var idxType = _mainEmitter.Emit(e.Index);

        if (objType is ListType l)
        {
            TypeMapper.EmitBox(IL, idxType);
            var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
            IL.Emit(OpCodes.Call, getItem);

            // FIX: Unbox the object back to its raw semantic type
            TypeMapper.EmitUnbox(IL, l.ElementType);

            return l.ElementType;
        }

        if (objType is StrType)
        {
            TypeMapper.EmitBox(IL, idxType);
            var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
            IL.Emit(OpCodes.Call, getItem);

            // FIX: Cast object back to string
            TypeMapper.EmitUnbox(IL, NajaTypes.Str);

            return NajaTypes.Str;
        }

        if (objType is DictType dt)
        {
            TypeMapper.EmitBox(IL, idxType);
            var getItem = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
            IL.Emit(OpCodes.Call, getItem);

            // FIX: Unbox the object back to its raw semantic type
            TypeMapper.EmitUnbox(IL, dt.ValueType);

            return dt.ValueType;
        }

        // Unknown — use dynamic helper
        TypeMapper.EmitBox(IL, objType);
        TypeMapper.EmitBox(IL, idxType);
        var dynGet = typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;
        IL.Emit(OpCodes.Call, dynGet);
        return NajaTypes.Unknown;
    }

    /// <summary>
    /// Emits IL for slice expressions: a[1:10:2].
    /// Creates a NajaSlice object with lower, upper, and step bounds.
    /// </summary>
    public NajaType EmitSlice(SliceExpr e)
    {
        // Build a NajaSlice object (runtime helper) — or call string/list slice
        var sliceCtor = typeof(NajaSlice).GetConstructor(
            new[] { typeof(object), typeof(object), typeof(object) })!;

        EmitSlicePart(e.Lower);
        EmitSlicePart(e.Upper);
        EmitSlicePart(e.Step);
        IL.Emit(OpCodes.Newobj, sliceCtor);
        return NajaTypes.Unknown;
    }

    /// <summary>
    /// Helper: Emits IL for one component of a slice (lower, upper, or step).
    /// Emits null if the expression is null, otherwise emits the boxed value.
    /// </summary>
    private void EmitSlicePart(Expression? expr)
    {
        if (expr is null) IL.Emit(OpCodes.Ldnull);
        else { var t = _mainEmitter.Emit(expr); TypeMapper.EmitBox(IL, t); }
    }

    /// <summary>
    /// Abstract override (required by base class but not used directly).
    /// Routes to specific emitter based on expression type.
    /// </summary>
    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException(
            $"Use EmitAttribute(), EmitSubscript(), or EmitSlice() directly. " +
            $"Expression type: {expr.GetType().Name}");
    }
}

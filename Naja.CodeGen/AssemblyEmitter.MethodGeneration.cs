using Naja.CodeGen;
using Naja.Inference;
using Naja.Parser;
using Naja.Semantics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NajaParserModule = Naja.Parser.Module;

namespace Naja.CodeGen;

/// <summary>
/// Partial class for method and function generation logic: instance method declaration/emission,
/// module-level function declaration/emission with generator and closure support.
/// </summary>
public sealed partial class AssemblyEmitter
{
    // ── Instance method declaration ───────────────────────────────────────────

    private (MethodBuilder mb, Type[] paramTypes, string uniqueName) DeclareInstanceMethod(
        FunctionDef fn, TypeBuilder ct)
    {
        if (fn.IsAsync)
            throw new CodeGenException("async/await is not yet supported.", fn.Line, fn.Column);

        bool hasSelf = fn.Params.Count > 0 && fn.Params[0].Name is "self" or "cls";
        var clrParams = fn.Params
            .Skip(hasSelf ? 1 : 0)
            .Select(_ => typeof(object))
            .ToArray();

        bool isStatic = fn.Decorators.Any(d => d is NameExpr { Name: "staticmethod" or "classmethod" });
        bool isFinal = fn.Decorators.Any(d =>
            d is NameExpr { Name: "final" }
            || (d is AttributeExpr a && a.Attribute == "final" && a.Object is NameExpr { Name: "typing" }));
        bool isProp = fn.Decorators.Any(d => d is NameExpr { Name: "property" });
        bool isSetter = fn.Decorators.Any(d => d is AttributeExpr a2 && a2.Attribute == "setter");

        string uniqueName = isProp ? "get_" + fn.Name
                          : isSetter ? "set_" + fn.Name
                          : fn.Name;
        string emitName = uniqueName;

        bool isPriv = fn.Name.StartsWith('_')
                   && !(fn.Name.StartsWith("__") && fn.Name.EndsWith("__"));

        var attrs = isPriv ? MethodAttributes.Private : MethodAttributes.Public;

        if (isStatic)
        {
            attrs |= MethodAttributes.Static;
        }
        else
        {
            attrs |= MethodAttributes.Virtual | MethodAttributes.HideBySig;

            // Avoid reflection on TypeBuilder base
            MethodInfo? baseMethod = null;
            if (ct.BaseType != null && ct.BaseType is not TypeBuilder)
            {
                baseMethod = ct.BaseType.GetMethod(
                    emitName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, clrParams, null);
            }

            if (baseMethod == null || !baseMethod.IsVirtual || baseMethod.IsFinal)
                attrs |= MethodAttributes.NewSlot;
        }


        if (isFinal && !isStatic) attrs |= MethodAttributes.Final;
        if (isProp || isSetter) attrs |= MethodAttributes.SpecialName;

        var mb = ct.DefineMethod(emitName, attrs, typeof(object), clrParams);

        if (!isStatic)
            for (int i = 0; i < clrParams.Length; i++)
                mb.DefineParameter(i + 1, ParameterAttributes.None,
                    fn.Params[i + (hasSelf ? 1 : 0)].Name);

        return (mb, clrParams, uniqueName);
    }

    // ── Instance method body emission ─────────────────────────────────────────

    private void EmitMethodBody(
        FunctionDef fn,
        MethodBuilder mb,
        TypeBuilder ct,
        ModuleBuilder modBuilder,
        Dictionary<string, FieldBuilder> moduleFields,
        Dictionary<string, MethodBuilder> moduleMethods,
        Dictionary<string, Type[]> moduleParamTypes,
        Dictionary<string, TypeBuilder> moduleClassTypes,
        Dictionary<string, ConstructorBuilder> moduleClassCtors,
        Dictionary<string, FieldBuilder> instanceFields,
        Dictionary<string, MethodBuilder> classMethods,
        Dictionary<string, Type[]> classParamTs,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap,
        Dictionary<string, string> namespaceImports)
    {
        var il = mb.GetILGenerator();
        bool isStatic = fn.Decorators.Any(d => d is NameExpr { Name: "staticmethod" or "classmethod" });
        bool hasSelf = !isStatic && fn.Params.Count > 0 && fn.Params[0].Name is "self" or "cls";

        var paramNames = fn.Params.Skip(hasSelf ? 1 : 0).Select(p => p.Name).ToList();

        var ctx = new EmitContext(il, _model, ct, modBuilder, typeof(object), paramNames);
        ctx.IsInstanceMethod = !isStatic;
        ctx.SelfName = hasSelf ? fn.Params[0].Name : null;
        ctx.IsInsideFunction = true;  // Fix 19: Mark as inside function for yield check

        foreach (var (k, v) in moduleFields) ctx.Fields[k] = v;
        foreach (var (k, v) in moduleMethods) ctx.Methods[k] = v;
        foreach (var (k, v) in moduleParamTypes) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in moduleClassTypes) ctx.ClassTypes[k] = v;
        foreach (var (k, v) in moduleClassCtors) ctx.ClassConstructors[k] = v;
        foreach (var (k, v) in _classCtorArgCounts) ctx.ClassCtorArgCounts[k] = v;
        foreach (var (k, v) in _classMethods) ctx.AllClassMethods[k] = v;

        foreach (var (k, v) in _classMethodParamTypes) ctx.AllClassMethodParamTypes[k] = v;
        foreach (var (k, v) in instanceFields) ctx.InstanceFields[k] = v;
        foreach (var (k, v) in classMethods) ctx.Methods[k] = v;
        foreach (var (k, v) in classParamTs) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in importMap) ctx.ImportMap[k] = v;
        foreach (var (k, v) in namespaceImports) ctx.NamespaceImports[k] = v;
        foreach (var mn in _classMethodNames) ctx.ClassMethods.Add(mn);


        var emitter = new StatementEmitter(ctx);
        emitter.EmitAll(fn.Body);

        EmitMethodEpilog(il, ctx, typeof(object));
    }

    // ── Module-level function declaration stub ────────────────────────────────

    private (MethodBuilder mb, Type[] paramTypes) DeclareMethod(FunctionDef fn, TypeBuilder tb)
    {
        if (fn.IsAsync)
            throw new CodeGenException("async/await is not yet supported.", fn.Line, fn.Column);

        // Reject duplicate parameter names
        var seenParams = new HashSet<string>();
        foreach (var p in fn.Params)
            if (!seenParams.Add(p.Name))
                throw new CodeGenException(
                    $"duplicate argument '{p.Name}' in function definition", fn.Line, fn.Column);

        var sym = _model.ModuleScope.Lookup(fn.Name);
        var fnType = sym?.Type as FunctionType;
        var retType = fnType is not null
            ? TypeMapper.ToReturnType(fnType.ReturnType)
            : typeof(object);

        var pts = fn.Params.Select((p, i) =>
        {
            var pType = fnType?.ParamTypes.ElementAtOrDefault(i);
            var clr = pType is not null ? TypeMapper.ToClrType(pType) : typeof(object);
            return clr == typeof(void) ? typeof(object) : clr;
        }).ToArray();

        var mb = tb.DefineMethod(
            fn.Name,
            MethodAttributes.Public | MethodAttributes.Static,
            retType,
            pts);

        for (int i = 0; i < fn.Params.Count; i++)
            mb.DefineParameter(i + 1, ParameterAttributes.None, fn.Params[i].Name);

        return (mb, pts);
    }

    // ── Module-level function body emission ───────────────────────────────────

    private void EmitFunctionBody(
        FunctionDef fn,
        MethodBuilder mb,
        TypeBuilder tb,
        ModuleBuilder modBuilder,
        Dictionary<string, FieldBuilder> fields,
        Dictionary<string, MethodBuilder> methods,
        Dictionary<string, Type[]> paramTypes,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap)
    {
        var il = mb.GetILGenerator();
        var sym = _model.ModuleScope.Lookup(fn.Name);
        var fnType = sym?.Type as FunctionType;
        var returnType = fnType is not null
            ? TypeMapper.ToReturnType(fnType.ReturnType)
            : typeof(object);

        var paramNames = fn.Params.Select(p => p.Name).ToList();
        var ctx = new EmitContext(il, _model, tb, modBuilder, returnType, paramNames);
        ctx.IsInsideFunction = true;  // Fix 19: Mark as inside function for yield check

        foreach (var (k, v) in fields) ctx.Fields[k] = v;
        foreach (var (k, v) in methods) ctx.Methods[k] = v;
        foreach (var (k, v) in paramTypes) ctx.MethodParamTypes[k] = v;
        foreach (var (k, v) in _classTypes) ctx.ClassTypes[k] = v;
        foreach (var (k, v) in _classConstructors) ctx.ClassConstructors[k] = v;
        foreach (var mn in _classMethodNames) ctx.ClassMethods.Add(mn);
        foreach (var (k, v) in importMap) ctx.ImportMap[k] = v;

        // Hoist variables referenced by nested functions: if an inner function
        // references a name that is assigned in this function, promote that name
        // to a module-level static field so the inner function sees the enclosing
        // binding (Python LEGB semantics).
        var nestedRefs = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.CollectNamesReferencedByNestedFunctions(fn.Body);
        var assigned = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.CollectAssignedNames(fn.Body);
        foreach (var r in nestedRefs.Intersect(assigned))
        {
            // Create hoisted field EVEN IF a module-level field exists with this name
            // because we need to shadow it in this function's scope for proper closure semantics
            var fb = tb.DefineField($"__nl_{r}", typeof(object), FieldAttributes.Private | FieldAttributes.Static);
            ctx.Fields[r] = fb;  // Override any existing field in context
        }

        // Check if this function contains yield statements (is a generator)
        bool isGenerator = Naja.CodeGen.Emitters.Statements.StatementAnalyzer.ContainsYield(fn.Body);
        if (isGenerator)
        {
            // Initialize the generator list at function entry
            var listType = typeof(System.Collections.Generic.List<object>);
            var ctor = listType.GetConstructor(Type.EmptyTypes)!;
            ctx.GeneratorListLocal = ctx.Locals.Declare($"__generator_{fn.Name}_{fn.Line}", listType);
            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc, ctx.GeneratorListLocal);

            // For generators, override returnType to be object since we'll return the list
            returnType = typeof(object);
        }

        var emitter = new StatementEmitter(ctx);
        emitter.EmitAll(fn.Body);

        if (isGenerator)
        {
            // Generator: emit fall-through epilog (no return-inside-exception epilog needed
            // because generator functions always fall through to here; the Leave epilog
            // for generator return is handled by the normal EmitMethodEpilog path when
            // ctx.MethodReturnLabel is set).
            if (ctx.MethodReturnLabel.HasValue)
            {
                il.MarkLabel(ctx.MethodReturnLabel.Value);
                if (ctx.ReturnValueLocal != null)
                    il.Emit(OpCodes.Ldloc, ctx.ReturnValueLocal);
                else
                {
                    il.Emit(OpCodes.Ldloc, ctx.GeneratorListLocal!);
                    var iterCtor2 = typeof(NajaGeneratorIterator)
                        .GetConstructor(new[] { typeof(System.Collections.Generic.List<object>) })!;
                    il.Emit(OpCodes.Newobj, iterCtor2);
                }
                il.Emit(OpCodes.Ret);
            }
            else
            {
                bool endsWithReturn = fn.Body.Count > 0 && fn.Body[^1] is ReturnStatement;
                if (!endsWithReturn)
                {
                    il.Emit(OpCodes.Ldloc, ctx.GeneratorListLocal!);
                    var iterCtor = typeof(NajaGeneratorIterator)
                        .GetConstructor(new[] { typeof(System.Collections.Generic.List<object>) })!;
                    il.Emit(OpCodes.Newobj, iterCtor);
                    il.Emit(OpCodes.Ret);
                }
            }
        }
        else
        {
            EmitMethodEpilog(il, ctx, returnType);
        }
    }

    /// <summary>
    /// Emits the function epilog after all statement emission is complete.
    /// If any <c>return</c> inside an exception block used <c>leave</c> to the
    /// <see cref="EmitContext.MethodReturnLabel"/>, we mark that label here and
    /// emit the final <c>ret</c>.  Otherwise we just emit a fall-through <c>ret</c>.
    /// </summary>
    private static void EmitMethodEpilog(ILGenerator il, EmitContext ctx, Type returnType)
    {
        if (ctx.MethodReturnLabel.HasValue)
        {
            // At least one `return` inside an exception block used `leave` to jump here.
            // The fall-through path (function body exits without an explicit return) also
            // needs to reach the epilog. Since we're outside exception blocks at this point
            // a plain `br` is valid.
            if (ctx.ReturnValueLocal != null)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Stloc, ctx.ReturnValueLocal);
            }
            il.Emit(OpCodes.Br, ctx.MethodReturnLabel.Value);

            il.MarkLabel(ctx.MethodReturnLabel.Value);
            if (ctx.ReturnValueLocal != null)
                il.Emit(OpCodes.Ldloc, ctx.ReturnValueLocal);
            else if (returnType != typeof(void))
                il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // No leave-based returns. Emit a plain fallthrough ret.
            if (returnType == typeof(object))
                il.Emit(OpCodes.Ldnull);
            else if (returnType != typeof(void))
            {
                il.Emit(OpCodes.Ldc_I4_0);
                if (returnType == typeof(long)) il.Emit(OpCodes.Conv_I8);
                if (returnType == typeof(double)) il.Emit(OpCodes.Conv_R8);
            }
            il.Emit(OpCodes.Ret);
        }
    }
}

using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Emits IL opcodes for lambda expressions.
/// </summary>
public sealed class LambdaEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public LambdaEmitters(EmitContext ctx, ExpressionEmitter mainEmitter) : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException("Use EmitLambda directly");
    }

    /// <summary>
    /// Compiles a lambda expression as a static method on the host type, returning a delegate.
    /// </summary>
    public NajaType EmitLambda(LambdaExpr e)
    {
        // Compile lambda as a static method on the host type, return a delegate
        var paramTypes = e.Params.Select(_ => typeof(object)).ToArray();
        var lambdaName = $"<lambda>_{e.Line}_{e.Column}";
        var mb = _ctx.TypeBuilder.DefineMethod(
            lambdaName,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            paramTypes);

        // Emit lambda body into new method
        var lambdaIL = mb.GetILGenerator();
        var paramNames = e.Params.Select(p => p.Name).ToList();
        var lambdaCtx = new EmitContext(lambdaIL, _ctx.Model, _ctx.TypeBuilder,
                                          _ctx.Module, typeof(object), paramNames);
        foreach (var (k, v) in _ctx.Fields) lambdaCtx.Fields[k] = v;
        foreach (var (k, v) in _ctx.Methods) lambdaCtx.Methods[k] = v;
        foreach (var (k, v) in _ctx.MethodParamTypes) lambdaCtx.MethodParamTypes[k] = v;
        // Copy locals from outer scope for closure support
        foreach (var localName in _ctx.Locals.GetAllNames())
        {
            var local = _ctx.Locals.TryGet(localName);
            if (local != null && !lambdaCtx.Locals.Contains(localName))
            {
                lambdaCtx.Locals.Declare(localName, local.LocalType);
            }
        }

        var bodyEmitter = new ExpressionEmitter(lambdaCtx);
        var retType = bodyEmitter.Emit(e.Body);
        TypeMapper.EmitBox(lambdaIL, retType);
        lambdaIL.Emit(OpCodes.Ret);

        // Create a Func<...> delegate pointing at this method
        var delegateType = System.Linq.Expressions.Expression.GetFuncType(paramTypes.Concat(new[] { typeof(object) }).ToArray());
        var ctor = delegateType.GetConstructors()[0];
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldftn, mb);
        IL.Emit(OpCodes.Newobj, ctor);

        // If any parameters have defaults, evaluate them inline and wrap the delegate
        bool hasDefaults = e.Params.Any(p => p.Default is not null);
        if (hasDefaults)
        {
            // Build array of evaluated defaults inline in the current context
            int defaultsCount = e.Params.Count(p => p.Default is not null);
            IL.Emit(OpCodes.Ldc_I4, defaultsCount);
            IL.Emit(OpCodes.Newarr, typeof(object));

            int defaultIndex = 0;
            for (int i = 0; i < e.Params.Count; i++)
            {
                var p = e.Params[i];
                if (p.Default is null) continue;
                IL.Emit(OpCodes.Dup); // array
                IL.Emit(OpCodes.Ldc_I4, defaultIndex);
                // Evaluate the default expression in the current context
                _mainEmitter.Emit(p.Default);
                TypeMapper.EmitBox(IL, NajaTypes.Unknown);
                IL.Emit(OpCodes.Stelem_Ref);
                defaultIndex++;
            }

            // Call CreateFunctionWithDefaults(delegate, defaults)
            var createFnHelper = typeof(NajaBuiltins).GetMethod("CreateFunctionWithDefaults",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
            IL.Emit(OpCodes.Call, createFnHelper);
        }

        return NajaTypes.Unknown;
    }
}

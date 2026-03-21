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
        // Detect outer-scope params captured by this lambda's body.
        // (e.g. `lambda x: lambda: x` — the inner lambda captures outer param `x`)
        var capturedOuterParams = new List<(string name, int outerIdx)>();
        if (_ctx.Parameters.Count > 0)
        {
            var referencedNames = CollectNamesInBodyExpr(e.Body);
            var innerParamNames = e.Params.Select(p => p.Name).ToHashSet();
            foreach (var name in referencedNames)
            {
                if (innerParamNames.Contains(name)) continue;
                int outerIdx = _ctx.GetParamIndex(name);
                if (outerIdx >= 0)
                    capturedOuterParams.Add((name, outerIdx));
            }
        }

        // Build extended param lists: real declared params + synthetic captured outer params
        var realParamTypes = e.Params.Select(_ => typeof(object)).ToArray();
        var allParamTypes = realParamTypes.Concat(capturedOuterParams.Select(_ => typeof(object))).ToArray();
        var allParamNames = e.Params.Select(p => p.Name)
                               .Concat(capturedOuterParams.Select(cp => cp.name)).ToList();

        var lambdaName = $"<lambda>_{e.Line}_{e.Column}";
        var mb = _ctx.TypeBuilder.DefineMethod(
            lambdaName,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            allParamTypes);

        // Emit lambda body into new method
        var lambdaIL = mb.GetILGenerator();
        var lambdaCtx = new EmitContext(lambdaIL, _ctx.Model, _ctx.TypeBuilder,
                                          _ctx.Module, typeof(object), allParamNames);
        foreach (var (k, v) in _ctx.Fields) lambdaCtx.Fields[k] = v;
        foreach (var (k, v) in _ctx.Methods) lambdaCtx.Methods[k] = v;
        foreach (var (k, v) in _ctx.MethodParamTypes) lambdaCtx.MethodParamTypes[k] = v;
        // Copy locals from outer scope for closure support
        foreach (var localName in _ctx.Locals.GetAllNames())
        {
            var local = _ctx.Locals.TryGet(localName);
            if (local != null && !lambdaCtx.Locals.Contains(localName))
                lambdaCtx.Locals.Declare(localName, local.LocalType);
        }

        var bodyEmitter = new ExpressionEmitter(lambdaCtx);
        var retType = bodyEmitter.Emit(e.Body);
        TypeMapper.EmitBox(lambdaIL, retType);
        lambdaIL.Emit(OpCodes.Ret);

        // Create a Func<...> delegate pointing at this method
        var delegateType = System.Linq.Expressions.Expression.GetFuncType(allParamTypes.Concat(new[] { typeof(object) }).ToArray());
        var ctor = delegateType.GetConstructors()[0];
        IL.Emit(OpCodes.Ldnull);
        IL.Emit(OpCodes.Ldftn, mb);
        IL.Emit(OpCodes.Newobj, ctor);

        // Wrap in NajaFunction when explicit parameter defaults OR captured outer params exist
        bool hasExplicitDefaults = e.Params.Any(p => p.Default is not null);
        bool needsNajaFn = hasExplicitDefaults || capturedOuterParams.Count > 0;
        if (needsNajaFn)
        {
            int defaultsCount = e.Params.Count(p => p.Default is not null) + capturedOuterParams.Count;
            IL.Emit(OpCodes.Ldc_I4, defaultsCount);
            IL.Emit(OpCodes.Newarr, typeof(object));

            int defaultIndex = 0;
            // Explicit parameter defaults first
            for (int i = 0; i < e.Params.Count; i++)
            {
                var p = e.Params[i];
                if (p.Default is null) continue;
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, defaultIndex);
                _mainEmitter.Emit(p.Default);
                TypeMapper.EmitBox(IL, NajaTypes.Unknown);
                IL.Emit(OpCodes.Stelem_Ref);
                defaultIndex++;
            }
            // Captured outer param values (injected at call-site for per-call value semantics)
            foreach (var (_, outerIdx) in capturedOuterParams)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, defaultIndex);
                int ilIdx = _ctx.IsInstanceMethod ? outerIdx + 1 : outerIdx;
                switch (ilIdx)
                {
                    case 0: IL.Emit(OpCodes.Ldarg_0); break;
                    case 1: IL.Emit(OpCodes.Ldarg_1); break;
                    case 2: IL.Emit(OpCodes.Ldarg_2); break;
                    case 3: IL.Emit(OpCodes.Ldarg_3); break;
                    default: IL.Emit(OpCodes.Ldarg_S, (byte)ilIdx); break;
                }
                IL.Emit(OpCodes.Stelem_Ref);
                defaultIndex++;
            }

            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CreateFunctionWithDefaults_Method);
        }

        return NajaTypes.Unknown;
    }

    private static HashSet<string> CollectNamesInBodyExpr(Expression expr)
    {
        var names = new HashSet<string>();
        CollectNamesInBodyExprRec(expr, names);
        return names;
    }

    private static void CollectNamesInBodyExprRec(Expression expr, HashSet<string> names)
    {
        if (expr is null) return;
        switch (expr)
        {
            case NameExpr ne: names.Add(ne.Name); break;
            case BinaryExpr be: CollectNamesInBodyExprRec(be.Left, names); CollectNamesInBodyExprRec(be.Right, names); break;
            case UnaryExpr ue: CollectNamesInBodyExprRec(ue.Operand, names); break;
            case BoolOpExpr bo: foreach (var v in bo.Values) CollectNamesInBodyExprRec(v, names); break;
            case CompareExpr ce: CollectNamesInBodyExprRec(ce.Left, names); foreach (var (_, r) in ce.Comparators) CollectNamesInBodyExprRec(r, names); break;
            case CallExpr call: CollectNamesInBodyExprRec(call.Func, names); foreach (var a in call.Args) CollectNamesInBodyExprRec(a.Value, names); break;
            case IfExpr ie: CollectNamesInBodyExprRec(ie.Condition, names); CollectNamesInBodyExprRec(ie.Then, names); CollectNamesInBodyExprRec(ie.Else, names); break;
            case AttributeExpr ae: CollectNamesInBodyExprRec(ae.Object, names); break;
            case SubscriptExpr se: CollectNamesInBodyExprRec(se.Object, names); if (se.Index is not null) CollectNamesInBodyExprRec(se.Index, names); break;
            case ListExpr le: foreach (var el in le.Elements) CollectNamesInBodyExprRec(el, names); break;
            case TupleExpr te: foreach (var el in te.Elements) CollectNamesInBodyExprRec(el, names); break;
            case LambdaExpr le2: CollectNamesInBodyExprRec(le2.Body, names); break;
        }
    }
}

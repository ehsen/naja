using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Statements;

/// <summary>
/// Emits IL for exception handling statements (try, raise, assert, with).
/// Manages exception blocks, catch handlers, and exception type resolution.
/// </summary>
public class ExceptionEmitters : StatementEmitterBase
{
    public ExceptionEmitters(EmitContext ctx, ExpressionEmitter exprEmitter, Action<Statement> emitStatement)
        : base(ctx, exprEmitter, emitStatement)
    {
    }

    // ── Try / except / else / finally ─────────────────────────────────────────

    public void EmitTry(TryStatement s)
    {
        var hasHandlers = s.Handlers.Count > 0;
        var hasFinally = s.Finally.Count > 0;
        var hasElse = s.Else.Count > 0;

        LocalBuilder? noExFlag = null;
        if (hasElse)
            noExFlag = _ctx.Locals.Declare($"__noex_{s.Line}", typeof(bool));

        if (hasFinally)
        {
            // Outer exception block for finally
            IL.BeginExceptionBlock();
        }

        if (hasHandlers)
        {
            // Inner exception block for handlers
            IL.BeginExceptionBlock();
            EmitAll(s.Body);

            if (noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stloc, noExFlag);
            }

            foreach (var handler in s.Handlers)
            {
                IL.BeginCatchBlock(typeof(Exception));

                var catchTypes = ResolveCatchTypes(handler);
                var exTmp = _ctx.Locals.Declare($"__ex_{s.Line}_{handler.Line}", typeof(Exception));
                IL.Emit(OpCodes.Stloc, exTmp);

                if (catchTypes.Count > 1 || catchTypes[0] != typeof(Exception))
                {
                    var matchedLabel = IL.DefineLabel();
                    foreach (var ct in catchTypes)
                    {
                        IL.Emit(OpCodes.Ldloc, exTmp);
                        IL.Emit(OpCodes.Isinst, ct);
                        IL.Emit(OpCodes.Brtrue, matchedLabel);
                    }
                    IL.Emit(OpCodes.Ldloc, exTmp);
                    IL.Emit(OpCodes.Throw);
                    IL.MarkLabel(matchedLabel);
                }

                if (handler.Name is not null)
                {
                    if (!_ctx.Locals.Contains(handler.Name))
                        _ctx.Locals.Declare(handler.Name, typeof(Exception));
                    IL.Emit(OpCodes.Ldloc, exTmp);
                    _ctx.Locals.EmitStore(handler.Name);
                }

                if (noExFlag is not null)
                {
                    IL.Emit(OpCodes.Ldc_I4_0);
                    IL.Emit(OpCodes.Stloc, noExFlag);
                }

                EmitAll(handler.Body);
            }

            IL.EndExceptionBlock();  // end inner (handler) block
        }
        else
        {
            // try/finally with no handlers:
            // Body goes directly inside the outer exception block.
            // The CLR guarantees the finally runs even if an exception escapes.
            EmitAll(s.Body);

            if (noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stloc, noExFlag);
            }
        }

        if (hasFinally)
        {
            IL.BeginFinallyBlock();
            EmitAll(s.Finally);
            IL.EndExceptionBlock();  // end outer (finally) block
        }

        // Emit else clause AFTER the entire exception handling structure
        if (hasElse && noExFlag is not null)
        {
            IL.Emit(OpCodes.Ldloc, noExFlag);
            var skipElse = IL.DefineLabel();
            IL.Emit(OpCodes.Brfalse, skipElse);
            EmitAll(s.Else);
            IL.MarkLabel(skipElse);
        }
    }

    private List<Type> ResolveCatchTypes(ExceptHandler handler)
    {
        if (handler.ExceptionType is null)
            return new List<Type> { typeof(Exception) };

        if (handler.ExceptionType is TupleExpr texpr)
            return texpr.Elements
                .Select(el => {
                    var name = el is NameExpr n ? n.Name : el.ToString() ?? "";
                    return TypeMapper.ResolveExceptionType(name) ?? typeof(Exception);
                })
                .ToList();

        var exName = handler.ExceptionType is NameExpr ne
            ? ne.Name
            : handler.ExceptionType.ToString() ?? "";
        return new List<Type> { TypeMapper.ResolveExceptionType(exName) ?? typeof(Exception) };
    }

    // ── Raise ─────────────────────────────────────────────────────────────────

    public void EmitRaise(RaiseStatement s)
    {
        if (s.Exception is not null)
        {
            _exprEmitter.Emit(s.Exception);
            // Exception expression may be either an Exception instance or a Type object
            // representing an exception class (e.g., "raise StopIteration"). Use
            // NajaBuiltins.EnsureException to normalize to a CLR Exception instance.
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.EnsureException_Method);

            if (s.Cause is not null)
            {
                // Must store exception first — cause gets emitted second
                // so arg order on stack matches (Exception, object)
                var exTmp = _ctx.Locals.Declare($"__raise_{s.Line}", typeof(Exception));
                IL.Emit(OpCodes.Stloc, exTmp);          // pop Exception, save it

                IL.Emit(OpCodes.Ldloc, exTmp);          // push Exception  (arg0)
                _exprEmitter.Emit(s.Cause);                    // push cause      (arg1)
                TypeMapper.EmitBox(IL, NajaTypes.Unknown);

                var setCause = typeof(NajaBuiltins)
                    .GetMethod(nameof(NajaBuiltins.SetExceptionCause))!;
                IL.Emit(OpCodes.Call, setCause);        // returns Exception, stack: [Exception]
            }

            IL.Emit(OpCodes.Throw);
        }
        else
        {
            IL.Emit(OpCodes.Rethrow);
        }
    }

    // ── Assert ────────────────────────────────────────────────────────────────

    public void EmitAssert(AssertStatement s)
    {
        var passLabel = IL.DefineLabel();
        _exprEmitter.Emit(s.Test);
        IL.Emit(OpCodes.Brtrue, passLabel);

        var ctor = typeof(Exception).GetConstructor(new[] { typeof(string) })!;
        if (s.Message is not null)
        {
            _exprEmitter.Emit(s.Message);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToStr_Method);
            // Append source location for easier diagnostics
            IL.Emit(OpCodes.Ldstr, $" (at {s.Line}:{s.Column})");
            IL.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) })!);
        }
        else
            IL.Emit(OpCodes.Ldstr, $"AssertionError (at {s.Line}:{s.Column})");

        IL.Emit(OpCodes.Newobj, ctor);
        IL.Emit(OpCodes.Throw);
        IL.MarkLabel(passLabel);
    }

    // ── With / as ─────────────────────────────────────────────────────────────

    public void EmitWith(WithStatement s)
    {
        if (s.IsAsync)
            throw new CodeGenException("async with is not yet supported in Naja.", s.Line, s.Column);
        // with expr as var: body
        // Full Python semantics:
        //   __enter__() called first; result bound to 'as' target.
        //   __exit__(exc_type, exc_val, tb) called in a catch block.
        //   If __exit__ returns truthy the exception is SUPPRESSED.
        //   __exit__(None,None,None) is called when no exception occurred.
        foreach (var item in s.Items)
        {
            var ctxLocal = _ctx.Locals.Declare($"__with_{s.Line}_{item.GetHashCode()}", typeof(object));
            var excLocal = _ctx.Locals.Declare($"__withex_{s.Line}_{item.GetHashCode()}", typeof(Exception));
            var suppressLocal = _ctx.Locals.Declare($"__withsup_{s.Line}_{item.GetHashCode()}", typeof(bool));

            // Evaluate context expression and store
            var ctxType = _exprEmitter.Emit(item.Context);
            TypeMapper.EmitBox(IL, ctxType);
            IL.Emit(OpCodes.Stloc, ctxLocal);

            // suppress = false
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Stloc, suppressLocal);

            IL.BeginExceptionBlock();

            // __enter__()
            IL.Emit(OpCodes.Ldloc, ctxLocal);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ContextEnter_Method);

            if (item.Target is NameExpr n)
            {
                if (!_ctx.Locals.Contains(n.Name))
                    _ctx.Locals.Declare(n.Name, typeof(object));
                _ctx.Locals.EmitStore(n.Name);
            }
            else
            {
                IL.Emit(OpCodes.Pop);
            }

            EmitAll(s.Body);

            // Normal path: call __exit__(None, None, None), ignore return value
            IL.Emit(OpCodes.Ldloc, ctxLocal);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ContextExitWithException_Method);
            IL.Emit(OpCodes.Pop);  // discard — normal exit never suppresses

            // Catch block: call __exit__ with the live exception
            IL.BeginCatchBlock(typeof(Exception));
            IL.Emit(OpCodes.Stloc, excLocal);

            IL.Emit(OpCodes.Ldloc, ctxLocal);
            IL.Emit(OpCodes.Ldloc, excLocal);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ContextExitWithException_Method);
            IL.Emit(OpCodes.Stloc, suppressLocal);

            // Re-throw if __exit__ returned false/None
            var suppressedLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, suppressLocal);
            IL.Emit(OpCodes.Brtrue, suppressedLabel);
            IL.Emit(OpCodes.Ldloc, excLocal);
            IL.Emit(OpCodes.Throw);
            IL.MarkLabel(suppressedLabel);

            IL.EndExceptionBlock();
        }
    }
}

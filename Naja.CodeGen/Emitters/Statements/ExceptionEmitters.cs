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

        // ── Special case: return inside finally swallows any exception ─────────
        // Python semantics: `return` in a `finally` block discards any in-flight
        // exception. The CLR prohibits `leave` (and `ret`) inside BeginFinallyBlock,
        // so we instead wrap the try body in a swallowing try/catch and emit the
        // finally body as ordinary straight-line code after that block.
        bool finallyHasReturn = hasFinally && !hasHandlers &&
            StatementAnalyzer.ContainsReturnStatement(s.Finally);

        if (finallyHasReturn)
        {
            var afterSwallow = IL.DefineLabel();
            IL.BeginExceptionBlock();
            _ctx.ExceptionBlockDepth++;
            EmitAll(s.Body);
            if (noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stloc, noExFlag);
            }
            // Catch and discard any exception — the return below overrides it.
            IL.BeginCatchBlock(typeof(Exception));
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Leave, afterSwallow);
            IL.EndExceptionBlock();
            _ctx.ExceptionBlockDepth--;
            IL.MarkLabel(afterSwallow);

            // Emit the finally body using normal return semantics (no Leave restriction).
            EmitAll(s.Finally);

            if (hasElse && noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldloc, noExFlag);
                var skipElse2 = IL.DefineLabel();
                IL.Emit(OpCodes.Brfalse, skipElse2);
                EmitAll(s.Else);
                IL.MarkLabel(skipElse2);
            }
            return;
        }

        // ── Normal path ───────────────────────────────────────────────────────
        if (hasFinally)
        {
            // Outer exception block for finally
            IL.BeginExceptionBlock();
            _ctx.ExceptionBlockDepth++;
        }

        if (hasHandlers)
        {
            // Inner exception block for handlers.
            // IMPORTANT: CLR only allows ONE BeginCatchBlock per exception type per
            // try block. We open a single catch(Exception) and dispatch to the
            // correct Python handler body using isinst checks at runtime.
            IL.BeginExceptionBlock();
            _ctx.ExceptionBlockDepth++;
            EmitAll(s.Body);

            if (noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Stloc, noExFlag);
            }

            // Single catch block for ALL handlers
            IL.BeginCatchBlock(typeof(Exception));
            var exTmp = _ctx.Locals.Declare($"__ex_{s.Line}", typeof(Exception));
            IL.Emit(OpCodes.Stloc, exTmp);

            if (noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Stloc, noExFlag);
            }

            // Label placed after EndExceptionBlock — all matched handlers Leave here
            var afterHandlers = IL.DefineLabel();

            for (int hi = 0; hi < s.Handlers.Count; hi++)
            {
                var handler = s.Handlers[hi];
                var skipHandler = IL.DefineLabel();
                var catchTypes = ResolveCatchTypes(handler);

                // Emit type check unless this is a bare `except:` (catches everything)
                bool isBareExcept = handler.ExceptionType is null;
                if (!isBareExcept)
                {
                    var matchLabel = IL.DefineLabel();
                    foreach (var ct in catchTypes)
                    {
                        IL.Emit(OpCodes.Ldloc, exTmp);
                        IL.Emit(OpCodes.Isinst, ct);
                        IL.Emit(OpCodes.Brtrue, matchLabel);
                    }
                    IL.Emit(OpCodes.Br, skipHandler);
                    IL.MarkLabel(matchLabel);
                }

                // Bind exception variable if handler has `as <name>`.
                // Fields (module-level vars) take priority over locals — same ordering
                // as NameEmitters.EmitName and AssignmentEmitters.EmitStore.
                if (handler.Name is not null)
                {
                    IL.Emit(OpCodes.Ldloc, exTmp);
                    if (_ctx.Fields.TryGetValue(handler.Name, out var exField))
                        IL.Emit(OpCodes.Stsfld, exField);
                    else
                    {
                        if (!_ctx.Locals.Contains(handler.Name))
                            _ctx.Locals.Declare(handler.Name, typeof(object));
                        _ctx.Locals.EmitStore(handler.Name);
                        _ctx.ExceptionHandlerVars.Add(handler.Name);
                    }
                }

                // Track the active handler exception for implicit chaining (__context__).
                // Any 'raise X' inside the handler body will see this and set X.__context__.
                var prevHandlerEx = _ctx.ActiveHandlerExceptionLocal;
                _ctx.ActiveHandlerExceptionLocal = exTmp;
                EmitAll(handler.Body);
                _ctx.ActiveHandlerExceptionLocal = prevHandlerEx;

                // If the handler body always terminates unconditionally (bare raise,
                // raise X, return), skip the deletion and Leave to avoid dead code
                // after Throw/Rethrow which can cause InvalidProgramException.
                bool handlerTerminates = StatementAnalyzer.EndsWithUnconditionalTransfer(handler.Body);
                if (!handlerTerminates)
                {
                    // Per Python semantics: delete the handler variable after the block.
                    if (handler.Name is not null)
                    {
                        IL.Emit(OpCodes.Ldsfld, NajaBuiltinsMethodCache.DeletedSentinel_Field);
                        if (_ctx.Fields.TryGetValue(handler.Name, out var exFieldDel))
                            IL.Emit(OpCodes.Stsfld, exFieldDel);
                        else
                            _ctx.Locals.EmitStore(handler.Name);
                    }

                    IL.Emit(OpCodes.Leave, afterHandlers);
                }
                IL.MarkLabel(skipHandler);
            }

            // No handler matched — rethrow with original stack trace
            IL.Emit(OpCodes.Rethrow);

            IL.EndExceptionBlock();
            _ctx.ExceptionBlockDepth--;
            IL.MarkLabel(afterHandlers);
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

        // Python semantics: else runs BEFORE finally, only if try succeeded (no exception).
        // Emit else clause HERE, before the finally block, while still inside exception block.
        if (hasElse && noExFlag is not null && !hasFinally)
        {
            // If no finally, emit else after all handlers/body complete
            IL.Emit(OpCodes.Ldloc, noExFlag);
            var skipElse = IL.DefineLabel();
            IL.Emit(OpCodes.Brfalse, skipElse);
            EmitAll(s.Else);
            IL.MarkLabel(skipElse);
        }

        if (hasFinally)
        {
            // Emit else BEFORE finally, still within the exception block
            if (hasElse && noExFlag is not null)
            {
                IL.Emit(OpCodes.Ldloc, noExFlag);
                var skipElse = IL.DefineLabel();
                IL.Emit(OpCodes.Brfalse, skipElse);
                EmitAll(s.Else);
                IL.MarkLabel(skipElse);
            }

            IL.BeginFinallyBlock();
            EmitAll(s.Finally);
            IL.EndExceptionBlock();
            _ctx.ExceptionBlockDepth--;
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

            bool hasImplicitChain = _ctx.ActiveHandlerExceptionLocal is not null;
            bool hasExplicitCause = s.Cause is not null;

            if (hasImplicitChain || hasExplicitCause)
            {
                // Must save/restore the exception so we can call chaining helpers
                // before the final Throw.
                var exTmp = _ctx.Locals.Declare($"__raise_{s.Line}", typeof(Exception));
                IL.Emit(OpCodes.Stloc, exTmp);

                // Implicit chaining: when a new exception is raised inside an except
                // handler, Python sets new.__context__ = caught_exception.
                if (hasImplicitChain)
                {
                    IL.Emit(OpCodes.Ldloc, exTmp);
                    IL.Emit(OpCodes.Ldloc, _ctx.ActiveHandlerExceptionLocal!);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.SetExceptionContext_Method);
                    IL.Emit(OpCodes.Pop);  // discard returned Exception; we have exTmp
                }

                // Explicit cause: 'raise X from Y' / 'raise X from None'
                if (hasExplicitCause)
                {
                    IL.Emit(OpCodes.Ldloc, exTmp);          // push Exception  (arg0)
                    _exprEmitter.Emit(s.Cause!);             // push cause      (arg1)
                    TypeMapper.EmitBox(IL, NajaTypes.Unknown);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.SetExceptionCause_Method);
                    // SetExceptionCause returns Exception — leave on stack for Throw
                }
                else
                {
                    IL.Emit(OpCodes.Ldloc, exTmp);           // push Exception for Throw
                }
            }

            IL.Emit(OpCodes.Throw);
        }
        else
        {
            // Bare `raise`. Inside an except handler (ActiveHandlerExceptionLocal
            // is set), IL `rethrow` is legal and re-raises the active exception.
            // OUTSIDE a handler it is INVALID IL (rethrow is only permitted in a
            // catch block) — the JIT turns it into a fatal Internal CLR Error
            // (0x80131506) that kills the whole process (crashed the bulk suite
            // via test_raise.py::test_invalid_reraise).
            // Python semantics: bare raise with no active exception raises
            // RuntimeError("No active exception to re-raise").
            if (_ctx.ActiveHandlerExceptionLocal is not null)
            {
                IL.Emit(OpCodes.Rethrow);
            }
            else
            {
                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.NoActiveException_Method);
                IL.Emit(OpCodes.Throw);
            }
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
                // Route the binding through the SAME resolution order as EmitName:
                // if Pass 1 hoisted this name to a static field (module-level var or
                // closure capture), NameEmitters loads fields BEFORE locals — storing
                // to a local here would leave the field null (store/load mismatch).
                if (_ctx.Fields.TryGetValue(n.Name, out var withField))
                {
                    IL.Emit(OpCodes.Stsfld, withField);
                }
                else
                {
                    if (!_ctx.Locals.Contains(n.Name))
                        _ctx.Locals.Declare(n.Name, typeof(object));
                    _ctx.Locals.EmitStore(n.Name);
                }
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

using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Statements;

/// <summary>
/// Emits IL for scope-related statements (nonlocal, with).
/// Manages variable hoisting and closure semantics.
/// </summary>
public class ScopeEmitters : StatementEmitterBase
{
    public ScopeEmitters(EmitContext ctx, ExpressionEmitter exprEmitter, Action<Statement> emitStatement)
        : base(ctx, exprEmitter, emitStatement)
    {
    }

    // ── Nonlocal ──────────────────────────────────────────────────────────────

    public void EmitNonlocal(NonlocalStatement s)
    {
        foreach (var name in s.Names)
        {
            if (_ctx.GlobalNames.Contains(name))
                throw new CodeGenException(
                    $"name '{name}' is nonlocal and global", s.Line, s.Column);
        }

        // For each nonlocal name, ensure it is backed by a module-level static field
        // so both the outer function and this inner function share the same storage cell.
        // If the outer function has already promoted the variable to _ctx.Fields (done
        // by EmitFunctionBody/EmitModule), this is a no-op. If it hasn't (e.g. when a
        // nonlocal refers to a variable that is only assigned inside the outer function
        // and wasn't seen yet), we declare a synthetic static field on the module type.
        foreach (var name in s.Names)
        {
            if (!_ctx.Fields.ContainsKey(name))
            {
                // Promote to a new static field on the module TypeBuilder
                var syntheticField = _ctx.TypeBuilder.DefineField(
                    $"__nl_{name}",
                    typeof(object),
                    FieldAttributes.Public | FieldAttributes.Static);
                _ctx.Fields[name] = syntheticField;
            }
            // Remove from locals if accidentally declared as a local so field wins
            // (LocalsManager doesn't support removal, but EmitName checks Fields first)
        }
    }

    // ── Return statement (expression variant that returns a value) ────────────

    public void EmitReturn(ReturnStatement s)
    {
        if (!_ctx.IsInsideFunction)
            throw new CodeGenException("'return' outside function", s.Line, s.Column);

        // In a coroutine generator body, 'return value' signals the end of iteration
        // by throwing NajaGeneratorReturn. The NajaGenerator thread catches this,
        // stores ReturnValue, and sets _done = true on the next MoveNext().
        if (_ctx.IsGeneratorBody)
        {
            if (s.Value is not null)
            {
                var vt = _exprEmitter.Emit(s.Value);
                TypeMapper.EmitBox(IL, vt);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }
            IL.Emit(OpCodes.Newobj, NajaBuiltinsMethodCache.NajaGeneratorReturn_Ctor);
            IL.Emit(OpCodes.Throw);
            return;
        }

        // Legacy path for old-style generators (GeneratorListLocal set) — kept for safety
        if (_ctx.GeneratorListLocal != null)
        {
            if (s.Value is not null)
            {
                _exprEmitter.Emit(s.Value);
                IL.Emit(OpCodes.Pop);
            }
            IL.Emit(OpCodes.Ldloc, _ctx.GeneratorListLocal);
            var iterCtor = typeof(NajaGeneratorIterator)
                .GetConstructor(new[] { typeof(System.Collections.Generic.List<object>) })!;
            IL.Emit(OpCodes.Newobj, iterCtor);
            EmitReturnOrLeave();
            return;
        }

        if (s.Value is not null)
        {
            var type = _exprEmitter.Emit(s.Value);
            if (_ctx.ReturnType == typeof(void))
            {
                IL.Emit(OpCodes.Pop);
                EmitReturnOrLeave(hasValue: false);
                return;
            }
            else if (_ctx.ReturnType == typeof(object))
            {
                TypeMapper.EmitBox(IL, type);
            }
            // else types match directly — value already on stack
            EmitReturnOrLeave(hasValue: true);
            return;
        }
        else if (_ctx.ReturnType == typeof(object))
        {
            IL.Emit(OpCodes.Ldnull);
            EmitReturnOrLeave(hasValue: true);
            return;
        }
        else if (_ctx.ReturnType != typeof(void))
        {
            IL.Emit(OpCodes.Ldc_I4_0);
            if (_ctx.ReturnType == typeof(long)) IL.Emit(OpCodes.Conv_I8);
            if (_ctx.ReturnType == typeof(double)) IL.Emit(OpCodes.Conv_R8);
            EmitReturnOrLeave(hasValue: true);
            return;
        }

        EmitReturnOrLeave(hasValue: false);
    }

    /// <summary>
    /// Emits either `ret` (outside any exception block) or `leave` (inside one).
    /// When using `leave`, the return value (if any) is first stored to
    /// <see cref="EmitContext.ReturnValueLocal"/> and the method epilog will
    /// reload and `ret` it after the exception block ends.
    /// Caller must have already pushed the return value onto the stack when hasValue=true.
    /// </summary>
    private void EmitReturnOrLeave(bool hasValue = false)
    {
        if (_ctx.ExceptionBlockDepth == 0)
        {
            IL.Emit(OpCodes.Ret);
            return;
        }

        // Inside a protected region: must use `leave` instead of `ret`.
        // Store the return value (if any) into a shared local so the epilog
        // can reload it after the exception block.
        if (hasValue)
        {
            if (_ctx.ReturnValueLocal is null)
            {
                var rvType = _ctx.ReturnType == typeof(void) ? typeof(object) : _ctx.ReturnType;
                _ctx.ReturnValueLocal = _ctx.Locals.Declare("__retval__", rvType);
            }
            IL.Emit(OpCodes.Stloc, _ctx.ReturnValueLocal);
        }

        // Lazily define the return-epilog label
        if (_ctx.MethodReturnLabel is null)
            _ctx.MethodReturnLabel = IL.DefineLabel();

        IL.Emit(OpCodes.Leave, _ctx.MethodReturnLabel.Value);
    }

    // ── Expression statement ──────────────────────────────────────────────────

    public void EmitExprStatement(ExprStatement s)
    {
        if (s.Expr is StarredExpr starExpr)
            throw new CodeGenException("starred expression not allowed here", starExpr.Line, starExpr.Column);
        _ = _exprEmitter.Emit(s.Expr);
        // Every Emit() leaves exactly one value on stack — always pop it
        IL.Emit(OpCodes.Pop);
    }
}

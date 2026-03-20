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
                    FieldAttributes.Private | FieldAttributes.Static);
                _ctx.Fields[name] = syntheticField;
            }
            // Remove from locals if accidentally declared as a local so field wins
            // (LocalsManager doesn't support removal, but EmitName checks Fields first)
        }
    }

    // ── Return statement (expression variant that returns a value) ────────────

    public void EmitReturn(ReturnStatement s)
    {
        // In a generator function, 'return' (with or without a value) terminates
        // iteration early. The collected yields are wrapped in a NajaGeneratorIterator
        // and returned; the return value itself is discarded in this eager model.
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
            IL.Emit(OpCodes.Ret);
            return;
        }

        if (s.Value is not null)
        {
            var type = _exprEmitter.Emit(s.Value);
            if (_ctx.ReturnType == typeof(void))
            {
                IL.Emit(OpCodes.Pop);
            }
            else if (_ctx.ReturnType == typeof(object))
            {
                // Box value types so they fit in object return slot
                TypeMapper.EmitBox(IL, type);
            }
            // else types match directly — emit as-is
        }
        else if (_ctx.ReturnType == typeof(object))
        {
            IL.Emit(OpCodes.Ldnull);
        }
        else if (_ctx.ReturnType != typeof(void))
        {
            // Return default for value types — push zero
            IL.Emit(OpCodes.Ldc_I4_0);
            if (_ctx.ReturnType == typeof(long)) IL.Emit(OpCodes.Conv_I8);
            if (_ctx.ReturnType == typeof(double)) IL.Emit(OpCodes.Conv_R8);
        }

        IL.Emit(OpCodes.Ret);
    }

    // ── Expression statement ──────────────────────────────────────────────────

    public void EmitExprStatement(ExprStatement s)
    {
        _ = _exprEmitter.Emit(s.Expr);
        // Every Emit() leaves exactly one value on stack — always pop it
        IL.Emit(OpCodes.Pop);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// TypeInferenceEngine — Integration Guide
//
// This file shows the MINIMAL changes needed to wire the inference engine into
// your existing pipeline.  Every change is additive — nothing existing breaks.
//
// Files to touch: EmitContext.cs, AssemblyEmitter.cs, ExpressionEmitter.cs
// New file:       TypeInferenceEngine.cs  (the engine itself)
// ═══════════════════════════════════════════════════════════════════════════════

// ─── 1. EmitContext.cs ────────────────────────────────────────────────────────
// Add ONE property.  Everything else stays exactly as-is.

/*
public sealed class EmitContext
{
    // ... all your existing properties unchanged ...

    /// <summary>
    /// Inference results for this module.
    /// Null when running without the inference pass (e.g. unit tests that
    /// construct EmitContext directly).
    /// </summary>
    public InferenceResult? Inference { get; init; }   // ← ADD THIS
}
*/

// ─── 2. AssemblyEmitter.cs ───────────────────────────────────────────────────
// Run inference once after semantic analysis, before any IL emission.
// Touch only the top of EmitModule().

/*
private TypeBuilder EmitModule(NajaModule module, ModuleBuilder modBuilder)
{
    // ── NEW: run type inference before any IL emission ────────────────────
    var inferenceEngine = new TypeInferenceEngine(_model);
    var inferenceResult = inferenceEngine.Infer(module);
    // inferenceResult is threaded into every EmitContext below
    // ─────────────────────────────────────────────────────────────────────

    var typeBuilder = modBuilder.DefineType("NajaModule", ...);
    // ... rest of your existing code ...

    // When you create mainCtx, add the inference result:
    var mainCtx = new EmitContext(mainIL, _model, typeBuilder, modBuilder,
                                  typeof(void), [])
    {
        Inference = inferenceResult   // ← ADD THIS  (C# 9 init property)
    };

    // Same for every ctx created in EmitFunctionBody / EmitMethodBody:
    var ctx = new EmitContext(il, _model, ct, modBuilder, typeof(object), paramNames)
    {
        IsInstanceMethod = !isStatic,
        SelfName         = skipSelf ? fn.Params[0].Name : null,
        Inference        = inferenceResult   // ← ADD THIS
    };
}
*/

// ─── 3. ExpressionEmitter.cs — EmitName ──────────────────────────────────────
// The only emitter method that needs to be touched.
// Replace the symbol-table lookup with an inference-first lookup.

/*
private NajaType EmitName(NameExpr e)
{
    // ... existing ldarg / local / field / method checks unchanged ...

    // 2. Check locals  (existing code)
    var local = _ctx.Locals.TryGet(e.Name);
    if (local is not null)
    {
        IL.Emit(OpCodes.Ldloc, local);

        // ── CHANGE: prefer inference result over local CLR type ───────────
        // The inference engine knows the NajaType even when the local
        // was declared as `object` (because it stores heterogeneous values).
        if (_ctx.Inference is not null)
        {
            var inferred = _ctx.Inference.GetType(e);
            if (inferred is not UnknownType) return inferred;
        }
        // ─────────────────────────────────────────────────────────────────

        if (local.LocalType == typeof(object))
            return NajaTypes.Unknown;
        return _ctx.Model.GetSymbol(e)?.Type ?? NajaTypes.Unknown;
    }

    // ... rest unchanged ...
}
*/

// ─── 4. ExpressionEmitter.cs — EmitBinary ────────────────────────────────────
// The inference engine pre-computed the result type.  If both operands resolved
// to concrete types, we can skip the DynamicAdd/Sub/Mul path entirely.
// This is the highest-value change — eliminates most boxing in tight loops.

/*
private NajaType EmitBinary(BinaryExpr e)
{
    // ── NEW: check if inference already resolved both sides ───────────────
    if (_ctx.Inference is not null)
    {
        var leftInferred  = _ctx.Inference.GetType(e.Left);
        var rightInferred = _ctx.Inference.GetType(e.Right);

        // If both sides are fully typed, we can emit direct opcodes
        // without going through DynamicAdd etc.
        bool leftKnown  = leftInferred  is not UnknownType;
        bool rightKnown = rightInferred is not UnknownType;

        if (leftKnown && rightKnown)
        {
            // Promote types if needed before emitting left/right
            var l = Emit(e.Left);
            var r = Emit(e.Right);
            // The existing switch cases already handle typed paths correctly
            // when neither side is UnknownType — fall through to them.
            // The only difference: we KNOW neither will be Unknown, so the
            // "if (l is UnknownType || r is UnknownType)" branches won't fire.
            // No code change needed — the existing logic is already correct here.
            _ = l; _ = r; // suppress unused warnings if restructured
        }
    }
    // ─────────────────────────────────────────────────────────────────────

    // existing switch (e.Op) ... unchanged below
}
*/

// ─── 5. StatementEmitter.cs — EmitStore (locals declaration) ─────────────────
// When declaring a local, use the inferred type instead of typeof(object).
// This makes the IL verifier happy and enables the emitter to use typed opcodes.

/*
private void EmitStore(Expression target, NajaType valType)
{
    if (target is NameExpr n)
    {
        // ── NEW: prefer inferred type for local declaration ───────────────
        NajaType declType = valType;
        if (_ctx.Inference is not null)
        {
            var inferred = _ctx.Inference.GetType(target);
            if (inferred is not UnknownType) declType = inferred;
        }
        var clrType = TypeMapper.ToClrType(declType);
        if (clrType == typeof(void)) clrType = typeof(object);
        // ─────────────────────────────────────────────────────────────────

        if (!_ctx.Locals.Contains(n.Name))
            _ctx.Locals.Declare(n.Name, clrType);   // was always typeof(object) before
        _ctx.Locals.EmitStore(n.Name);
    }
    // ... rest unchanged ...
}
*/

// ═══════════════════════════════════════════════════════════════════════════════
// AOT Gate usage
// ═══════════════════════════════════════════════════════════════════════════════

/*
// In your compiler driver / CLI:

var engine = new TypeInferenceEngine(semanticModel);
var result = engine.Infer(module);

// Report dynamic sites (for --warn-dynamic flag):
foreach (var site in result.DynamicSites)
    Console.WriteLine($"  DYNAMIC: {site.GetType().Name} at line {site.Line}:{site.Column}");

// AOT gate (for --target=aot flag):
foreach (var stmt in module.Body.OfType<FunctionDef>())
{
    if (!result.IsFullyStatic(stmt))
    {
        if (targetIsAot)
            throw new CodeGenException(
                $"Function '{stmt.Name}' has dynamic sites and cannot be compiled for AOT. " +
                $"Add type annotations or use --target=jit.",
                stmt.Line, stmt.Column);
    }
}
*/

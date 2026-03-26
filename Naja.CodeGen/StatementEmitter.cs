using System.Reflection.Emit;
using Naja.CodeGen.Emitters.Statements;
using Naja.Parser;
using Naja.Semantics;

// Disambiguate between System.Reflection.Emit.Label and System.Windows.Forms.Label
using ILLabel = System.Reflection.Emit.Label;

namespace Naja.CodeGen;

/// <summary>
/// Dispatcher for statement emission. Delegates to specialist emitters for each statement category.
/// Statements do NOT leave values on the stack (stack is balanced after each).
/// </summary>
public sealed class StatementEmitter
{
    private readonly EmitContext _ctx;
    private readonly ExpressionEmitter _expr;
    private ILGenerator IL => _ctx.IL;

    // Specialist emitters (lazily initialized)
    private AssignmentEmitters? _assignmentEmitters;
    private ControlFlowEmitters? _controlFlowEmitters;
    private ExceptionEmitters? _exceptionEmitters;
    private DefinitionEmitters? _definitionEmitters;
    private ScopeEmitters? _scopeEmitters;

    public StatementEmitter(EmitContext ctx)
    {
        _ctx = ctx;
        _expr = new ExpressionEmitter(ctx);
    }

    // ── Main dispatch ─────────────────────────────────────────────────────────

    public void Emit(Statement stmt)
    {
        switch (stmt)
        {
            // Assignment statements
            case AssignStatement s: GetAssignmentEmitters().EmitAssign(s); break;
            case AnnAssignStatement s: GetAssignmentEmitters().EmitAnnAssign(s); break;
            case AugAssignStatement s: GetAssignmentEmitters().EmitAugAssign(s); break;

            // Scope & expression
            case ExprStatement s: GetScopeEmitters().EmitExprStatement(s); break;
            case ReturnStatement s: GetScopeEmitters().EmitReturn(s); break;

            // Control flow
            case IfStatement s: GetControlFlowEmitters().EmitIf(s); break;
            case WhileStatement s: GetControlFlowEmitters().EmitWhile(s); break;
            case ForStatement s: GetControlFlowEmitters().EmitFor(s); break;
            case BreakStatement s: GetControlFlowEmitters().EmitBreak(s); break;
            case ContinueStatement s: GetControlFlowEmitters().EmitContinue(s); break;
            case MatchStatement s: EmitMatch(s); break;

            // Exception handling
            case TryStatement s: GetExceptionEmitters().EmitTry(s); break;
            case RaiseStatement s: GetExceptionEmitters().EmitRaise(s); break;
            case AssertStatement s: GetExceptionEmitters().EmitAssert(s); break;
            case WithStatement s: GetExceptionEmitters().EmitWith(s); break;

            // Definitions
            case FunctionDef s: GetDefinitionEmitters().EmitFunctionDef(s); break;
            case ClassDef s: GetDefinitionEmitters().EmitClassDef(s); break;

            // Scope management
            case NonlocalStatement s: GetScopeEmitters().EmitNonlocal(s); break;

            // No-ops
            case PassStatement _: break;
            case DeleteStatement s:
                foreach (var target in s.Targets)
                    if (target is NameExpr dn &&
                        !_ctx.Locals.Contains(dn.Name) &&
                        !_ctx.Fields.ContainsKey(dn.Name) &&
                        !_ctx.Parameters.Contains(dn.Name) &&
                        !_ctx.ClassTypes.ContainsKey(dn.Name) &&
                        !_ctx.Methods.ContainsKey(dn.Name))
                        throw new CodeGenException($"cannot delete undefined name '{dn.Name}'", dn.Line, dn.Column);
                break;
            case ImportStatement s:
                // Register locally-imported module names so NameEmitters can resolve them
                // (e.g. `import sys` inside a method body makes `sys` available as a name).
                foreach (var alias in s.Names)
                    _ctx.NamespaceImports[alias.Alias ?? alias.Name] = "";
                break;
            case FromImportStatement _: break;
            case GlobalStatement s:
                foreach (var name in s.Names) _ctx.GlobalNames.Add(name);
                break;
            case TypeAliasStatement _: break;

            default:
                throw new CodeGenException(
                    $"Cannot emit statement: {stmt.GetType().Name}",
                    stmt.Line, stmt.Column);
        }
    }

    // ── Utility methods ──────────────────────────────────────────────────────

    public void EmitAll(IReadOnlyList<Statement> stmts)
    {
        foreach (var s in stmts) Emit(s);
    }

    // ── Pattern matching for match statements ─────────────────────────────────

    private void EmitMatch(MatchStatement s)
    {
        var endLabel = IL.DefineLabel();
        var subjectLocal = _ctx.Locals.Declare($"__match_{s.Line}", typeof(object));

        var subjType = _expr.Emit(s.Subject);
        TypeMapper.EmitBox(IL, subjType);
        IL.Emit(OpCodes.Stloc, subjectLocal);

        foreach (var c in s.Cases)
        {
            var nextCase = IL.DefineLabel();

            // Emit pattern match check
            EmitPatternCheck(c.Pattern, subjectLocal, nextCase);

            // Guard
            if (c.Guard is not null)
            {
                _expr.Emit(c.Guard);
                IL.Emit(OpCodes.Brfalse, nextCase);
            }

            EmitAll(c.Body);
            IL.Emit(OpCodes.Br, endLabel);
            IL.MarkLabel(nextCase);
        }

        IL.MarkLabel(endLabel);
    }

    private void EmitPatternCheck(Pattern pattern, LocalBuilder subject, ILLabel noMatch)
    {
        var objEquals = typeof(object).GetMethod("Equals", new[] { typeof(object), typeof(object) })!;

        switch (pattern)
        {
            case WildcardPattern:
                break;  // always matches

            case CapturePattern cp:
                IL.Emit(OpCodes.Ldloc, subject);
                if (!_ctx.Locals.Contains(cp.Name))
                    _ctx.Locals.Declare(cp.Name, typeof(object));
                _ctx.Locals.EmitStore(cp.Name);
                break;

            case LiteralPattern lp:
                IL.Emit(OpCodes.Ldloc, subject);
                var litType = _expr.Emit(lp.Value);
                TypeMapper.EmitBox(IL, litType);
                IL.Emit(OpCodes.Call, objEquals);
                IL.Emit(OpCodes.Brfalse, noMatch);
                break;

            case OrPattern op:
                {
                    var matched = IL.DefineLabel();
                    foreach (var p in op.Patterns)
                    {
                        var tryNext = IL.DefineLabel();
                        EmitPatternCheck(p, subject, tryNext);
                        IL.Emit(OpCodes.Br, matched);
                        IL.MarkLabel(tryNext);
                    }
                    IL.Emit(OpCodes.Br, noMatch);
                    IL.MarkLabel(matched);
                    break;
                }

            case SequencePattern sp:
                {
                    var seqList = _ctx.Locals.Declare($"__seq_{subject.LocalIndex}", typeof(System.Collections.Generic.List<object?>));
                    var countProp = typeof(System.Collections.Generic.List<object?>).GetProperty("Count")!.GetGetMethod()!;
                    var listGetItem = typeof(System.Collections.Generic.List<object?>).GetProperty("Item")!.GetGetMethod()!;

                    IL.Emit(OpCodes.Ldloc, subject);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.UnpackIterable_Method);
                    IL.Emit(OpCodes.Stloc, seqList);

                    // Find star pattern position (-1 if none)
                    int starIdx = -1;
                    for (int si = 0; si < sp.Patterns.Count; si++)
                        if (sp.Patterns[si] is StarPattern) { starIdx = si; break; }

                    if (starIdx < 0)
                    {
                        // Exact length match
                        var nextSeq = IL.DefineLabel();
                        IL.Emit(OpCodes.Ldloc, seqList);
                        IL.Emit(OpCodes.Callvirt, countProp);
                        IL.Emit(OpCodes.Ldc_I4, sp.Patterns.Count);
                        IL.Emit(OpCodes.Beq_S, nextSeq);
                        IL.Emit(OpCodes.Br, noMatch);
                        IL.MarkLabel(nextSeq);

                        for (int i = 0; i < sp.Patterns.Count; i++)
                        {
                            var elemLocal = _ctx.Locals.Declare($"__elem_{subject.LocalIndex}_{i}", typeof(object));
                            IL.Emit(OpCodes.Ldloc, seqList);
                            IL.Emit(OpCodes.Ldc_I4, i);
                            IL.Emit(OpCodes.Callvirt, listGetItem);
                            IL.Emit(OpCodes.Stloc, elemLocal);
                            EmitPatternCheck(sp.Patterns[i], elemLocal, noMatch);
                        }
                    }
                    else
                    {
                        int suffixCount = sp.Patterns.Count - starIdx - 1;
                        int minLength = sp.Patterns.Count - 1;  // star can match 0 elements

                        // Store count for dynamic suffix indexing and star capture
                        var seqCountLocal = _ctx.Locals.Declare($"__seqcount_{subject.LocalIndex}", typeof(int));
                        IL.Emit(OpCodes.Ldloc, seqList);
                        IL.Emit(OpCodes.Callvirt, countProp);
                        IL.Emit(OpCodes.Stloc, seqCountLocal);

                        // count < minLength → noMatch
                        IL.Emit(OpCodes.Ldloc, seqCountLocal);
                        IL.Emit(OpCodes.Ldc_I4, minLength);
                        IL.Emit(OpCodes.Blt, noMatch);

                        // Match before-star elements (constant indices)
                        for (int i = 0; i < starIdx; i++)
                        {
                            var elemLocal = _ctx.Locals.Declare($"__elem_{subject.LocalIndex}_{i}", typeof(object));
                            IL.Emit(OpCodes.Ldloc, seqList);
                            IL.Emit(OpCodes.Ldc_I4, i);
                            IL.Emit(OpCodes.Callvirt, listGetItem);
                            IL.Emit(OpCodes.Stloc, elemLocal);
                            EmitPatternCheck(sp.Patterns[i], elemLocal, noMatch);
                        }

                        // Match after-star elements (runtime-computed index: seqCount - (suffixCount - j))
                        for (int j = 0; j < suffixCount; j++)
                        {
                            var elemLocal = _ctx.Locals.Declare($"__elem_{subject.LocalIndex}_{starIdx + 1 + j}", typeof(object));
                            IL.Emit(OpCodes.Ldloc, seqList);
                            IL.Emit(OpCodes.Ldloc, seqCountLocal);
                            IL.Emit(OpCodes.Ldc_I4, suffixCount - j);
                            IL.Emit(OpCodes.Sub);
                            IL.Emit(OpCodes.Callvirt, listGetItem);
                            IL.Emit(OpCodes.Stloc, elemLocal);
                            EmitPatternCheck(sp.Patterns[starIdx + 1 + j], elemLocal, noMatch);
                        }

                        // Star capture or discard
                        var star = (StarPattern)sp.Patterns[starIdx];
                        if (star.Name is not null && star.Name != "_")
                        {
                            if (!_ctx.Locals.Contains(star.Name))
                                _ctx.Locals.Declare(star.Name, typeof(object));
                            IL.Emit(OpCodes.Ldloc, seqList);
                            IL.Emit(OpCodes.Ldc_I4, starIdx);
                            IL.Emit(OpCodes.Ldc_I4, suffixCount);
                            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetUnpackSlice_Method);
                            _ctx.Locals.EmitStore(star.Name);
                        }
                    }
                    break;
                }

            case MappingPattern mp:
                {
                    IL.Emit(OpCodes.Ldloc, subject);
                    IL.Emit(OpCodes.Isinst, typeof(System.Collections.Generic.Dictionary<object, object>));
                    IL.Emit(OpCodes.Brfalse, noMatch);

                    for (int i = 0; i < mp.Pairs.Count; i++)
                    {
                        var (keyExpr, valuePattern) = mp.Pairs[i];

                        var keyLocal = _ctx.Locals.Declare($"__mapkey_{subject.LocalIndex}_{i}", typeof(object));
                        var keyType = _expr.Emit(keyExpr);
                        TypeMapper.EmitBox(IL, keyType);
                        IL.Emit(OpCodes.Stloc, keyLocal);

                        IL.Emit(OpCodes.Ldloc, subject);
                        IL.Emit(OpCodes.Ldloc, keyLocal);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.Contains_Method);
                        IL.Emit(OpCodes.Brfalse, noMatch);

                        var valLocal = _ctx.Locals.Declare($"__mapval_{subject.LocalIndex}_{i}", typeof(object));
                        IL.Emit(OpCodes.Ldloc, subject);
                        IL.Emit(OpCodes.Ldloc, keyLocal);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetItem_Method);
                        IL.Emit(OpCodes.Stloc, valLocal);

                        EmitPatternCheck(valuePattern, valLocal, noMatch);
                    }

                    if (mp.Rest is not null)
                    {
                        if (!_ctx.Locals.Contains(mp.Rest))
                            _ctx.Locals.Declare(mp.Rest, typeof(object));
                        IL.Emit(OpCodes.Ldloc, subject);
                        _ctx.Locals.EmitStore(mp.Rest);
                    }
                    break;
                }

            case AsPattern ap:
                {
                    // Check inner pattern; on success, bind the whole subject to the alias name
                    EmitPatternCheck(ap.Inner, subject, noMatch);
                    if (!_ctx.Locals.Contains(ap.Name))
                        _ctx.Locals.Declare(ap.Name, typeof(object));
                    IL.Emit(OpCodes.Ldloc, subject);
                    _ctx.Locals.EmitStore(ap.Name);
                    break;
                }

            case ClassPattern cp:
                {
                    // isinstance check: IsInstance(subject, classType)
                    IL.Emit(OpCodes.Ldloc, subject);
                    _expr.Emit(cp.Cls);
                    IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.IsInstance_Method);
                    IL.Emit(OpCodes.Brfalse, noMatch);

                    // Keyword sub-patterns: case Point(x=px, y=py)
                    foreach (var (attrName, subPattern) in cp.Keyword)
                    {
                        var attrLocal = _ctx.Locals.Declare($"__kw_{subject.LocalIndex}_{attrName}", typeof(object));
                        IL.Emit(OpCodes.Ldloc, subject);
                        IL.Emit(OpCodes.Ldstr, attrName);
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetAttr_Method);
                        IL.Emit(OpCodes.Stloc, attrLocal);
                        EmitPatternCheck(subPattern, attrLocal, noMatch);
                    }

                    // Positional sub-patterns: case Point(1, py) — resolves names via __match_args__
                    if (cp.Positional.Count > 0)
                    {
                        var matchArgsLocal = _ctx.Locals.Declare($"__matchargs_{subject.LocalIndex}", typeof(object));
                        _expr.Emit(cp.Cls);
                        IL.Emit(OpCodes.Ldstr, "__match_args__");
                        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetAttr_Method);
                        IL.Emit(OpCodes.Stloc, matchArgsLocal);

                        for (int i = 0; i < cp.Positional.Count; i++)
                        {
                            var attrNameLocal = _ctx.Locals.Declare($"__posname_{subject.LocalIndex}_{i}", typeof(string));
                            IL.Emit(OpCodes.Ldloc, matchArgsLocal);
                            IL.Emit(OpCodes.Ldc_I4, i);
                            IL.Emit(OpCodes.Box, typeof(int));
                            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetItem_Method);
                            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ToStr_Method);
                            IL.Emit(OpCodes.Stloc, attrNameLocal);

                            var attrValLocal = _ctx.Locals.Declare($"__posval_{subject.LocalIndex}_{i}", typeof(object));
                            IL.Emit(OpCodes.Ldloc, subject);
                            IL.Emit(OpCodes.Ldloc, attrNameLocal);
                            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.GetAttr_Method);
                            IL.Emit(OpCodes.Stloc, attrValLocal);

                            EmitPatternCheck(cp.Positional[i], attrValLocal, noMatch);
                        }
                    }
                    break;
                }

            default:
                throw new CodeGenException($"Unknown pattern type: {pattern.GetType().Name}", 0, 0);
        }
    }

    // ── Lazy-load specialist emitters ─────────────────────────────────────────

    private AssignmentEmitters GetAssignmentEmitters()
        => _assignmentEmitters ??= new AssignmentEmitters(_ctx, _expr, Emit);

    private ControlFlowEmitters GetControlFlowEmitters()
        => _controlFlowEmitters ??= new ControlFlowEmitters(_ctx, _expr, Emit);

    private ExceptionEmitters GetExceptionEmitters()
        => _exceptionEmitters ??= new ExceptionEmitters(_ctx, _expr, Emit);

    private DefinitionEmitters GetDefinitionEmitters()
        => _definitionEmitters ??= new DefinitionEmitters(_ctx, _expr, Emit);

    private ScopeEmitters GetScopeEmitters()
        => _scopeEmitters ??= new ScopeEmitters(_ctx, _expr, Emit);
}

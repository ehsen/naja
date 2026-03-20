using System.Reflection;
using System.Reflection.Emit;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Statements;

/// <summary>
/// Emits IL for definition statements (function def, class def).
/// Handles nested function creation, closures, generators, and class definitions.
/// </summary>
public class DefinitionEmitters : StatementEmitterBase
{
    public DefinitionEmitters(EmitContext ctx, ExpressionEmitter exprEmitter, Action<Statement> emitStatement)
        : base(ctx, exprEmitter, emitStatement)
    {
    }

    // ── Nested function def ───────────────────────────────────────────────────

    public void EmitFunctionDef(FunctionDef s)
    {
        // Reject duplicate parameter names
        var seen = new HashSet<string>();
        foreach (var p in s.Params)
            if (!seen.Add(p.Name))
                throw new CodeGenException(
                    $"duplicate argument '{p.Name}' in function definition", s.Line, s.Column);

        // Nested function — declare as a static method on the host type
        // and store a reference in a local (closures deferred to Phase 7)
        var pts = s.Params.Select(_ => typeof(object)).ToArray();
        var mb = _ctx.TypeBuilder.DefineMethod(
            $"{s.Name}_{s.Line}",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            pts);

        for (int i = 0; i < s.Params.Count; i++)
            mb.DefineParameter(i + 1, ParameterAttributes.None, s.Params[i].Name);

        var paramNames = s.Params.Select(p => p.Name).ToList();
        var fnCtx = new EmitContext(mb.GetILGenerator(), _ctx.Model,
                                    _ctx.TypeBuilder, _ctx.Module,
                                    typeof(object), paramNames);
        fnCtx.IsInsideFunction = true;

        // Propagate all outer-scope lookups into the nested function context
        foreach (var (k, v) in _ctx.Fields) fnCtx.Fields[k] = v;
        foreach (var (k, v) in _ctx.Methods) fnCtx.Methods[k] = v;
        foreach (var (k, v) in _ctx.MethodParamTypes) fnCtx.MethodParamTypes[k] = v;
        foreach (var (k, v) in _ctx.ClassTypes) fnCtx.ClassTypes[k] = v;
        foreach (var (k, v) in _ctx.ClassConstructors) fnCtx.ClassConstructors[k] = v;
        foreach (var (k, v) in _ctx.InstanceFields) fnCtx.InstanceFields[k] = v;
        foreach (var (k, v) in _ctx.AllClassMethods) fnCtx.AllClassMethods[k] = v;
        foreach (var (k, v) in _ctx.AllClassMethodParamTypes) fnCtx.AllClassMethodParamTypes[k] = v;
        foreach (var mn in _ctx.ClassMethods) fnCtx.ClassMethods.Add(mn);
        foreach (var (k, v) in _ctx.ImportMap) fnCtx.ImportMap[k] = v;
        foreach (var (k, v) in _ctx.NamespaceImports) fnCtx.NamespaceImports[k] = v;
        fnCtx.SelfName = _ctx.SelfName;
        fnCtx.IsInstanceMethod = _ctx.IsInstanceMethod;

        // Pre-scan for nonlocal declarations in this function body and promote those
        // variables to static fields NOW (before emitting the body) so any inner
        // lambdas/closures also see them as fields.
        var nonlocalNames = CollectNonlocalNames(s.Body);
        foreach (var nlName in nonlocalNames)
        {
            if (!fnCtx.Fields.ContainsKey(nlName))
            {
                var nlField = _ctx.TypeBuilder.DefineField($"__nl_{nlName}", typeof(object), FieldAttributes.Private | FieldAttributes.Static);
                fnCtx.Fields[nlName] = nlField;
                _ctx.Fields[nlName] = nlField;  // also visible in outer scope
            }
        }

        // Hoist variables referenced by nested functions: if an inner function
        // references a name that is assigned in this function, promote that name
        // to a module-level static field so the inner function sees the enclosing
        // binding (Python LEGB semantics).
        var nestedRefs = CollectNamesReferencedByNestedFunctions(s.Body);
        var assigned = CollectAssignedNames(s.Body);
        foreach (var r in nestedRefs.Intersect(assigned))
        {
            if (!fnCtx.Fields.ContainsKey(r))
            {
                var hoisted = _ctx.TypeBuilder.DefineField($"__nl_{r}", typeof(object), FieldAttributes.Private | FieldAttributes.Static);
                fnCtx.Fields[r] = hoisted;
                _ctx.Fields[r] = hoisted;
            }
        }

        // (Closure hoisting moved to AssemblyEmitter.EmitFunctionBody so promotion
        // happens before the outer function body is emitted.)

        // Check if this function contains yield statements (is a generator)
        bool isGenerator = ContainsYield(s.Body);
        if (isGenerator)
        {
            // Initialize the generator list at function entry — MUST use fnCtx.IL (the new method's ILGenerator)
            var listType = typeof(System.Collections.Generic.List<object>);
            var ctor = listType.GetConstructor(Type.EmptyTypes)!;
            fnCtx.GeneratorListLocal = fnCtx.Locals.Declare($"__generator_{s.Name}_{s.Line}", listType);
            fnCtx.IL.Emit(OpCodes.Newobj, ctor);
            fnCtx.IL.Emit(OpCodes.Stloc, fnCtx.GeneratorListLocal);
        }

        var fnIL = fnCtx.IL;  // always use the nested function's ILGenerator for its body

        var bodyEmitter = new StatementEmitter(fnCtx);
        bodyEmitter.EmitAll(s.Body);

        if (isGenerator)
        {
            if (fnCtx.MethodReturnLabel.HasValue)
            {
                if (fnCtx.ReturnValueLocal != null)
                {
                    fnIL.Emit(OpCodes.Ldnull);
                    fnIL.Emit(OpCodes.Stloc, fnCtx.ReturnValueLocal);
                }
                fnIL.Emit(OpCodes.Br, fnCtx.MethodReturnLabel.Value);
                fnIL.MarkLabel(fnCtx.MethodReturnLabel.Value);
                if (fnCtx.ReturnValueLocal != null)
                    fnIL.Emit(OpCodes.Ldloc, fnCtx.ReturnValueLocal);
                else
                {
                    fnIL.Emit(OpCodes.Ldloc, fnCtx.GeneratorListLocal!);
                    var iterCtor2 = typeof(NajaGeneratorIterator)
                        .GetConstructor(new[] { typeof(System.Collections.Generic.List<object>) })!;
                    fnIL.Emit(OpCodes.Newobj, iterCtor2);
                }
                fnIL.Emit(OpCodes.Ret);
            }
            else
            {
                bool endsWithReturn = s.Body.Count > 0 && s.Body[^1] is ReturnStatement;
                if (!endsWithReturn)
                {
                    fnIL.Emit(OpCodes.Ldloc, fnCtx.GeneratorListLocal!);
                    var iterCtor = typeof(NajaGeneratorIterator)
                        .GetConstructor(new[] { typeof(System.Collections.Generic.List<object>) })!;
                    fnIL.Emit(OpCodes.Newobj, iterCtor);
                    fnIL.Emit(OpCodes.Ret);
                }
            }
        }
        else
        {
            // Non-generator: use the shared epilog helper pattern inline
            if (fnCtx.MethodReturnLabel.HasValue)
            {
                if (fnCtx.ReturnValueLocal != null)
                {
                    fnIL.Emit(OpCodes.Ldnull);
                    fnIL.Emit(OpCodes.Stloc, fnCtx.ReturnValueLocal);
                }
                fnIL.Emit(OpCodes.Br, fnCtx.MethodReturnLabel.Value);
                fnIL.MarkLabel(fnCtx.MethodReturnLabel.Value);
                if (fnCtx.ReturnValueLocal != null)
                    fnIL.Emit(OpCodes.Ldloc, fnCtx.ReturnValueLocal);
                else
                    fnIL.Emit(OpCodes.Ldnull);
                fnIL.Emit(OpCodes.Ret);
            }
            else
            {
                bool endsWithReturn = s.Body.Count > 0 && s.Body[^1] is ReturnStatement;
                if (!endsWithReturn)
                {
                    fnIL.Emit(OpCodes.Ldnull);
                    fnIL.Emit(OpCodes.Ret);
                }
            }
        }

        // Register in current context so calls within scope find it
        _ctx.Methods[s.Name] = mb;
        _ctx.MethodParamTypes[s.Name] = pts;

        // Push null as the "function object" value — local variable holds method ref
        // Full delegate creation in Phase 7
    }

    // ── Class def ─────────────────────────────────────────────────────────────

    public void EmitClassDef(ClassDef s)
    {
        // Inline class definitions (nested in functions) — no-op for now
        // Top-level classes are handled by AssemblyEmitter
    }

    // ── Yield detection ───────────────────────────────────────────────────────

    /// <summary>Check if a statement list contains any yield expressions (public wrapper for AssemblyEmitter).</summary>
    public static bool ContainsYieldStatic(IReadOnlyList<Statement> statements) => ContainsYield(statements);

    /// <summary>Check if a statement list contains any yield expressions.</summary>
    public static bool ContainsYield(IReadOnlyList<Statement> statements)
    {
        foreach (var stmt in statements)
        {
            if (ContainsYieldInStatement(stmt))
                return true;
        }
        return false;
    }

    private static bool ContainsYieldInStatement(Statement stmt)
    {
        return stmt switch
        {
            ExprStatement es => ContainsYieldInExpression(es.Expr),
            ReturnStatement rs => rs.Value is not null && ContainsYieldInExpression(rs.Value),
            IfStatement ifs => ContainsYield(ifs.Then) ||
                              ifs.Elifs.Any(e => ContainsYield(e.Body)) ||
                              ContainsYield(ifs.Else),
            WhileStatement ws => ContainsYield(ws.Body) || ContainsYield(ws.Else),
            ForStatement fs => ContainsYield(fs.Body) || ContainsYield(fs.Else),
            TryStatement ts => ContainsYield(ts.Body) ||
                              ts.Handlers.Any(h => ContainsYield(h.Body)) ||
                              ContainsYield(ts.Else) ||
                              ContainsYield(ts.Finally),
            WithStatement ws => ContainsYield(ws.Body),
            AssignStatement ass => ass.Value is not null && ContainsYieldInExpression(ass.Value),
            AnnAssignStatement aas => aas.Value is not null && ContainsYieldInExpression(aas.Value),
            AugAssignStatement aas => ContainsYieldInExpression(aas.Value),
            _ => false
        };
    }

    private static bool ContainsYieldInExpression(Expression expr)
    {
        return expr switch
        {
            YieldExpr => true,
            BinaryExpr be => ContainsYieldInExpression(be.Left) || ContainsYieldInExpression(be.Right),
            UnaryExpr ue => ContainsYieldInExpression(ue.Operand),
            BoolOpExpr boe => boe.Values.Any(ContainsYieldInExpression),
            CompareExpr ce => ContainsYieldInExpression(ce.Left) || ce.Comparators.Any(c => ContainsYieldInExpression(c.Right)),
            IfExpr ie => ContainsYieldInExpression(ie.Condition) || ContainsYieldInExpression(ie.Then) || ContainsYieldInExpression(ie.Else),
            CallExpr ce => ce.Args.Any(a => ContainsYieldInExpression(a.Value)),
            AttributeExpr ae => ContainsYieldInExpression(ae.Object),
            SubscriptExpr se => ContainsYieldInExpression(se.Object) || ContainsYieldInExpression(se.Index),
            ListExpr le => le.Elements.Any(ContainsYieldInExpression),
            TupleExpr te => te.Elements.Any(ContainsYieldInExpression),
            SetExpr se => se.Elements.Any(ContainsYieldInExpression),
            DictExpr de => de.Pairs.Any(p => (p.Key is null || ContainsYieldInExpression(p.Key)) && ContainsYieldInExpression(p.Value)),
            ListCompExpr lce => ContainsYieldInComprehension(lce.Generators) || ContainsYieldInExpression(lce.Element),
            SetCompExpr sce => ContainsYieldInComprehension(sce.Generators) || ContainsYieldInExpression(sce.Element),
            DictCompExpr dce => ContainsYieldInComprehension(dce.Generators) || ContainsYieldInExpression(dce.Key) || ContainsYieldInExpression(dce.Value),
            GeneratorExpr ge => ContainsYieldInComprehension(ge.Generators) || ContainsYieldInExpression(ge.Element),
            _ => false
        };
    }

    private static bool ContainsYieldInComprehension(IReadOnlyList<Comprehension> generators)
    {
        foreach (var gen in generators)
        {
            if (ContainsYieldInExpression(gen.Iter))
                return true;
            foreach (var cond in gen.Conditions)
            {
                if (ContainsYieldInExpression(cond))
                    return true;
            }
        }
        return false;
    }

    // ── Name collection helpers ───────────────────────────────────────────────

    private static HashSet<string> CollectAssignedNames(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body)
        {
            switch (stmt)
            {
                case AssignStatement a:
                    foreach (var t in a.Targets)
                        if (t is NameExpr ne) names.Add(ne.Name);
                    break;
                case AnnAssignStatement aa:
                    if (aa.Target is NameExpr ne2) names.Add(ne2.Name);
                    break;
                case ForStatement fs:
                    if (fs.Target is NameExpr ne3) names.Add(ne3.Name);
                    foreach (var s in fs.Body) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    break;
                case IfStatement ifs:
                    foreach (var s in ifs.Then) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    foreach (var (_, b) in ifs.Elifs) foreach (var s in b) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    foreach (var s in ifs.Else) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    break;
                default:
                    break;
            }
        }
        return names;
    }

    private static HashSet<string> CollectReferencedNames(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body)
            CollectReferencedNamesInStmt(stmt, names);
        return names;
    }

    private static void CollectReferencedNamesInStmt(Statement stmt, HashSet<string> names)
    {
        switch (stmt)
        {
            case ExprStatement es:
                CollectNamesInExpr(es.Expr, names);
                break;
            case AssignStatement a:
                CollectNamesInExpr(a.Value, names);
                break;
            case AnnAssignStatement aa:
                if (aa.Value is not null) CollectNamesInExpr(aa.Value, names);
                break;
            case IfStatement ifs:
                CollectNamesInExpr(ifs.Condition, names);
                foreach (var s in ifs.Then) CollectReferencedNamesInStmt(s, names);
                foreach (var (_, b) in ifs.Elifs) foreach (var s in b) CollectReferencedNamesInStmt(s, names);
                foreach (var s in ifs.Else) CollectReferencedNamesInStmt(s, names);
                break;
            case ReturnStatement rs:
                if (rs.Value is not null) CollectNamesInExpr(rs.Value, names);
                break;
            case ForStatement fs:
                CollectNamesInExpr(fs.Iter, names);
                foreach (var s in fs.Body) CollectReferencedNamesInStmt(s, names);
                break;
            case WhileStatement ws:
                CollectNamesInExpr(ws.Condition, names);
                foreach (var s in ws.Body) CollectReferencedNamesInStmt(s, names);
                break;
            case WithStatement w:
                foreach (var item in w.Items) CollectNamesInExpr(item.Context, names);
                foreach (var s in w.Body) CollectReferencedNamesInStmt(s, names);
                break;
            case TryStatement ts:
                foreach (var s in ts.Body) CollectReferencedNamesInStmt(s, names);
                foreach (var h in ts.Handlers) foreach (var s in h.Body) CollectReferencedNamesInStmt(s, names);
                break;
            // Other statement kinds ignored (conservative)
            default:
                break;
        }
    }

    private static void CollectNamesInExpr(Expression expr, HashSet<string> names)
    {
        if (expr == null) return;
        switch (expr)
        {
            case NameExpr ne: names.Add(ne.Name); break;
            case BinaryExpr be: CollectNamesInExpr(be.Left, names); CollectNamesInExpr(be.Right, names); break;
            case UnaryExpr ue: CollectNamesInExpr(ue.Operand, names); break;
            case BoolOpExpr bo: foreach (var v in bo.Values) CollectNamesInExpr(v, names); break;
            case CompareExpr ce: CollectNamesInExpr(ce.Left, names); foreach (var (_, r) in ce.Comparators) CollectNamesInExpr(r, names); break;
            case IfExpr ie: CollectNamesInExpr(ie.Condition, names); CollectNamesInExpr(ie.Then, names); CollectNamesInExpr(ie.Else, names); break;
            case CallExpr ce2: CollectNamesInExpr(ce2.Func, names); foreach (var a in ce2.Args) CollectNamesInExpr(a.Value, names); break;
            case AttributeExpr ae: CollectNamesInExpr(ae.Object, names); break;
            case SubscriptExpr se: CollectNamesInExpr(se.Object, names); if (se.Index is not null) CollectNamesInExpr(se.Index, names); break;
            case ListExpr le: foreach (var e in le.Elements) CollectNamesInExpr(e, names); break;
            case TupleExpr te: foreach (var e in te.Elements) CollectNamesInExpr(e, names); break;
            case DictExpr de: foreach (var p in de.Pairs) { if (p.Key is not null) CollectNamesInExpr(p.Key, names); CollectNamesInExpr(p.Value, names); } break;
            case ListCompExpr lce: CollectNamesInExpr(lce.Element, names); foreach (var g in lce.Generators) { CollectNamesInExpr(g.Iter, names); foreach (var c in g.Conditions) CollectNamesInExpr(c, names); } break;
            case LambdaExpr le2: CollectNamesInExpr(le2.Body, names); break;
            default: break;
        }
    }

    private static HashSet<string> CollectNamesReferencedByNestedFunctions(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body)
        {
            if (stmt is FunctionDef fn)
            {
                var nested = CollectReferencedNames(fn.Body);
                foreach (var n in nested) names.Add(n);
            }
            else if (stmt is IfStatement ifs)
            {
                foreach (var s in ifs.Then) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var (_, b) in ifs.Elifs) foreach (var s in b) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var s in ifs.Else) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is ForStatement fs)
            {
                foreach (var s in fs.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is WhileStatement ws)
            {
                foreach (var s in ws.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is TryStatement ts)
            {
                foreach (var s in ts.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var h in ts.Handlers) foreach (var s in h.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is WithStatement w)
            {
                foreach (var s in w.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
        }
        return names;
    }

    private static HashSet<string> CollectNonlocalNames(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body)
            CollectNonlocalNamesInStmt(stmt, names);
        return names;
    }

    private static void CollectNonlocalNamesInStmt(Statement stmt, HashSet<string> names)
    {
        switch (stmt)
        {
            case NonlocalStatement nl:
                foreach (var n in nl.Names) names.Add(n);
                break;
            case IfStatement ifs:
                foreach (var s in ifs.Then) CollectNonlocalNamesInStmt(s, names);
                foreach (var (_, b) in ifs.Elifs) foreach (var s in b) CollectNonlocalNamesInStmt(s, names);
                foreach (var s in ifs.Else) CollectNonlocalNamesInStmt(s, names);
                break;
            case WhileStatement ws:
                foreach (var s in ws.Body) CollectNonlocalNamesInStmt(s, names);
                break;
            case ForStatement fs:
                foreach (var s in fs.Body) CollectNonlocalNamesInStmt(s, names);
                break;
            case TryStatement ts:
                foreach (var s in ts.Body) CollectNonlocalNamesInStmt(s, names);
                foreach (var h in ts.Handlers) foreach (var s in h.Body) CollectNonlocalNamesInStmt(s, names);
                break;
            case WithStatement wts:
                foreach (var s in wts.Body) CollectNonlocalNamesInStmt(s, names);
                break;
                // Do NOT recurse into nested FunctionDef — they have their own nonlocal scopes
        }
    }
}

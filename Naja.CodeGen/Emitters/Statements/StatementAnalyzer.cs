using Naja.Parser;

namespace Naja.CodeGen.Emitters.Statements;

/// <summary>
/// Static utility helpers for statement analysis (used by AssemblyEmitter and code generation).
/// </summary>
public static class StatementAnalyzer
{
    /// <summary>
    /// Collect names that are assigned to (targets of assignments) within the given statement list.
    /// </summary>
    public static HashSet<string> CollectAssignedNames(IReadOnlyList<Statement> body)
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
                case FunctionDef fd:
                    names.Add(fd.Name);
                    break;
                case ClassDef cd:
                    names.Add(cd.Name);
                    break;
                case ImportStatement im:
                    foreach (var n in im.Names) names.Add(n.Alias ?? n.Name.Split('.')[0]);
                    break;
                case FromImportStatement fim:
                    foreach (var n in fim.Names) names.Add(n.Alias ?? n.Name);
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
                case TryStatement ts:
                    foreach (var s in ts.Body) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    foreach (var h in ts.Handlers) foreach (var s in h.Body) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    foreach (var s in ts.Else) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    foreach (var s in ts.Finally) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    break;
                case WhileStatement ws:
                    foreach (var s in ws.Body) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    foreach (var s in ws.Else) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    break;
                case WithStatement wts:
                    foreach (var s in wts.Body) foreach (var n in CollectAssignedNames(new[] { s })) names.Add(n);
                    break;
                default:
                    break;
            }
        }
        return names;
    }

    /// <summary>
    /// Deep-scan for `import X` statements ANYWHERE in the module (inside try, if,
    /// for, while, with bodies — the CPython optional-import pattern lives in
    /// `try: import x / except ImportError: x = None`). Emits the same list the
    /// top-level EmitModule pre-pass consumes, so imports nested in control flow
    /// resolve identically to top-level ones in every scope.
    /// </summary>
    public static List<(string ModuleName, string LocalName)> CollectImportBindings(
        IReadOnlyList<Statement> body)
    {
        var bindings = new List<(string, string)>();
        foreach (var stmt in body)
        {
            switch (stmt)
            {
                case ImportStatement im:
                    foreach (var n in im.Names)
                        bindings.Add((n.Name, n.Alias ?? n.Name.Split('.')[0]));
                    break;
                case IfStatement ifs:
                    bindings.AddRange(CollectImportBindings(ifs.Then));
                    foreach (var (_, b) in ifs.Elifs) bindings.AddRange(CollectImportBindings(b));
                    bindings.AddRange(CollectImportBindings(ifs.Else));
                    break;
                case TryStatement ts:
                    bindings.AddRange(CollectImportBindings(ts.Body));
                    foreach (var h in ts.Handlers) bindings.AddRange(CollectImportBindings(h.Body));
                    bindings.AddRange(CollectImportBindings(ts.Else));
                    bindings.AddRange(CollectImportBindings(ts.Finally));
                    break;
                case ForStatement fs:
                    bindings.AddRange(CollectImportBindings(fs.Body));
                    bindings.AddRange(CollectImportBindings(fs.Else));
                    break;
                case WhileStatement ws:
                    bindings.AddRange(CollectImportBindings(ws.Body));
                    bindings.AddRange(CollectImportBindings(ws.Else));
                    break;
                case WithStatement wts:
                    bindings.AddRange(CollectImportBindings(wts.Body));
                    break;
            }
        }
        return bindings;
    }

    /// <summary>
    /// Find referenced NameExpr identifiers in a statement list (simple conservative scan).
    /// </summary>
    public static HashSet<string> CollectReferencedNames(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body)
            CollectReferencedNamesInStmt(stmt, names);
        return names;
    }

    /// <summary>
    /// Collect names that are referenced inside any nested FunctionDef bodies within the provided statements.
    /// </summary>
    public static HashSet<string> CollectNamesReferencedByNestedFunctions(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body)
        {
            if (stmt is FunctionDef fn)
            {
                var referenced = CollectReferencedNames(fn.Body);
                var transitivelyNeeded = CollectNamesReferencedByNestedFunctions(fn.Body);
                var locallyAssigned = CollectAssignedNames(fn.Body);
                var nonlocalNames = CollectNonlocalNames(fn.Body);
                foreach (var nl in nonlocalNames) locallyAssigned.Remove(nl);
                foreach (var p in fn.Params) locallyAssigned.Add(p.Name);
                foreach (var n in referenced.Union(transitivelyNeeded))
                    if (!locallyAssigned.Contains(n))
                        names.Add(n);
                
                foreach (var p in fn.Params) if (p.Default != null) CollectNamesReferencedByNestedFunctionsInExpr(p.Default, names);
                foreach (var d in fn.Decorators) CollectNamesReferencedByNestedFunctionsInExpr(d, names);
            }
            else if (stmt is IfStatement ifs)
            {
                CollectNamesReferencedByNestedFunctionsInExpr(ifs.Condition, names);
                foreach (var s in ifs.Then) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var (_, b) in ifs.Elifs) foreach (var s in b) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var s in ifs.Else) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is ForStatement fs)
            {
                CollectNamesReferencedByNestedFunctionsInExpr(fs.Iter, names);
                foreach (var s in fs.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var s in fs.Else) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is WhileStatement ws)
            {
                CollectNamesReferencedByNestedFunctionsInExpr(ws.Condition, names);
                foreach (var s in ws.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var s in ws.Else) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is TryStatement ts)
            {
                foreach (var s in ts.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var h in ts.Handlers) foreach (var s in h.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var s in ts.Else) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
                foreach (var s in ts.Finally) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is WithStatement w)
            {
                foreach (var item in w.Items) CollectNamesReferencedByNestedFunctionsInExpr(item.Context, names);
                foreach (var s in w.Body) foreach (var n in CollectNamesReferencedByNestedFunctions(new[] { s })) names.Add(n);
            }
            else if (stmt is ClassDef cls)
            {
                foreach (var b in cls.Bases) CollectNamesReferencedByNestedFunctionsInExpr(b, names);
                foreach (var d in cls.Decorators) CollectNamesReferencedByNestedFunctionsInExpr(d, names);
                
                foreach (var m in cls.Body.OfType<FunctionDef>())
                {
                    var referenced = CollectReferencedNames(m.Body);
                    var locallyDefined = CollectAssignedNames(m.Body);
                    foreach (var p in m.Params) locallyDefined.Add(p.Name);
                    var transitive = CollectNamesReferencedByNestedFunctions(m.Body);
                    foreach (var n in referenced.Union(transitive))
                        if (!locallyDefined.Contains(n))
                            names.Add(n);
                }
            }
            else if (stmt is ExprStatement es) CollectNamesReferencedByNestedFunctionsInExpr(es.Expr, names);
            else if (stmt is AssignStatement a) { CollectNamesReferencedByNestedFunctionsInExpr(a.Value, names); foreach (var t in a.Targets) CollectNamesReferencedByNestedFunctionsInExpr(t as Expression, names); }
            else if (stmt is AnnAssignStatement aa) { if (aa.Value != null) CollectNamesReferencedByNestedFunctionsInExpr(aa.Value, names); }
            else if (stmt is AugAssignStatement au) { CollectNamesReferencedByNestedFunctionsInExpr(au.Target, names); CollectNamesReferencedByNestedFunctionsInExpr(au.Value, names); }
            else if (stmt is ReturnStatement rs) { if (rs.Value != null) CollectNamesReferencedByNestedFunctionsInExpr(rs.Value, names); }
            else if (stmt is AssertStatement ast) { CollectNamesReferencedByNestedFunctionsInExpr(ast.Test, names); if (ast.Message != null) CollectNamesReferencedByNestedFunctionsInExpr(ast.Message, names); }
            else if (stmt is RaiseStatement rst) { if (rst.Exception != null) CollectNamesReferencedByNestedFunctionsInExpr(rst.Exception, names); if (rst.Cause != null) CollectNamesReferencedByNestedFunctionsInExpr(rst.Cause, names); }
        }
        return names;
    }

    private static void CollectNamesReferencedByNestedFunctionsInExpr(Expression? expr, HashSet<string> names)
    {
        if (expr == null) return;
        switch (expr)
        {
            case LambdaExpr le:
                var referenced = CollectReferencedNames(new[] { new ExprStatement(le.Body, le.Line, le.Column) });
                var transitivelyNeeded = new HashSet<string>();
                CollectNamesReferencedByNestedFunctionsInExpr(le.Body, transitivelyNeeded);
                var locallyAssigned = new HashSet<string>();
                foreach (var p in le.Params) locallyAssigned.Add(p.Name);
                foreach (var n in referenced.Union(transitivelyNeeded))
                    if (!locallyAssigned.Contains(n))
                        names.Add(n);
                break;
            case BinaryExpr be: CollectNamesReferencedByNestedFunctionsInExpr(be.Left, names); CollectNamesReferencedByNestedFunctionsInExpr(be.Right, names); break;
            case UnaryExpr ue: CollectNamesReferencedByNestedFunctionsInExpr(ue.Operand, names); break;
            case BoolOpExpr bo: foreach (var v in bo.Values) CollectNamesReferencedByNestedFunctionsInExpr(v, names); break;
            case CompareExpr ce: CollectNamesReferencedByNestedFunctionsInExpr(ce.Left, names); foreach (var (_, r) in ce.Comparators) CollectNamesReferencedByNestedFunctionsInExpr(r, names); break;
            case IfExpr ie: CollectNamesReferencedByNestedFunctionsInExpr(ie.Condition, names); CollectNamesReferencedByNestedFunctionsInExpr(ie.Then, names); CollectNamesReferencedByNestedFunctionsInExpr(ie.Else, names); break;
            case CallExpr ce2: CollectNamesReferencedByNestedFunctionsInExpr(ce2.Func, names); foreach (var a in ce2.Args) CollectNamesReferencedByNestedFunctionsInExpr(a.Value, names); break;
            case AttributeExpr ae: CollectNamesReferencedByNestedFunctionsInExpr(ae.Object, names); break;
            case SubscriptExpr se: CollectNamesReferencedByNestedFunctionsInExpr(se.Object, names); CollectNamesReferencedByNestedFunctionsInExpr(se.Index, names); break;
            case ListExpr le: foreach (var e in le.Elements) CollectNamesReferencedByNestedFunctionsInExpr(e, names); break;
            case TupleExpr te: foreach (var e in te.Elements) CollectNamesReferencedByNestedFunctionsInExpr(e, names); break;
            case SetExpr se: foreach (var e in se.Elements) CollectNamesReferencedByNestedFunctionsInExpr(e, names); break;
            case DictExpr de: foreach (var p in de.Pairs) { CollectNamesReferencedByNestedFunctionsInExpr(p.Key, names); CollectNamesReferencedByNestedFunctionsInExpr(p.Value, names); } break;
            case ListCompExpr lce: CollectNamesReferencedByNestedFunctionsInExpr(lce.Element, names); foreach (var g in lce.Generators) { CollectNamesReferencedByNestedFunctionsInExpr(g.Iter, names); foreach (var c in g.Conditions) CollectNamesReferencedByNestedFunctionsInExpr(c, names); } break;
            case SetCompExpr sce: CollectNamesReferencedByNestedFunctionsInExpr(sce.Element, names); foreach (var g in sce.Generators) { CollectNamesReferencedByNestedFunctionsInExpr(g.Iter, names); foreach (var c in g.Conditions) CollectNamesReferencedByNestedFunctionsInExpr(c, names); } break;
            case DictCompExpr dce: CollectNamesReferencedByNestedFunctionsInExpr(dce.Key, names); CollectNamesReferencedByNestedFunctionsInExpr(dce.Value, names); foreach (var g in dce.Generators) { CollectNamesReferencedByNestedFunctionsInExpr(g.Iter, names); foreach (var c in g.Conditions) CollectNamesReferencedByNestedFunctionsInExpr(c, names); } break;
            case GeneratorExpr ge: CollectNamesReferencedByNestedFunctionsInExpr(ge.Element, names); foreach (var g in ge.Generators) { CollectNamesReferencedByNestedFunctionsInExpr(g.Iter, names); foreach (var c in g.Conditions) CollectNamesReferencedByNestedFunctionsInExpr(c, names); } break;
            case YieldExpr ye: CollectNamesReferencedByNestedFunctionsInExpr(ye.Value, names); break;
        }
    }

    /// <summary>
    /// Returns true if the last statement in <paramref name="body"/> always transfers
    /// control unconditionally (raise, return), so no further IL is needed after it.
    /// Used to avoid emitting dead code (Leave/store) after Throw/Rethrow in catch handlers.
    /// </summary>
    public static bool EndsWithUnconditionalTransfer(IReadOnlyList<Statement> body)
    {
        if (body.Count == 0) return false;
        return body[^1] is RaiseStatement or ReturnStatement;
    }

    /// <summary>
    /// Returns true if <paramref name="statements"/> contains at least one <see cref="ReturnStatement"/>
    /// at any nesting depth (but not inside nested function definitions).
    /// Used to detect <c>return</c> inside <c>finally</c> blocks.
    /// </summary>
    public static bool ContainsReturnStatement(IReadOnlyList<Statement> statements)
    {
        foreach (var stmt in statements)
        {
            if (ContainsReturnInStatement(stmt))
                return true;
        }
        return false;
    }

    private static bool ContainsReturnInStatement(Statement stmt) => stmt switch
    {
        ReturnStatement => true,
        IfStatement ifs => ContainsReturnStatement(ifs.Then) ||
                           ifs.Elifs.Any(e => ContainsReturnStatement(e.Body)) ||
                           ContainsReturnStatement(ifs.Else),
        WhileStatement ws => ContainsReturnStatement(ws.Body),
        ForStatement fs => ContainsReturnStatement(fs.Body),
        TryStatement ts => ContainsReturnStatement(ts.Body) ||
                           ts.Handlers.Any(h => ContainsReturnStatement(h.Body)) ||
                           ContainsReturnStatement(ts.Finally),
        WithStatement ws => ContainsReturnStatement(ws.Body),
        _ => false  // FunctionDef intentionally excluded: nested function returns are independent
    };

    /// <summary>
    /// Check if a statement list contains any yield expressions.
    /// </summary>
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

    private static void CollectReferencedNamesInStmt(Statement stmt, HashSet<string> names)
    {
        switch (stmt)
        {
            case ExprStatement es:
                CollectNamesInExpr(es.Expr, names);
                break;
            case AssignStatement a:
                CollectNamesInExpr(a.Value, names);
                foreach (var t in a.Targets)
                    if (t is not NameExpr)
                        CollectNamesInExpr(t, names);
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
            case RaiseStatement rs:
                if (rs.Exception is not null) CollectNamesInExpr(rs.Exception, names);
                if (rs.Cause is not null) CollectNamesInExpr(rs.Cause, names);
                break;
            case AssertStatement ast:
                CollectNamesInExpr(ast.Test, names);
                if (ast.Message is not null) CollectNamesInExpr(ast.Message, names);
                break;
            case AugAssignStatement aas:
                CollectNamesInExpr(aas.Value, names);
                CollectNamesInExpr(aas.Target, names);
                break;
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

    public static HashSet<string> CollectNonlocalNames(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body) CollectNonlocalNamesInStmt(stmt, names);
        return names;
    }

    public static HashSet<string> CollectGlobalNames(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body)
            if (stmt is GlobalStatement gs)
                foreach (var n in gs.Names) names.Add(n);
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
            // Do NOT recurse into nested FunctionDef — nonlocal scope is per-function
        }
    }
}

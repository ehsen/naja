using Naja.Parser;
using Naja.Semantics;
using NajaModule = Naja.Parser.Module;

namespace Naja.Inference;

// ═══════════════════════════════════════════════════════════════════════════════
// DynamicSiteAnalyser
//
// Runs AFTER TypeInferenceEngine.Infer() and BEFORE any IL emission.
//
// Walks every expression in the module and, for each one whose inferred type is
// UnknownType, calls DiagnosticSink.ReportDynamicSite().
//
// In Strict mode  → every Unknown becomes an Error → ThrowIfErrors() aborts emission
// In Balanced mode → every Unknown becomes a Warning → emission continues
// In Permissive   → no-op
//
// This separation keeps the inference engine pure (no sink dependency) and
// keeps the emitters clean (they just check IsUnknown, not mode logic).
//
// Special cases handled
// ─────────────────────
//  • dynamic(expr) calls   → ReportExplicitDynamic instead of ReportDynamicSite
//                            They are always allowed; the error is suppressed.
//  • cast(T, expr) calls   → ReportCast (informational only)
//  • Expressions inside    → dynamic() nodes suppress their children too.
//    a dynamic() subtree
// ═══════════════════════════════════════════════════════════════════════════════

public sealed class DynamicSiteAnalyser
{
    private readonly InferenceResult _inference;
    private readonly DiagnosticSink  _sink;

    public DynamicSiteAnalyser(InferenceResult inference, DiagnosticSink sink)
    {
        _inference = inference;
        _sink      = sink;
    }

    /// <summary>
    /// Analyse all expressions in the module and report dynamic sites.
    /// Call <see cref="DiagnosticSink.ThrowIfErrors"/> after this to abort
    /// in strict mode.
    /// </summary>
    public void Analyse(NajaModule module)
    {
        if (_sink.Mode == CompilationMode.Permissive) return;  // fast-path

        foreach (var stmt in module.Body)
            AnalyseStatement(stmt);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Statement walk
    // ═══════════════════════════════════════════════════════════════════════════════

    private void AnalyseStatement(Statement stmt)
    {
        switch (stmt)
        {
            case AssignStatement s:
                AnalyseExpr(s.Value);
                break;

            case AnnAssignStatement s:
                if (s.Value is not null) AnalyseExpr(s.Value);
                break;

            case AugAssignStatement s:
                AnalyseExpr(s.Value);
                break;

            case ReturnStatement s:
                if (s.Value is not null) AnalyseExpr(s.Value);
                break;

            case ExprStatement s:
                AnalyseExpr(s.Expr);
                break;

            case IfStatement s:
                AnalyseExpr(s.Condition);
                AnalyseStatements(s.Then);
                AnalyseStatements(s.Else);
                break;

            case WhileStatement s:
                AnalyseExpr(s.Condition);
                AnalyseStatements(s.Body);
                AnalyseStatements(s.Else);
                break;

            case ForStatement s:
                AnalyseExpr(s.Iter);
                AnalyseStatements(s.Body);
                AnalyseStatements(s.Else);
                break;

            case TryStatement s:
                AnalyseStatements(s.Body);
                foreach (var h in s.Handlers) AnalyseStatements(h.Body);
                AnalyseStatements(s.Else);
                AnalyseStatements(s.Finally);
                break;

            case WithStatement s:
                foreach (var item in s.Items)
                    AnalyseExpr(item.Context);
                AnalyseStatements(s.Body);
                break;

            case RaiseStatement s:
                if (s.Exception is not null) AnalyseExpr(s.Exception);
                break;

            case AssertStatement s:
                AnalyseExpr(s.Test);
                if (s.Message is not null) AnalyseExpr(s.Message);
                break;

            case FunctionDef s:
                AnalyseStatements(s.Body);
                break;

            case ClassDef s:
                AnalyseStatements(s.Body);
                break;

            case MatchStatement s:
                AnalyseExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    if (c.Guard is not null) AnalyseExpr(c.Guard);
                    AnalyseStatements(c.Body);
                }
                break;
        }
    }

    private void AnalyseStatements(IReadOnlyList<Statement> stmts)
    {
        foreach (var s in stmts) AnalyseStatement(s);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Expression walk
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyse one expression node. Reports to the sink if Unknown.
    /// Children are only analysed if the node itself is NOT a dynamic() call
    /// (dynamic() is the explicit opt-in — we don't recurse into it for errors).
    /// </summary>
    private void AnalyseExpr(Expression expr)
    {
        // ── Intercept dynamic() calls ─────────────────────────────────────
        // dynamic(expr) is always allowed — it's the explicit opt-in keyword.
        // We report an Info, suppress errors on the inner expression, and return.
        if (expr is CallExpr { Func: NameExpr { Name: "dynamic" }, Args: var dynArgs } && dynArgs.Count == 1)
        {
            _sink.ReportExplicitDynamic(DescribeExpr(dynArgs[0].Value), expr.Line, expr.Column);
            // Do NOT recurse into dynArgs[0] — it's intentionally dynamic
            return;
        }

        // ── Intercept cast() calls ────────────────────────────────────────
        // cast(T, expr) is a type assertion — always allowed, informational only.
        if (expr is CallExpr { Func: NameExpr { Name: "cast" }, Args: var castArgs } && castArgs.Count == 2)
        {
            _sink.ReportCast(DescribeExpr(castArgs[0].Value), DescribeExpr(castArgs[1].Value), expr.Line, expr.Column);
            // Recurse into the value argument to catch nested dynamic sites
            // that are NOT inside a dynamic() wrapper
            AnalyseExpr(castArgs[1].Value);
            return;
        }

        // ── Check this node's inferred type ──────────────────────────────
        var inferredType = _inference.GetType(expr);
        if (inferredType is UnknownType)
        {
            // Only report leaf/meaningful nodes — not every intermediate subexpression.
            // This keeps the error count actionable (one error per dynamic site,
            // not ten errors for the sub-expressions of one dynamic call chain).
            if (IsMeaningfulDynamicSite(expr))
            {
                _sink.ReportDynamicSite(
                    DescribeExpr(expr),
                    expr.Line,
                    expr.Column,
                    BuildHint(expr));
            }
        }

        // ── Recurse into children ─────────────────────────────────────────
        foreach (var child in ChildExpressions(expr))
            AnalyseExpr(child);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// True for expression kinds that represent a meaningful dynamic site
    /// worth reporting as a separate diagnostic rather than noise.
    /// </summary>
    private static bool IsMeaningfulDynamicSite(Expression expr) => expr switch
    {
        // A name with Unknown type — e.g. unannotated parameter
        NameExpr           => true,
        // A call whose return type is Unknown — most common case
        CallExpr           => true,
        // An attribute access on an Unknown object
        AttributeExpr      => true,
        // A subscript on an Unknown collection
        SubscriptExpr      => true,
        // A binary op where at least one operand is Unknown
        BinaryExpr         => true,

        // These are typically intermediate nodes whose Unknown-ness is
        // caused by one of the above — don't double-report.
        IntLiteral         => false,
        FloatLiteral       => false,
        StringLiteral      => false,
        BoolLiteral        => false,
        NoneLiteral        => false,
        FStringExpr        => false,
        UnaryExpr          => false,
        BoolOpExpr         => false,
        CompareExpr        => false,
        IfExpr             => false,
        WalrusExpr         => false,
        ListExpr           => false,
        DictExpr           => false,
        TupleExpr          => false,
        SetExpr            => false,
        _                  => false
    };

    private static string DescribeExpr(Expression expr) => expr switch
    {
        NameExpr      e  => e.Name,
        CallExpr      e  => $"call:{DescribeExpr(e.Func)}(...)",
        AttributeExpr e  => $"{DescribeExpr(e.Object)}.{e.Attribute}",
        SubscriptExpr e  => $"{DescribeExpr(e.Object)}[...]",
        BinaryExpr    e  => $"{DescribeExpr(e.Left)} {e.Op} {DescribeExpr(e.Right)}",
        IntLiteral    e  => e.Value.ToString(),
        FloatLiteral  e  => e.Value.ToString(),
        StringLiteral e  => $"'{e.Value}'",
        BoolLiteral   e  => e.Value.ToString(),
        NoneLiteral   _  => "None",
        _                => expr.GetType().Name
    };

    private static string? BuildHint(Expression expr)
    {
        return expr switch
        {
            // Unannotated parameter or variable
            NameExpr n => $"Add a type annotation: {n.Name}: int  (or the appropriate type)",

            // Call whose return type is unknown
            CallExpr { Func: NameExpr { Name: var fn } } =>
                $"Add a return type annotation to '{fn}': def {fn}(...) -> int:",

            // Method call on unknown object
            CallExpr { Func: AttributeExpr { Object: var obj, Attribute: var meth } } =>
                $"The type of '{DescribeExpr(obj)}' is unknown. " +
                $"Annotate it, or use: cast(int, {DescribeExpr(obj)}.{meth}(...))",

            // Attribute access
            AttributeExpr { Object: var obj, Attribute: var attr } =>
                $"The type of '{DescribeExpr(obj)}' is unknown. " +
                $"Annotate '{DescribeExpr(obj)}' to resolve '.{attr}'",

            // Binary op with unknown operand
            BinaryExpr { Left: var l, Right: var r } =>
                $"Operand types unknown. " +
                $"Annotate the variables in '{DescribeExpr(l)}' and '{DescribeExpr(r)}'",

            _ => null
        };
    }

    /// <summary>
    /// Returns the direct child expressions of a node for recursive analysis.
    /// We do NOT use a general AST visitor here to avoid a dependency on a
    /// visitor framework — we enumerate children explicitly for the node types
    /// that matter for dynamic-site analysis.
    /// </summary>
    private static IEnumerable<Expression> ChildExpressions(Expression expr)
    {
        switch (expr)
        {
            case CallExpr e:
                yield return e.Func;
                foreach (var a in e.Args) yield return a.Value;
                break;

            case AttributeExpr e:
                yield return e.Object;
                break;

            case SubscriptExpr e:
                yield return e.Object;
                yield return e.Index;
                break;

            case BinaryExpr e:
                yield return e.Left;
                yield return e.Right;
                break;

            case UnaryExpr e:
                yield return e.Operand;
                break;

            case BoolOpExpr e:
                foreach (var v in e.Values) yield return v;
                break;

            case CompareExpr e:
                yield return e.Left;
                foreach (var (_, r) in e.Comparators) yield return r;
                break;

            case IfExpr e:
                yield return e.Condition;
                yield return e.Then;
                yield return e.Else;
                break;

            case WalrusExpr e:
                yield return e.Value;
                break;

            case ListExpr e:
                foreach (var el in e.Elements) yield return el;
                break;

            case TupleExpr e:
                foreach (var el in e.Elements) yield return el;
                break;

            case SetExpr e:
                foreach (var el in e.Elements) yield return el;
                break;

            case DictExpr e:
                foreach (var (k, v) in e.Pairs)
                {
                    if (k is not null) yield return k;
                    yield return v;
                }
                break;

            case ListCompExpr e:
                yield return e.Element;
                foreach (var g in e.Generators)
                {
                    yield return g.Iter;
                    foreach (var c in g.Conditions) yield return c;
                }
                break;

            case StarredExpr e:
                yield return e.Value;
                break;

            // Literals, NameExpr — no children to recurse into
        }
    }
}

using Naja.Parser;
using Naja.Semantics;
using NajaModule = Naja.Parser.Module;

namespace Naja.Inference;

// ═══════════════════════════════════════════════════════════════════════════════
// Naja Type Inference Engine
//
// Architecture
// ────────────
// Sits between SemanticAnalysis and AssemblyEmitter in the pipeline:
//
//   .py → Parser → AST → SemanticAnalysis → [TypeInferenceEngine] → AssemblyEmitter
//
// The engine walks the AST BEFORE any IL is emitted and annotates every
// expression node with the most precise NajaType it can prove statically.
// The emitters then call ctx.Inference.GetType(node) instead of falling
// back to NajaTypes.Unknown, which eliminates DynamicCall/box/unbox on
// every expression that can be resolved.
//
// Key properties
// ──────────────
//  • Purely additive — zero changes to existing emitters required to use it.
//    The emitters already check "is this UnknownType?" before going dynamic;
//    inference simply makes more nodes come back as something other than Unknown.
//
//  • Conservative — when inference cannot prove a type it returns Unknown,
//    which causes the emitter to take the existing dynamic path. No regression.
//
//  • Iterative — loops and branches are analysed via a fixed-point iteration
//    so loop-counter types and branch-merged variables stabilise correctly.
//
//  • Interprocedural — within a single module, return types of functions that
//    are fully inferred are propagated to their call sites.
//
// Usage
// ─────
//   var engine  = new TypeInferenceEngine(semanticModel);
//   var result  = engine.Infer(module);          // analyse entire module
//
//   // In EmitContext constructor, attach result:
//   ctx.Inference = result;
//
//   // In ExpressionEmitter.EmitName / EmitBinary etc:
//   var knownType = _ctx.Inference?.GetType(expr) ?? NajaTypes.Unknown;
//
// AOT gate
// ────────
//   result.IsFullyStatic(functionDef)   → true iff zero Unknown nodes inside
//   result.DynamicSites                 → list of nodes that fell back to Unknown
//                                         (for warnings / AOT rejection)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Immutable snapshot of inference results for one module.
/// Produced by <see cref="TypeInferenceEngine.Infer"/>.
/// </summary>
public sealed class InferenceResult
{
    // Node → inferred NajaType  (keyed by reference identity)
    private readonly Dictionary<Expression, NajaType> _types;

    // Function name → inferred return type  (for call-site propagation)
    public readonly Dictionary<string, NajaType> FunctionReturnTypes;

    // Variable name → inferred NajaType per function scope
    // Key: (function name or "" for module scope, variable name)
    public readonly Dictionary<(string Scope, string Name), NajaType> VariableTypes;

    public InferenceResult(
        Dictionary<Expression, NajaType> types,
        Dictionary<string, NajaType> functionReturnTypes,
        Dictionary<(string, string), NajaType> variableTypes)
    {
        _types             = types;
        FunctionReturnTypes = functionReturnTypes;
        VariableTypes       = variableTypes;
    }

    /// <summary>
    /// Best-known NajaType for an expression node.
    /// Returns <see cref="NajaTypes.Unknown"/> when inference could not determine
    /// a concrete type — the emitter must take its normal dynamic path.
    /// </summary>
    public NajaType GetType(Expression node) =>
        _types.TryGetValue(node, out var t) ? t : NajaTypes.Unknown;

    /// <summary>
    /// True when every expression inside <paramref name="fn"/> resolved to a
    /// concrete (non-Unknown) type.  Used as the AOT-safe gate.
    /// </summary>
    public bool IsFullyStatic(FunctionDef fn)
    {
        foreach (var node in CollectExpressions(fn.Body))
            if (_types.TryGetValue(node, out var t) && t is UnknownType)
                return false;
        return true;
    }

    /// <summary>All expression nodes that fell back to Unknown (dynamic sites).</summary>
    public IReadOnlyList<Expression> DynamicSites =>
        _types.Where(kv => kv.Value is UnknownType)
              .Select(kv => kv.Key)
              .ToList();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<Expression> CollectExpressions(IReadOnlyList<Statement> body)
    {
        foreach (var stmt in body)
        foreach (var expr in WalkExpressions(stmt))
            yield return expr;
    }

    private static IEnumerable<Expression> WalkExpressions(Statement stmt) => stmt switch
    {
        ExprStatement s      => WalkExpr(s.Expr),
        AssignStatement s    => WalkExpr(s.Value),
        AugAssignStatement s => WalkExpr(s.Value),
        AnnAssignStatement s => s.Value is null ? [] : WalkExpr(s.Value),
        ReturnStatement s    => s.Value is null ? [] : WalkExpr(s.Value),
        RaiseStatement s     => s.Exception is null ? [] : WalkExpr(s.Exception),
        AssertStatement s    => s.Message is null
                                    ? WalkExpr(s.Test)
                                    : WalkExpr(s.Test).Concat(WalkExpr(s.Message)),
        IfStatement s        => WalkExpr(s.Condition)
                                    .Concat(s.Then.SelectMany(WalkExpressions))
                                    .Concat(s.Else.SelectMany(WalkExpressions)),
        WhileStatement s     => WalkExpr(s.Condition)
                                    .Concat(s.Body.SelectMany(WalkExpressions))
                                    .Concat(s.Else.SelectMany(WalkExpressions)),
        ForStatement s       => WalkExpr(s.Iter)
                                    .Concat(s.Body.SelectMany(WalkExpressions))
                                    .Concat(s.Else.SelectMany(WalkExpressions)),
        FunctionDef s        => s.Body.SelectMany(WalkExpressions),
        ClassDef s           => s.Body.SelectMany(WalkExpressions),
        _                    => []
    };

    private static IEnumerable<Expression> WalkExpr(Expression expr)
    {
        yield return expr;
        switch (expr)
        {
            case CallExpr e:
                foreach (var s in WalkExpr(e.Func)) yield return s;
                foreach (var a in e.Args) foreach (var s in WalkExpr(a.Value)) yield return s;
                break;
            case AttributeExpr e:
                foreach (var s in WalkExpr(e.Object)) yield return s;
                break;
            case SubscriptExpr e:
                foreach (var s in WalkExpr(e.Object)) yield return s;
                foreach (var s in WalkExpr(e.Index)) yield return s;
                break;
            case BinaryExpr e:
                foreach (var s in WalkExpr(e.Left)) yield return s;
                foreach (var s in WalkExpr(e.Right)) yield return s;
                break;
            case UnaryExpr e:
                foreach (var s in WalkExpr(e.Operand)) yield return s;
                break;
            case BoolOpExpr e:
                foreach (var v in e.Values) foreach (var s in WalkExpr(v)) yield return s;
                break;
            case CompareExpr e:
                foreach (var s in WalkExpr(e.Left)) yield return s;
                foreach (var (_, r) in e.Comparators) foreach (var s in WalkExpr(r)) yield return s;
                break;
            case IfExpr e:
                foreach (var s in WalkExpr(e.Condition)) yield return s;
                foreach (var s in WalkExpr(e.Then)) yield return s;
                foreach (var s in WalkExpr(e.Else)) yield return s;
                break;
            case WalrusExpr e:
                foreach (var s in WalkExpr(e.Value)) yield return s;
                break;
            case ListExpr e:
                foreach (var el in e.Elements) foreach (var s in WalkExpr(el)) yield return s;
                break;
            case TupleExpr e:
                foreach (var el in e.Elements) foreach (var s in WalkExpr(el)) yield return s;
                break;
            case SetExpr e:
                foreach (var el in e.Elements) foreach (var s in WalkExpr(el)) yield return s;
                break;
            case DictExpr e:
                foreach (var (k, v) in e.Pairs)
                {
                    if (k is not null) foreach (var s in WalkExpr(k)) yield return s;
                    foreach (var s in WalkExpr(v)) yield return s;
                }
                break;
            case StarredExpr e:
                foreach (var s in WalkExpr(e.Value)) yield return s;
                break;
            case ListCompExpr e:
                foreach (var s in WalkExpr(e.Element)) yield return s;
                foreach (var g in e.Generators)
                {
                    foreach (var s in WalkExpr(g.Iter)) yield return s;
                    foreach (var c in g.Conditions) foreach (var s in WalkExpr(c)) yield return s;
                }
                break;
        }
    }
}

/// <summary>
/// Analyses a parsed module and produces an <see cref="InferenceResult"/>.
/// </summary>
public sealed class TypeInferenceEngine
{
    private readonly SemanticModel _model;

    // Accumulated output
    private readonly Dictionary<Expression, NajaType> _types   = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, NajaType>     _fnRet   = new();
    private readonly Dictionary<(string, string), NajaType> _vars = new();

    // Per-scope variable tables — rebuilt on each scope entry
    // Stack of (scopeName, varTable)
    private readonly Stack<(string Name, Dictionary<string, NajaType> Vars)> _scopes = new();

    // Function stubs: name → declared return type (from annotation or prior pass)
    private readonly Dictionary<string, NajaType> _fnStubs = new();

    public TypeInferenceEngine(SemanticModel model)
    {
        _model = model;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════════════════════════════════════════

    public InferenceResult Infer(NajaModule module)
    {
        // ── Pass 0: collect function stubs so forward calls can be typed ──
        CollectFunctionStubs(module.Body);

        // ── Pass 1: module scope ──────────────────────────────────────────
        PushScope("");
        InferStatements(module.Body, "");
        var moduleVars = CurrentVars();
        PopScope();

        // ── Pass 2: function bodies (now that all stubs are known) ────────
        foreach (var stmt in module.Body)
        {
            if (stmt is FunctionDef fn)
                InferFunction(fn, moduleVars);
            else if (stmt is ClassDef cls)
                InferClass(cls, moduleVars);
        }

        return new InferenceResult(_types, _fnRet, _vars);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Stub collection (Pass 0)
    // ═══════════════════════════════════════════════════════════════════════════════

    private void CollectFunctionStubs(IReadOnlyList<Statement> stmts)
    {
        foreach (var stmt in stmts)
        {
            if (stmt is FunctionDef fn)
            {
                if (fn.ReturnAnnotation is not null)
                {
                    var retType = AnnotationToNajaType(fn.ReturnAnnotation);
                    _fnStubs[fn.Name] = retType;
                    _fnRet[fn.Name] = retType;
                }
                else
                {
                    _fnStubs[fn.Name] = NajaTypes.Unknown;
                    // _fnRet intentionally not seeded
                }
            }
            else if (stmt is ClassDef cls)
            {
                foreach (var s in cls.Body)
                {
                    if (s is not FunctionDef method) continue;
                    string key = $"{cls.Name}.{method.Name}";
                    if (method.ReturnAnnotation is not null)
                    {
                        var retType = AnnotationToNajaType(method.ReturnAnnotation);
                        _fnStubs[key] = retType;
                        _fnRet[key] = retType;
                    }
                    else
                    {
                        _fnStubs[key] = NajaTypes.Unknown;
                        // _fnRet intentionally not seeded
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Statement inference
    // ═══════════════════════════════════════════════════════════════════════════════

    private void InferStatements(IReadOnlyList<Statement> stmts, string scopeName)
    {
        foreach (var stmt in stmts)
            InferStatement(stmt, scopeName);
    }

    private void InferStatement(Statement stmt, string scopeName)
    {
        switch (stmt)
        {
            case AssignStatement s:
                InferAssign(s, scopeName);
                break;

            case AnnAssignStatement s:
                InferAnnAssign(s, scopeName);
                break;

            case AugAssignStatement s:
                InferAugAssign(s, scopeName);
                break;

            case ReturnStatement s:
                if (s.Value is not null)
                {
                    var t = InferExpr(s.Value, scopeName);
                    // Update the function's inferred return type
                    if (!string.IsNullOrEmpty(scopeName))
                        _fnRet[scopeName] = Unify(_fnRet.GetValueOrDefault(scopeName, NajaTypes.Unknown), t);
                }
                break;

            case IfStatement s:
                InferIf(s, scopeName);
                break;

            case WhileStatement s:
                InferWhile(s, scopeName);
                break;

            case ForStatement s:
                InferFor(s, scopeName);
                break;

            case TryStatement s:
                InferTry(s, scopeName);
                break;

            case WithStatement s:
                foreach (var item in s.Items)
                    InferExpr(item.Context, scopeName);
                InferStatements(s.Body, scopeName);
                break;

            case ExprStatement s:
                InferExpr(s.Expr, scopeName);
                break;

            case AssertStatement s:
                InferExpr(s.Test, scopeName);
                if (s.Message is not null) InferExpr(s.Message, scopeName);
                break;

            case RaiseStatement s:
                if (s.Exception is not null) InferExpr(s.Exception, scopeName);
                break;

            case MatchStatement s:
                InferMatch(s, scopeName);
                break;

            // These don't produce typed expressions
            case FunctionDef:
            case ClassDef:
            case PassStatement:
            case BreakStatement:
            case ContinueStatement:
            case ImportStatement:
            case FromImportStatement:
            case GlobalStatement:
            case NonlocalStatement:
            case DeleteStatement:
            case TypeAliasStatement:
                break;
        }
    }

    // ── Assignment ────────────────────────────────────────────────────────

    private void InferAssign(AssignStatement s, string scope)
    {
        var valType = InferExpr(s.Value, scope);
        foreach (var target in s.Targets)
            BindTarget(target, valType, scope);
    }

    private void InferAnnAssign(AnnAssignStatement s, string scope)
    {
        // Annotation takes priority; fall back to RHS inference
        var annotated = AnnotationToNajaType(s.Annotation);
        if (s.Value is not null)
        {
            var rhs = InferExpr(s.Value, scope);
            // If annotation says int but literal says int, use int.
            // If annotation is Unknown (no recognised annotation), use rhs.
            var resolved = annotated is UnknownType ? rhs : annotated;
            BindTarget(s.Target, resolved, scope);
        }
        else if (annotated is not UnknownType)
        {
            BindTarget(s.Target, annotated, scope);
        }
    }

    private void InferAugAssign(AugAssignStatement s, string scope)
    {
        var targetType = LookupVar(s.Target is NameExpr n ? n.Name : "", scope);
        var valueType  = InferExpr(s.Value, scope);
        var result     = InferBinaryOp(s.Op, targetType, valueType);
        if (s.Target is NameExpr na)
            SetVar(na.Name, result, scope);
    }

    // ── Control flow ──────────────────────────────────────────────────────

    private void InferIf(IfStatement s, string scope)
    {
        InferExpr(s.Condition, scope);

        // Snapshot vars before both branches
        var before = SnapshotVars();

        InferStatements(s.Then, scope);
        var afterTrue = SnapshotVars();

        RestoreVars(before);
        if (s.Else.Count > 0)
            InferStatements(s.Else, scope);
        var afterFalse = SnapshotVars();

        // Merge: variables that differ between branches are unified
        MergeBranches(afterTrue, afterFalse, scope);
    }

    private void InferWhile(WhileStatement s, string scope)
    {
        InferExpr(s.Condition, scope);

        // Fixed-point: iterate body until variable types stabilise
        // (handles cases like x = 0; while ..: x = x + item)
        const int maxIter = 8;
        for (int i = 0; i < maxIter; i++)
        {
            var before = SnapshotVars();
            InferStatements(s.Body, scope);
            if (s.Else.Count > 0) InferStatements(s.Else, scope);
            if (VarsEqual(before)) break;   // stable — done
        }
    }

    private void InferFor(ForStatement s, string scope)
    {
        var iterType = InferExpr(s.Iter, scope);
        var elemType = ElementTypeOf(iterType);

        // Bind loop variable(s)
        BindTarget(s.Target, elemType, scope);

        // Fixed-point for the body
        const int maxIter = 8;
        for (int i = 0; i < maxIter; i++)
        {
            var before = SnapshotVars();
            InferStatements(s.Body, scope);
            if (s.Else.Count > 0) InferStatements(s.Else, scope);
            if (VarsEqual(before)) break;
        }
    }

    private void InferTry(TryStatement s, string scope)
    {
        InferStatements(s.Body, scope);
        foreach (var handler in s.Handlers)
        {
            if (handler.Name is not null)
                SetVar(handler.Name, NajaTypes.Unknown, scope); // exception object = Unknown
            InferStatements(handler.Body, scope);
        }
        if (s.Else.Count  > 0) InferStatements(s.Else,   scope);
        if (s.Finally.Count > 0) InferStatements(s.Finally, scope);
    }

    private void InferMatch(MatchStatement s, string scope)
    {
        InferExpr(s.Subject, scope);
        foreach (var c in s.Cases)
        {
            if (c.Guard is not null) InferExpr(c.Guard, scope);
            BindPattern(c.Pattern, scope);
            InferStatements(c.Body, scope);
        }
    }

    private void BindPattern(Pattern pattern, string scope)
    {
        switch (pattern)
        {
            case CapturePattern cp:
                SetVar(cp.Name, NajaTypes.Unknown, scope);
                break;
            case AsPattern ap:
                BindPattern(ap.Inner, scope);
                SetVar(ap.Name, NajaTypes.Unknown, scope);
                break;
            case OrPattern op:
                foreach (var p in op.Patterns) BindPattern(p, scope);
                break;
            case SequencePattern sp:
                foreach (var p in sp.Patterns) BindPattern(p, scope);
                break;
            case MappingPattern mp:
                foreach (var (_, vp) in mp.Pairs) BindPattern(vp, scope);
                if (mp.Rest is not null) SetVar(mp.Rest, NajaTypes.Unknown, scope);
                break;
        }
    }

    // ── Function / class bodies ───────────────────────────────────────────

    private void InferFunction(
    FunctionDef fn,
    Dictionary<string, NajaType> moduleVars,
    string? qualifiedName = null)  // e.g. "Customer.__init__"
    {
        // Use qualified name for all dictionary keys
        // Fall back to fn.Name for module-level functions
        var key = qualifiedName ?? fn.Name;

        PushScope(key);

        // Seed parameters from annotations
        for (int i = 0; i < fn.Params.Count; i++)
        {
            var p = fn.Params[i];
            var pType = p.Annotation is not null
                ? AnnotationToNajaType(p.Annotation)
                : NajaTypes.Unknown;
            SetVar(p.Name, pType, key);
        }

        // Bring module-level variables into scope
        foreach (var (name, type) in moduleVars)
            if (!CurrentVars().ContainsKey(name))
                SetVar(name, type, key);

        InferStatements(fn.Body, key);

        // Finalise return type
        if (!_fnRet.ContainsKey(key))
            _fnRet[key] = NajaTypes.None;

        // Persist variable types for this scope
        foreach (var (name, type) in CurrentVars())
            _vars[(key, name)] = type;

        PopScope();
    }

    private void InferClass(ClassDef cls, Dictionary<string, NajaType> moduleVars)
    {
        foreach (var stmt in cls.Body)
        {
            if (stmt is FunctionDef method)
            {
                string qualifiedName = $"{cls.Name}.{method.Name}";

                InferFunction(method, moduleVars, qualifiedName);

                if (method.Name == "__init__")
                    ExtractInstanceFields(method, cls.Name);
            }
        }
    }

    private void ExtractInstanceFields(FunctionDef init, string className)
    {
        foreach (var stmt in init.Body)
        {
            if (stmt is AssignStatement assign)
            {
                foreach (var target in assign.Targets)
                {
                    if (target is AttributeExpr { Object: NameExpr { Name: "self" }, Attribute: var field })
                    {
                        var t = _types.TryGetValue(assign.Value, out var vt) ? vt : NajaTypes.Unknown;
                        _vars[(className, field)] = t;
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Expression inference
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Infer the type of <paramref name="expr"/>, record it, and return it.
    /// </summary>
    public NajaType InferExpr(Expression expr, string scope)
    {
        var type = ComputeExprType(expr, scope);
        _types[expr] = type;
        return type;
    }

    private NajaType ComputeExprType(Expression expr, string scope) => expr switch
    {
        IntLiteral     => NajaTypes.Int,
        FloatLiteral   => NajaTypes.Float,
        BoolLiteral    => NajaTypes.Bool,
        StringLiteral  => NajaTypes.Str,
        FStringExpr    => NajaTypes.Str,          // f-strings always produce str
        NoneLiteral    => NajaTypes.None,
        EllipsisLiteral => NajaTypes.Unknown,

        NameExpr       e => InferName(e, scope),
        BinaryExpr     e => InferBinary(e, scope),
        UnaryExpr      e => InferUnary(e, scope),
        BoolOpExpr     e => InferBoolOp(e, scope),
        CompareExpr    e => InferCompare(e, scope),
        IfExpr         e => InferIfExpr(e, scope),
        WalrusExpr     e => InferWalrus(e, scope),
        CallExpr       e => InferCall(e, scope),
        AttributeExpr  e => InferAttribute(e, scope),
        SubscriptExpr  e => InferSubscript(e, scope),
        SliceExpr      _ => NajaTypes.Unknown,    // slice object — not a scalar
        LambdaExpr     _ => new FunctionType([], NajaTypes.Unknown),
        ListExpr       e => InferListExpr(e, scope),
        TupleExpr      e => InferTupleExpr(e, scope),
        SetExpr        e => InferSetExpr(e, scope),
        DictExpr       e => InferDictExpr(e, scope),
        ListCompExpr   e => InferListComp(e, scope),
        SetCompExpr    _ => new SetType(NajaTypes.Unknown),
        DictCompExpr   _ => new DictType(NajaTypes.Unknown, NajaTypes.Unknown),
        GeneratorExpr  e => InferListComp(e.Element, e.Generators, scope),
        StarredExpr    e => InferExpr(e.Value, scope),
        YieldExpr      _ => NajaTypes.Unknown,
        AwaitExpr      _ => NajaTypes.Unknown,
        _              => NajaTypes.Unknown
    };

    // ── Name ──────────────────────────────────────────────────────────────

    private NajaType InferName(NameExpr e, string scope)
    {
        // 1. Current scope locals / params
        var t = LookupVar(e.Name, scope);
        if (t is not UnknownType) return t;

        // 2. Semantic model symbol table
        var sym = _model.GetSymbol(e);
        if (sym is not null && sym.Type is not UnknownType) return sym.Type;

        // 3. Built-in constants
        return e.Name switch
        {
            "True" or "False" => NajaTypes.Bool,
            "None"            => NajaTypes.None,
            "__name__"        => NajaTypes.Str,
            "__file__"        => NajaTypes.Str,
            _                 => NajaTypes.Unknown
        };
    }

    // ── Binary ────────────────────────────────────────────────────────────

    private NajaType InferBinary(BinaryExpr e, string scope)
    {
        var l = InferExpr(e.Left,  scope);
        var r = InferExpr(e.Right, scope);
        return InferBinaryOp(e.Op, l, r);
    }

    private static NajaType InferBinaryOp(BinaryOp op, NajaType l, NajaType r) => op switch
    {
        // Arithmetic
        BinaryOp.Add =>
            (l is StrType  && r is StrType)  ? NajaTypes.Str   :
            (l is FloatType || r is FloatType) ? NajaTypes.Float :
            (l is IntType  && r is IntType)  ? NajaTypes.Int   :
            (l is BoolType && r is IntType) ? NajaTypes.Int:
            (l is BoolType && r is BoolType) ? NajaTypes.Int   :   // True+True = 2
            NajaTypes.Unknown,

        BinaryOp.Sub or BinaryOp.Mul =>
            (l is FloatType || r is FloatType) ? NajaTypes.Float :
            (l is IntType or BoolType) && (r is IntType or BoolType) ? NajaTypes.Int :
            NajaTypes.Unknown,

        BinaryOp.Div        => NajaTypes.Float,   // Python / always returns float
        BinaryOp.FloorDiv   =>
            (l is FloatType || r is FloatType) ? NajaTypes.Float : NajaTypes.Int,
        BinaryOp.Mod        =>
            (l is FloatType || r is FloatType) ? NajaTypes.Float :
            (l is IntType or BoolType) && (r is IntType or BoolType) ? NajaTypes.Int :
            NajaTypes.Unknown,
        BinaryOp.Pow        => NajaTypes.Float,   // ** always returns float in Python

        // Bitwise — only valid on ints
        BinaryOp.BitAnd or BinaryOp.BitOr or BinaryOp.BitXor
            or BinaryOp.LShift or BinaryOp.RShift =>
            (l is IntType or BoolType) && (r is IntType or BoolType)
                ? NajaTypes.Int : NajaTypes.Unknown,

        BinaryOp.MatMul     => NajaTypes.Unknown, // @ operator — user-defined

        _ => NajaTypes.Unknown
    };

    // ── Unary ─────────────────────────────────────────────────────────────

    private NajaType InferUnary(UnaryExpr e, string scope)
    {
        var operand = InferExpr(e.Operand, scope);
        return e.Op switch
        {
            UnaryOp.Not    => NajaTypes.Bool,
            UnaryOp.Neg    => operand is FloatType ? NajaTypes.Float :
                              operand is IntType or BoolType ? NajaTypes.Int :
                              NajaTypes.Unknown,
            UnaryOp.Pos    => operand,
            UnaryOp.Invert => NajaTypes.Int,
            _              => NajaTypes.Unknown
        };
    }

    // ── Bool op ───────────────────────────────────────────────────────────

    private NajaType InferBoolOp(BoolOpExpr e, string scope)
    {
        // `and`/`or` returns one of its operands — unify all
        NajaType result = NajaTypes.Unknown;
        foreach (var v in e.Values)
        {
            var t = InferExpr(v, scope);
            result = Unify(result, t);
        }
        return result;
    }

    // ── Comparison ────────────────────────────────────────────────────────

    private NajaType InferCompare(CompareExpr e, string scope)
    {
        InferExpr(e.Left, scope);
        foreach (var (_, right) in e.Comparators)
            InferExpr(right, scope);
        return NajaTypes.Bool;
    }

    // ── Ternary / walrus ──────────────────────────────────────────────────

    private NajaType InferIfExpr(IfExpr e, string scope)
    {
        InferExpr(e.Condition, scope);
        var t = InferExpr(e.Then,   scope);
        var f = InferExpr(e.Else, scope);
        return Unify(t, f);
    }

    private NajaType InferWalrus(WalrusExpr e, string scope)
    {
        var t = InferExpr(e.Value, scope);
        SetVar(e.Target, t, scope);
        return t;
    }

    // ── Call ──────────────────────────────────────────────────────────────

    private NajaType InferCall(CallExpr e, string scope)
    {
        // Infer all arguments first (side-effects on variable table)
        foreach (var arg in e.Args)   InferExpr(arg.Value, scope);

        // ── Builtin functions with known return types ─────────────────────
        if (e.Func is NameExpr { Name: var name })
        {
            var builtinRet = BuiltinReturnType(name, e.Args, scope);
            if (builtinRet is not null) return builtinRet;

            // User-defined function in this module?
            if (_fnRet.TryGetValue(name, out var fnRet))
                return fnRet;
        }

        // ── Method calls: obj.method(args) ───────────────────────────────
        if (e.Func is AttributeExpr { Object: var obj, Attribute: var method })
        {
            var objType = InferExpr(obj, scope);
            return InferMethodCall(objType, method, e.Args, scope);
        }

        return NajaTypes.Unknown;
    }

    private NajaType? BuiltinReturnType(string name, IReadOnlyList<Argument> args, string scope)
        => name switch
        {
            "len"      => NajaTypes.Int,
            "range"    => new ListType(NajaTypes.Int),   // treated as sequence of int
            "int"      => NajaTypes.Int,
            "float"    => NajaTypes.Float,
            "str"      => NajaTypes.Str,
            "bool"     => NajaTypes.Bool,
            "abs"      => args.Count > 0 ? InferExpr(args[0].Value, scope) : NajaTypes.Unknown,
            "round"    => NajaTypes.Int,   // round() → int (no ndigits) or float
            "min" or "max" =>
                args.Count > 0 ? InferExpr(args[0].Value, scope) : NajaTypes.Unknown,
            "sum"      => NajaTypes.Int,   // conservative — could be float
            "pow"      => NajaTypes.Float,
            "divmod"   => new TupleType([NajaTypes.Int, NajaTypes.Int]),
            "sorted"   => new ListType(NajaTypes.Unknown),
            "reversed" => new ListType(NajaTypes.Unknown),
            "enumerate"=> new ListType(new TupleType([NajaTypes.Int, NajaTypes.Unknown])),
            "zip"      => new ListType(new TupleType([])),
            "map"      => new ListType(NajaTypes.Unknown),
            "filter"   => new ListType(NajaTypes.Unknown),
            "list"     => new ListType(NajaTypes.Unknown),
            "dict"     => new DictType(NajaTypes.Unknown, NajaTypes.Unknown),
            "set"      => new SetType(NajaTypes.Unknown),
            "tuple"    => new TupleType([]),
            "chr"      => NajaTypes.Str,
            "ord"      => NajaTypes.Int,
            "hex" or "bin" or "oct" => NajaTypes.Str,
            "repr"     => NajaTypes.Str,
            "format"   => NajaTypes.Str,
            "id"       => NajaTypes.Int,
            "hash"     => NajaTypes.Int,
            "isinstance" => NajaTypes.Bool,
            "hasattr"  => NajaTypes.Bool,
            "callable" => NajaTypes.Bool,
            "any" or "all" => NajaTypes.Bool,
            "print"    => NajaTypes.None,
            "input"    => NajaTypes.Str,
            "open"     => NajaTypes.Unknown,   // file object
            "type"     => NajaTypes.Unknown,   // returns Type — Unknown in our model
            "getattr"  => NajaTypes.Unknown,
            "setattr"  => NajaTypes.None,
            "vars"     => new DictType(NajaTypes.Str, NajaTypes.Unknown),
            "dir"      => new ListType(NajaTypes.Str),
            _          => null
        };

    private NajaType InferMethodCall(NajaType objType, string method,
        IReadOnlyList<Argument> args, string scope)
    {
        // ── str methods ────────────────────────────────────────────────────
        if (objType is StrType)
        {
            return method switch
            {
                "upper" or "lower" or "strip" or "lstrip" or "rstrip"
                    or "replace" or "join" or "title" or "center"
                    or "ljust" or "rjust" or "zfill" or "encode"   => NajaTypes.Str,
                "split" or "splitlines"                             => new ListType(NajaTypes.Str),
                "startswith" or "endswith" or "isdigit" or "isalpha"
                    or "isalnum" or "isupper" or "islower"          => NajaTypes.Bool,
                "find" or "index" or "rfind" or "count"            => NajaTypes.Int,
                "format"                                            => NajaTypes.Str,
                _                                                   => NajaTypes.Unknown
            };
        }

        // ── list methods ───────────────────────────────────────────────────
        if (objType is ListType lt)
        {
            return method switch
            {
                "append" or "extend" or "insert"
                    or "remove" or "reverse" or "sort" or "clear" => NajaTypes.None,
                "pop"   => lt.ElementType,
                "index" or "count"                                => NajaTypes.Int,
                "copy"  => objType,
                _       => NajaTypes.Unknown
            };
        }

        // ── dict methods ───────────────────────────────────────────────────
        if (objType is DictType dt)
        {
            return method switch
            {
                "keys"   => new ListType(dt.KeyType),
                "values" => new ListType(dt.ValueType),
                "items"  => new ListType(new TupleType([dt.KeyType, dt.ValueType])),
                "get"    => dt.ValueType,
                "pop"    => dt.ValueType,
                "update" or "clear" => NajaTypes.None,
                "copy"   => objType,
                _        => NajaTypes.Unknown
            };
        }

        // ── set methods ────────────────────────────────────────────────────
        if (objType is SetType)
        {
            return method switch
            {
                "add" or "remove" or "discard" or "clear"
                    or "update" or "intersection_update"           => NajaTypes.None,
                "pop"                                              => NajaTypes.Unknown,
                "union" or "intersection" or "difference"
                    or "symmetric_difference"                      => objType,
                "issubset" or "issuperset" or "isdisjoint"        => NajaTypes.Bool,
                _                                                  => NajaTypes.Unknown
            };
        }

        return NajaTypes.Unknown;
    }

    // ── Attribute ─────────────────────────────────────────────────────────

    private NajaType InferAttribute(AttributeExpr e, string scope)
    {
        var objType = InferExpr(e.Object, scope);

        // Known attribute types for built-in types
        if (objType is StrType && e.Attribute == "__len__") return NajaTypes.Int;

        // Instance field lookup for classes we've analysed
        if (_vars.TryGetValue(("", e.Attribute), out var ft)) return ft;

        return NajaTypes.Unknown;
    }

    // ── Subscript ─────────────────────────────────────────────────────────

    private NajaType InferSubscript(SubscriptExpr e, string scope)
    {
        var objType   = InferExpr(e.Object, scope);
        InferExpr(e.Index, scope);

        return objType switch
        {
            ListType  lt => lt.ElementType,
            TupleType tt => tt.ElementTypes.Count > 0 ? tt.ElementTypes[0] : NajaTypes.Unknown,
            DictType  dt => dt.ValueType,
            StrType      => NajaTypes.Str,    // str[i] → str
            _            => NajaTypes.Unknown
        };
    }

    // ── Collection literals ───────────────────────────────────────────────

    private NajaType InferListExpr(ListExpr e, string scope)
    {
        if (e.Elements.Count == 0)
            return new ListType(NajaTypes.Unknown);

        NajaType? elemType = null;
        bool mixed = false;
        foreach (var el in e.Elements)
        {
            var t = InferExpr(el, scope);
            if (mixed) continue;
            if (elemType is null) { elemType = t; continue; }
            var unified = Unify(elemType, t);
            if (unified is UnknownType) mixed = true;
            else elemType = unified;
        }
        return new ListType(mixed ? NajaTypes.Unknown : (elemType ?? NajaTypes.Unknown));
    }

    private NajaType InferTupleExpr(TupleExpr e, string scope)
    {
        var elems = e.Elements.Select(el => InferExpr(el, scope)).ToArray();
        return new TupleType(elems);
    }

    private NajaType InferSetExpr(SetExpr e, string scope)
    {
        NajaType elemType = NajaTypes.Unknown;
        foreach (var el in e.Elements)
        {
            var t = InferExpr(el, scope);
            elemType = elemType is UnknownType ? t : Unify(elemType, t);
        }
        return new SetType(elemType);
    }

    private NajaType InferDictExpr(DictExpr e, string scope)
    {
        NajaType keyType = NajaTypes.Unknown, valType = NajaTypes.Unknown;
        foreach (var (k, v) in e.Pairs)
        {
            if (k is not null) { var kt = InferExpr(k, scope); keyType = Unify(keyType, kt); }
            var vt = InferExpr(v, scope); valType = Unify(valType, vt);
        }
        return new DictType(keyType, valType);
    }

    private NajaType InferListComp(ListCompExpr e, string scope)
    {
        foreach (var gen in e.Generators)
        {
            var iterType = InferExpr(gen.Iter, scope);
            var elemType = ElementTypeOf(iterType);
            BindTarget(gen.Target, elemType, scope);
            foreach (var cond in gen.Conditions) InferExpr(cond, scope);
        }
        var elType = InferExpr(e.Element, scope);
        return new ListType(elType);
    }

    private NajaType InferListComp(Expression element,
        IReadOnlyList<Comprehension> generators, string scope)
    {
        foreach (var gen in generators)
        {
            var iterType = InferExpr(gen.Iter, scope);
            BindTarget(gen.Target, ElementTypeOf(iterType), scope);
            foreach (var cond in gen.Conditions) InferExpr(cond, scope);
        }
        var elType = InferExpr(element, scope);
        return new ListType(elType);
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Type unification
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Return the most specific type that is a supertype of both <paramref name="a"/>
    /// and <paramref name="b"/>.  Falls back to Unknown only when no common type exists.
    /// </summary>
    public static NajaType Unify(NajaType a, NajaType b)
    {
        if (a == b)                         return a;
        if (a is UnknownType)              return b;
        if (b is UnknownType)              return a;

        // Numeric widening: bool ⊂ int ⊂ float
        if (a is BoolType  && b is BoolType)  return NajaTypes.Bool;
        if (a is BoolType  && b is IntType)   return NajaTypes.Int;
        if (b is BoolType  && a is IntType)   return NajaTypes.Int;
        if (a is IntType   && b is IntType)   return NajaTypes.Int;
        if (a is IntType   && b is FloatType) return NajaTypes.Float;
        if (a is FloatType && b is IntType)   return NajaTypes.Float;
        if (a is FloatType && b is FloatType) return NajaTypes.Float;
        if (a is BoolType  && b is FloatType) return NajaTypes.Float;
        if (b is BoolType  && a is FloatType) return NajaTypes.Float;

        // None + T → nullable (treat as Unknown for now — emitter handles null)
        if (a is NoneType) return b;
        if (b is NoneType) return a;

        // Collection covariance: List<int> unified with List<float> → List<float>
        if (a is ListType la && b is ListType lb)
            return new ListType(Unify(la.ElementType, lb.ElementType));
        if (a is SetType sa && b is SetType sb)
            return new SetType(Unify(sa.ElementType, sb.ElementType));
        if (a is DictType da && b is DictType db)
            return new DictType(Unify(da.KeyType, db.KeyType), Unify(da.ValueType, db.ValueType));

        // TupleType: same length → element-wise unify; different length → Unknown
        if (a is TupleType ta && b is TupleType tb)
        {
            if (ta.ElementTypes.Count != tb.ElementTypes.Count)
                return NajaTypes.Unknown;
            var elems = ta.ElementTypes.Zip(tb.ElementTypes).Select(p => Unify(p.First, p.Second)).ToArray();
            return new TupleType(elems);
        }

        // str + str (already handled by == above, but be explicit)
        if (a is StrType && b is StrType) return NajaTypes.Str;

        // No common subtype found → fall back to dynamic
        return NajaTypes.Unknown;
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Scope / variable management
    // ═══════════════════════════════════════════════════════════════════════════════

    private void PushScope(string name) =>
        _scopes.Push((name, new Dictionary<string, NajaType>()));

    private void PopScope() => _scopes.Pop();

    private Dictionary<string, NajaType> CurrentVars() => _scopes.Peek().Vars;

    private void SetVar(string name, NajaType type, string scope)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (_scopes.Count > 0)
            CurrentVars()[name] = type;
        _vars[(scope, name)] = type;
    }

    private NajaType LookupVar(string name, string scope)
    {
        // Current frame first
        if (_scopes.Count > 0 && CurrentVars().TryGetValue(name, out var t))
            return t;
        // Persisted scope table
        if (_vars.TryGetValue((scope, name), out var t2)) return t2;
        // Module scope
        if (_vars.TryGetValue(("", name), out var t3))   return t3;
        return NajaTypes.Unknown;
    }

    private void BindTarget(Expression target, NajaType type, string scope)
    {
        switch (target)
        {
            case NameExpr n:
                SetVar(n.Name, type, scope);
                _types[n] = type;   // also annotate the name node itself
                break;

            case TupleExpr t:
                // Tuple unpacking: a, b = expr
                var elemType = type is TupleType tt && tt.ElementTypes.Count == t.Elements.Count
                    ? (Func<int, NajaType>)(i => tt.ElementTypes[i])
                    : _ => (type is TupleType || type is ListType lt2
                            ? ElementTypeOf(type) : NajaTypes.Unknown);
                for (int i = 0; i < t.Elements.Count; i++)
                    BindTarget(t.Elements[i], elemType(i), scope);
                break;

            case AttributeExpr a:
                // self.x = expr → record field type
                if (a.Object is NameExpr { Name: "self" })
                    _vars[(scope, a.Attribute)] = type;
                break;
        }
    }

    // ── Snapshot / restore for branch merging ────────────────────────────

    private Dictionary<string, NajaType> SnapshotVars() =>
        new(CurrentVars());

    private void RestoreVars(Dictionary<string, NajaType> snapshot)
    {
        CurrentVars().Clear();
        foreach (var (k, v) in snapshot) CurrentVars()[k] = v;
    }

    private void MergeBranches(
        Dictionary<string, NajaType> trueVars,
        Dictionary<string, NajaType> falseVars,
        string scope)
    {
        var allKeys = trueVars.Keys.Union(falseVars.Keys);
        foreach (var key in allKeys)
        {
            var t = trueVars.GetValueOrDefault(key, NajaTypes.Unknown);
            var f = falseVars.GetValueOrDefault(key, NajaTypes.Unknown);
            var merged = Unify(t, f);
            SetVar(key, merged, scope);
        }
    }

    private bool VarsEqual(Dictionary<string, NajaType> snapshot)
    {
        var current = CurrentVars();
        if (current.Count != snapshot.Count) return false;
        foreach (var (k, v) in current)
            if (!snapshot.TryGetValue(k, out var sv) || !TypesEqual(v, sv))
                return false;
        return true;
    }

    private static bool TypesEqual(NajaType a, NajaType b) =>
        a.GetType() == b.GetType(); // structural equality via type identity

    // ═══════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>Get the element type of an iterable NajaType.</summary>
    private static NajaType ElementTypeOf(NajaType t) => t switch
    {
        ListType  lt => lt.ElementType,
        SetType   st => st.ElementType,
        TupleType tt => tt.ElementTypes.Count > 0 ? tt.ElementTypes[0] : NajaTypes.Unknown,
        DictType  dt => dt.KeyType,          // iterating dict yields keys
        StrType      => NajaTypes.Str,        // iterating str yields str chars
        _            => NajaTypes.Unknown
    };

    /// <summary>
    /// Convert a Python type annotation expression to a NajaType.
    /// Handles: int, float, str, bool, bytes, None,
    ///          list, list[T], List[T],
    ///          dict, dict[K,V], Dict[K,V],
    ///          tuple, tuple[T,...], Tuple[...],
    ///          set, Set[T],
    ///          Optional[T] (→ T, None treated as optional),
    ///          Union[T, U, ...] (→ unified type).
    /// </summary>
    private static NajaType AnnotationToNajaType(Expression ann)
    {
        // Simple name annotations
        if (ann is NameExpr n)
        {
            return n.Name switch
            {
                "int"   => NajaTypes.Int,
                "float" => NajaTypes.Float,
                "str"   => NajaTypes.Str,
                "bool"  => NajaTypes.Bool,
                "bytes" => NajaTypes.Bytes,
                "None"  => NajaTypes.None,
                "list"  => new ListType(NajaTypes.Unknown),
                "dict"  => new DictType(NajaTypes.Unknown, NajaTypes.Unknown),
                "set"   => new SetType(NajaTypes.Unknown),
                "tuple" => new TupleType([]),
                "object"=> NajaTypes.Unknown,
                "Any"   => NajaTypes.Unknown,
                _       => NajaTypes.Unknown
            };
        }

        // Subscript annotations: list[int], dict[str, int], Optional[int], etc.
        if (ann is SubscriptExpr s)
        {
            var outer = s.Object is NameExpr on ? on.Name : "";

            switch (outer)
            {
                case "list" or "List" or "Sequence" or "Iterable":
                    return new ListType(AnnotationToNajaType(s.Index));

                case "set" or "Set" or "FrozenSet":
                    return new SetType(AnnotationToNajaType(s.Index));

                case "dict" or "Dict" or "Mapping":
                    if (s.Index is TupleExpr te && te.Elements.Count == 2)
                        return new DictType(
                            AnnotationToNajaType(te.Elements[0]),
                            AnnotationToNajaType(te.Elements[1]));
                    return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);

                case "tuple" or "Tuple":
                    if (s.Index is TupleExpr tte)
                    {
                        var elems = tte.Elements.Select(AnnotationToNajaType).ToArray();
                        return new TupleType(elems);
                    }
                    return new TupleType([AnnotationToNajaType(s.Index)]);

                case "Optional":
                    // Optional[T] → T (we don't model nullability separately yet)
                    return AnnotationToNajaType(s.Index);

                case "Union":
                    if (s.Index is TupleExpr ute)
                    {
                        NajaType result = NajaTypes.Unknown;
                        foreach (var el in ute.Elements)
                            result = Unify(result, AnnotationToNajaType(el));
                        return result;
                    }
                    return AnnotationToNajaType(s.Index);
            }
        }

        // Attribute annotations: typing.List[int], etc.
        if (ann is AttributeExpr a)
            return AnnotationToNajaType(new NameExpr(a.Attribute, a.Line, a.Column));

        return NajaTypes.Unknown;
    }
}

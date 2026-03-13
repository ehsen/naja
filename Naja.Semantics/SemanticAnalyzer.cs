using Naja.Parser;

namespace Naja.Semantics;

/// <summary>
/// Walks the AST produced by the Naja parser and:
///   1. Builds a SymbolTable tree (one per scope)
///   2. Resolves every NameExpr to a Symbol
///   3. Infers types for every Expression
///   4. Reports semantic errors via DiagnosticBag
///
/// The result is a SemanticModel that the IL emitter queries.
/// </summary>
public sealed class SemanticAnalyzer
{
    private readonly DiagnosticBag _diagnostics = new();

    // Maps each AST node to its inferred type
    private readonly Dictionary<AstNode, NajaType> _types = new();

    // Maps each NameExpr / assignment target to its Symbol
    private readonly Dictionary<AstNode, Symbol> _symbols = new();

    // Current scope during the walk
    private SymbolTable _scope;

    // The builtin root scope — never popped
    private readonly SymbolTable _builtins;

    public SemanticAnalyzer()
    {
        _builtins = BuiltinScope.Create();
        _scope    = new SymbolTable(ScopeKind.Module, "<module>", _builtins);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public SemanticModel Analyze(Module module)
    {
        AnalyzeStatements(module.Body);
        return new SemanticModel(_scope, _diagnostics, _types, _symbols);
    }

    // ── Scope management ──────────────────────────────────────────────────────

    private SymbolTable PushScope(ScopeKind kind, string name)
    {
        _scope = new SymbolTable(kind, name, _scope);
        return _scope;
    }

    private void PopScope()
    {
        if (_scope.Parent is not null)
            _scope = _scope.Parent;
    }

    private T WithScope<T>(ScopeKind kind, string name, Func<T> action)
    {
        PushScope(kind, name);
        var result = action();
        PopScope();
        return result;
    }

    private void WithScope(ScopeKind kind, string name, Action action)
    {
        PushScope(kind, name);
        action();
        PopScope();
    }

    // ── Statement analysis ────────────────────────────────────────────────────

    private void AnalyzeStatements(IReadOnlyList<Statement> stmts)
    {
        foreach (var stmt in stmts)
            AnalyzeStatement(stmt);
    }

    private void AnalyzeStatement(Statement stmt)
    {
        switch (stmt)
        {
            case AssignStatement s:       Analyze(s); break;
            case AnnAssignStatement s:    Analyze(s); break;
            case AugAssignStatement s:    Analyze(s); break;
            case ExprStatement s:         AnalyzeExpr(s.Expr); break;
            case ReturnStatement s:       if (s.Value is not null) AnalyzeExpr(s.Value); break;
            case FunctionDef s:           Analyze(s); break;
            case ClassDef s:              Analyze(s); break;
            case IfStatement s:           Analyze(s); break;
            case WhileStatement s:        Analyze(s); break;
            case ForStatement s:          Analyze(s); break;
            case TryStatement s:          Analyze(s); break;
            case WithStatement s:         Analyze(s); break;
            case ImportStatement s:       Analyze(s); break;
            case FromImportStatement s:   Analyze(s); break;
            case GlobalStatement s:       Analyze(s); break;
            case NonlocalStatement s:     Analyze(s); break;
            case DeleteStatement s:       foreach (var t in s.Targets) AnalyzeExpr(t); break;
            case AssertStatement s:       AnalyzeExpr(s.Test); if (s.Message is not null) AnalyzeExpr(s.Message); break;
            case RaiseStatement s:        if (s.Exception is not null) AnalyzeExpr(s.Exception); break;
            case MatchStatement s:        Analyze(s); break;
            case TypeAliasStatement s:    Analyze(s); break;
            case PassStatement:
            case BreakStatement:
            case ContinueStatement:
                break;
            default:
                _diagnostics.Warning($"Unhandled statement type: {stmt.GetType().Name}", stmt.Line, stmt.Column);
                break;
        }
    }

    // ── Assignment ────────────────────────────────────────────────────────────

    private void Analyze(AssignStatement s)
    {
        var valType = AnalyzeExpr(s.Value);
        foreach (var target in s.Targets)
            DefineTarget(target, valType);
    }

    private void Analyze(AnnAssignStatement s)
    {
        // Resolve annotation to a type
        var annType = ResolveAnnotation(s.Annotation);
        NajaType valType = annType;

        if (s.Value is not null)
            valType = AnalyzeExpr(s.Value);

        DefineTarget(s.Target, annType != NajaTypes.Unknown ? annType : valType);
    }

    private void Analyze(AugAssignStatement s)
    {
        var targetType = AnalyzeExpr(s.Target);
        var valueType  = AnalyzeExpr(s.Value);
        var resultType = InferBinaryType(s.Op, targetType, valueType);
        SetType(s, resultType);
    }

    private void DefineTarget(Expression target, NajaType type)
    {
        switch (target)
        {
            case NameExpr n:
                var sym = _scope.Define(n.Name, SymbolKind.Variable, type, n.Line, n.Column);
                _symbols[n] = sym;
                SetType(n, type);
                break;

            case TupleExpr t:
                // Unpacking:  a, b = 1, 2
                foreach (var elem in t.Elements)
                    DefineTarget(elem, NajaTypes.Unknown);
                break;

            case ListExpr l:
                foreach (var elem in l.Elements)
                    DefineTarget(elem, NajaTypes.Unknown);
                break;

            case StarredExpr star:
                DefineTarget(star.Value, new ListType(NajaTypes.Unknown));
                break;

            case AttributeExpr:
            case SubscriptExpr:
                AnalyzeExpr(target);
                break;

            default:
                _diagnostics.Warning($"Unhandled assignment target: {target.GetType().Name}",
                    target.Line, target.Column);
                break;
        }
    }

    // ── Function definition ───────────────────────────────────────────────────

    private void Analyze(FunctionDef fn)
    {
        // Build parameter types from annotations
        var paramTypes = fn.Params.Select(p =>
            p.Annotation is not null ? ResolveAnnotation(p.Annotation) : NajaTypes.Unknown
        ).ToList();

        var returnType = fn.ReturnAnnotation is not null
            ? ResolveAnnotation(fn.ReturnAnnotation)
            : NajaTypes.Unknown;

        var fnType = new FunctionType(paramTypes, returnType);
        var sym    = _scope.Define(fn.Name, SymbolKind.Function, fnType, fn.Line, fn.Column);
        _symbols[fn] = sym;

        // Analyze decorators in current scope
        foreach (var dec in fn.Decorators)
            AnalyzeExpr(dec);

        // Analyze body in new function scope
        WithScope(ScopeKind.Function, fn.Name, () =>
        {
            // Define parameters in function scope
            foreach (var (param, ptype) in fn.Params.Zip(paramTypes))
            {
                _scope.Define(param.Name, SymbolKind.Parameter, ptype, fn.Line, fn.Column);
                if (param.Default is not null) AnalyzeExpr(param.Default);
            }

            AnalyzeStatements(fn.Body);
        });
    }

    // ── Class definition ──────────────────────────────────────────────────────

    private void Analyze(ClassDef cls)
    {
        var clsType = new ClassType(cls.Name);
        var sym     = _scope.Define(cls.Name, SymbolKind.Class, clsType, cls.Line, cls.Column);
        _symbols[cls] = sym;

        foreach (var dec in cls.Decorators) AnalyzeExpr(dec);
        foreach (var base_ in cls.Bases)    AnalyzeExpr(base_);

        WithScope(ScopeKind.Class, cls.Name, () =>
        {
            // 'self' is available inside class methods (defined when analyzing each method)
            AnalyzeStatements(cls.Body);
        });
    }

    // ── Control flow ──────────────────────────────────────────────────────────

    private void Analyze(IfStatement s)
    {
        AnalyzeExpr(s.Condition);
        AnalyzeStatements(s.Then);
        foreach (var (cond, body) in s.Elifs)
        {
            AnalyzeExpr(cond);
            AnalyzeStatements(body);
        }
        AnalyzeStatements(s.Else);
    }

    private void Analyze(WhileStatement s)
    {
        AnalyzeExpr(s.Condition);
        AnalyzeStatements(s.Body);
        AnalyzeStatements(s.Else);
    }

    private void Analyze(ForStatement s)
    {
        var iterType = AnalyzeExpr(s.Iter);
        var elemType = InferIterElementType(iterType);
        DefineTarget(s.Target, elemType);

        WithScope(ScopeKind.Function, "<for>", () =>
            AnalyzeStatements(s.Body));

        AnalyzeStatements(s.Else);
    }

    private void Analyze(TryStatement s)
    {
        AnalyzeStatements(s.Body);
        foreach (var h in s.Handlers)
        {
            if (h.ExceptionType is not null) AnalyzeExpr(h.ExceptionType);
            if (h.Name is not null)
                _scope.Define(h.Name, SymbolKind.Variable,
                    new ClassType("Exception"), h.Line, h.Column);
            AnalyzeStatements(h.Body);
        }
        AnalyzeStatements(s.Else);
        AnalyzeStatements(s.Finally);
    }

    private void Analyze(WithStatement s)
    {
        foreach (var item in s.Items)
        {
            var ctxType = AnalyzeExpr(item.Context);
            if (item.Target is not null)
                DefineTarget(item.Target, NajaTypes.Unknown);
        }
        AnalyzeStatements(s.Body);
    }

    // ── Import ────────────────────────────────────────────────────────────────

    private void Analyze(ImportStatement s)
    {
        foreach (var alias in s.Names)
        {
            var name = alias.Alias ?? alias.Name.Split('.')[0];
            _scope.Define(name, SymbolKind.Import, NajaTypes.Unknown, s.Line, s.Column);
        }
    }

    private void Analyze(FromImportStatement s)
    {
        foreach (var alias in s.Names)
        {
            var name = alias.Alias ?? alias.Name;
            _scope.Define(name, SymbolKind.Import, NajaTypes.Unknown, s.Line, s.Column);
        }
    }

    // ── Global / Nonlocal ─────────────────────────────────────────────────────

    private void Analyze(GlobalStatement s)
    {
        foreach (var name in s.Names)
        {
            var sym = _scope.LookupLocal(name);
            if (sym is null)
            {
                sym = _scope.Define(name, SymbolKind.Variable, NajaTypes.Unknown, s.Line, s.Column);
            }
            sym.IsGlobal = true;
        }
    }

    private void Analyze(NonlocalStatement s)
    {
        foreach (var name in s.Names)
        {
            var sym = _scope.Lookup(name);
            if (sym is null)
                _diagnostics.Error($"No enclosing scope defines '{name}'", s.Line, s.Column);
            else
                sym.IsNonlocal = true;
        }
    }

    // ── Match ─────────────────────────────────────────────────────────────────

    private void Analyze(MatchStatement s)
    {
        AnalyzeExpr(s.Subject);
        foreach (var c in s.Cases)
        {
            AnalyzePattern(c.Pattern);
            if (c.Guard is not null) AnalyzeExpr(c.Guard);
            AnalyzeStatements(c.Body);
        }
    }

    private void AnalyzePattern(Pattern p)
    {
        switch (p)
        {
            case CapturePattern cp:
                if (cp.Name != "_")
                    _scope.Define(cp.Name, SymbolKind.Variable, NajaTypes.Unknown, cp.Line, cp.Column);
                break;
            case AsPattern ap:
                AnalyzePattern(ap.Inner);
                if (ap.Name != "_")
                    _scope.Define(ap.Name, SymbolKind.Variable, NajaTypes.Unknown, ap.Line, ap.Column);
                break;
            case OrPattern op:
                // Only analyze the first pattern since Python requires all Or branches to bind the same names
                if (op.Patterns.Count > 0) AnalyzePattern(op.Patterns[0]);
                break;
            case SequencePattern sp:
                foreach (var inner in sp.Patterns) AnalyzePattern(inner);
                break;
            case MappingPattern mp:
                foreach (var (_, v) in mp.Pairs) AnalyzePattern(v);
                if (mp.Rest is not null && mp.Rest != "_")
                    _scope.Define(mp.Rest, SymbolKind.Variable, new DictType(NajaTypes.Unknown, NajaTypes.Unknown), mp.Line, mp.Column);
                break;
            case ClassPattern cp:
                foreach (var inner in cp.Positional) AnalyzePattern(inner);
                foreach (var (_, v) in cp.Keyword) AnalyzePattern(v);
                break;
        }
    }


    // ── Type alias ────────────────────────────────────────────────────────────

    private void Analyze(TypeAliasStatement s)
    {
        var valType = AnalyzeExpr(s.Value);
        _scope.Define(s.Name, SymbolKind.Variable, new TypeofType(valType), s.Line, s.Column);
    }

    // ── Expression analysis ───────────────────────────────────────────────────

    private NajaType AnalyzeExpr(Expression expr)
    {
        var type = expr switch
        {
            IntLiteral     => NajaTypes.Int,
            FloatLiteral   => NajaTypes.Float,
            StringLiteral  => NajaTypes.Str,
            FStringExpr    => NajaTypes.Str,
            BoolLiteral    => NajaTypes.Bool,
            NoneLiteral    => NajaTypes.None,
            EllipsisLiteral => NajaTypes.Unknown,

            NameExpr n          => AnalyzeName(n),
            BinaryExpr b        => AnalyzeBinary(b),
            UnaryExpr u         => AnalyzeUnary(u),
            BoolOpExpr bo       => AnalyzeBoolOp(bo),
            CompareExpr c       => AnalyzeCompare(c),
            IfExpr i            => AnalyzeIfExpr(i),
            CallExpr c          => AnalyzeCall(c),
            AttributeExpr a     => AnalyzeAttribute(a),
            SubscriptExpr s     => AnalyzeSubscript(s),
            SliceExpr           => NajaTypes.Unknown,
            LambdaExpr l        => AnalyzeLambda(l),
            ListExpr l          => AnalyzeList(l),
            TupleExpr t         => AnalyzeTuple(t),
            SetExpr s           => AnalyzeSet(s),
            DictExpr d          => AnalyzeDict(d),
            ListCompExpr lc     => AnalyzeListComp(lc),
            SetCompExpr sc      => AnalyzeSetComp(sc),
            DictCompExpr dc     => AnalyzeDictComp(dc),
            GeneratorExpr g     => AnalyzeGenerator(g),
            StarredExpr s       => AnalyzeExpr(s.Value),
            AwaitExpr a         => AnalyzeExpr(a.Value),
            YieldExpr           => NajaTypes.Unknown,
            WalrusExpr w        => AnalyzeWalrus(w),

            _ => NajaTypes.Unknown
        };

        SetType(expr, type);
        return type;
    }

    private NajaType AnalyzeName(NameExpr n)
    {
        var sym = _scope.Lookup(n.Name);
        if (sym is null)
        {
            _diagnostics.Warning($"Name '{n.Name}' is not defined", n.Line, n.Column);
            return NajaTypes.Unknown;
        }
        _symbols[n] = sym;
        return sym.Type;
    }

    private NajaType AnalyzeBinary(BinaryExpr b)
    {
        var left  = AnalyzeExpr(b.Left);
        var right = AnalyzeExpr(b.Right);
        return InferBinaryType(b.Op, left, right);
    }

    private NajaType AnalyzeUnary(UnaryExpr u)
    {
        var operand = AnalyzeExpr(u.Operand);
        return u.Op switch
        {
            UnaryOp.Not    => NajaTypes.Bool,
            UnaryOp.Neg    => operand is FloatType ? NajaTypes.Float : NajaTypes.Int,
            UnaryOp.Pos    => operand,
            UnaryOp.Invert => NajaTypes.Int,
            _              => NajaTypes.Unknown
        };
    }

    private NajaType AnalyzeBoolOp(BoolOpExpr bo)
    {
        NajaType result = NajaTypes.Unknown;
        foreach (var v in bo.Values)
            result = NajaTypes.Widen(result, AnalyzeExpr(v));
        return result;
    }

    private NajaType AnalyzeCompare(CompareExpr c)
    {
        AnalyzeExpr(c.Left);
        foreach (var (_, right) in c.Comparators)
            AnalyzeExpr(right);
        return NajaTypes.Bool;
    }

    private NajaType AnalyzeIfExpr(IfExpr i)
    {
        AnalyzeExpr(i.Condition);
        var thenType = AnalyzeExpr(i.Then);
        var elseType = AnalyzeExpr(i.Else);
        return NajaTypes.Widen(thenType, elseType);
    }

    private NajaType AnalyzeCall(CallExpr c)
    {
        var funcType = AnalyzeExpr(c.Func);
        foreach (var arg in c.Args) AnalyzeExpr(arg.Value);

        if (funcType is FunctionType ft)
            return ft.ReturnType;

        return NajaTypes.Unknown;
    }

    private NajaType AnalyzeAttribute(AttributeExpr a)
    {
        AnalyzeExpr(a.Object);
        return NajaTypes.Unknown;  // resolved in Phase 4 with CLR reflection
    }

    private NajaType AnalyzeSubscript(SubscriptExpr s)
    {
        var objType = AnalyzeExpr(s.Object);
        AnalyzeExpr(s.Index);
        return objType switch
        {
            ListType l => l.ElementType,
            DictType d => d.ValueType,
            StrType    => NajaTypes.Str,
            _          => NajaTypes.Unknown
        };
    }

    private NajaType AnalyzeLambda(LambdaExpr l)
    {
        return WithScope<NajaType>(ScopeKind.Lambda, "<lambda>", () =>
        {
            foreach (var p in l.Params)
            {
                _scope.Define(p.Name, SymbolKind.Parameter, NajaTypes.Unknown, l.Line, l.Column);
                if (p.Default is not null) AnalyzeExpr(p.Default);
            }
            var bodyType = AnalyzeExpr(l.Body);
            var paramTypes = l.Params.Select(_ => NajaTypes.Unknown).ToList<NajaType>();
            return new FunctionType(paramTypes, bodyType);
        });
    }

    private NajaType AnalyzeList(ListExpr l)
    {
        if (l.Elements.Count == 0) return new ListType(NajaTypes.Unknown);
        var elemType = l.Elements.Select(AnalyzeExpr).Aggregate(NajaTypes.Widen);
        return new ListType(elemType);
    }

    private NajaType AnalyzeTuple(TupleExpr t)
    {
        var elemTypes = t.Elements.Select(AnalyzeExpr).ToList<NajaType>();
        return new TupleType(elemTypes);
    }

    private NajaType AnalyzeSet(SetExpr s)
    {
        if (s.Elements.Count == 0) return new SetType(NajaTypes.Unknown);
        var elemType = s.Elements.Select(AnalyzeExpr).Aggregate(NajaTypes.Widen);
        return new SetType(elemType);
    }

    private NajaType AnalyzeDict(DictExpr d)
    {
        if (d.Pairs.Count == 0) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
        NajaType kt = NajaTypes.Unknown, vt = NajaTypes.Unknown;
        foreach (var (k, v) in d.Pairs)
        {
            if (k is not null) kt = NajaTypes.Widen(kt, AnalyzeExpr(k));
            vt = NajaTypes.Widen(vt, AnalyzeExpr(v));
        }
        return new DictType(kt, vt);
    }

    private NajaType AnalyzeListComp(ListCompExpr lc)
    {
        return WithScope<NajaType>(ScopeKind.Comprehension, "<listcomp>", () =>
        {
            AnalyzeComprehensions(lc.Generators);
            var elemType = AnalyzeExpr(lc.Element);
            return new ListType(elemType);
        });
    }

    private NajaType AnalyzeSetComp(SetCompExpr sc)
    {
        return WithScope<NajaType>(ScopeKind.Comprehension, "<setcomp>", () =>
        {
            AnalyzeComprehensions(sc.Generators);
            var elemType = AnalyzeExpr(sc.Element);
            return new SetType(elemType);
        });
    }

    private NajaType AnalyzeDictComp(DictCompExpr dc)
    {
        return WithScope<NajaType>(ScopeKind.Comprehension, "<dictcomp>", () =>
        {
            AnalyzeComprehensions(dc.Generators);
            var kt = AnalyzeExpr(dc.Key);
            var vt = AnalyzeExpr(dc.Value);
            return new DictType(kt, vt);
        });
    }

    private NajaType AnalyzeGenerator(GeneratorExpr g)
    {
        return WithScope<NajaType>(ScopeKind.Comprehension, "<genexpr>", () =>
        {
            AnalyzeComprehensions(g.Generators);
            AnalyzeExpr(g.Element);
            return NajaTypes.Unknown;
        });
    }

    private void AnalyzeComprehensions(IReadOnlyList<Comprehension> comps)
    {
        foreach (var comp in comps)
        {
            var iterType = AnalyzeExpr(comp.Iter);
            var elemType = InferIterElementType(iterType);
            DefineTarget(comp.Target, elemType);
            foreach (var cond in comp.Conditions)
                AnalyzeExpr(cond);
        }
    }

    private NajaType AnalyzeWalrus(WalrusExpr w)
    {
        var type = AnalyzeExpr(w.Value);
        _scope.Define(w.Target, SymbolKind.Variable, type, w.Line, w.Column);
        return type;
    }

    // ── Type inference helpers ────────────────────────────────────────────────

    private NajaType InferBinaryType(BinaryOp op, NajaType left, NajaType right) => op switch
    {
        BinaryOp.Add =>
            (left, right) switch
            {
                (StrType,   StrType)   => NajaTypes.Str,
                (IntType,   IntType)   => NajaTypes.Int,
                (FloatType, _)         => NajaTypes.Float,
                (_,         FloatType) => NajaTypes.Float,
                (ListType l, ListType) => l,
                _                     => NajaTypes.Unknown
            },
        BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Mod =>
            (left is FloatType || right is FloatType) ? NajaTypes.Float : NajaTypes.Int,
        BinaryOp.Div    => NajaTypes.Float,   // always float in Python 3
        BinaryOp.FloorDiv => NajaTypes.Int,
        BinaryOp.Pow    =>
            (left is FloatType || right is FloatType) ? NajaTypes.Float : NajaTypes.Int,
        BinaryOp.BitAnd or BinaryOp.BitOr or BinaryOp.BitXor
                        => NajaTypes.Int,
        BinaryOp.LShift or BinaryOp.RShift
                        => NajaTypes.Int,
        _               => NajaTypes.Unknown
    };

    private NajaType InferIterElementType(NajaType iterType) => iterType switch
    {
        ListType l => l.ElementType,
        SetType  s => s.ElementType,
        DictType d => d.KeyType,
        StrType    => NajaTypes.Str,
        _          => NajaTypes.Unknown
    };

    private NajaType ResolveAnnotation(Expression ann) => ann switch
    {
        NameExpr n          => NajaTypes.FromAnnotation(n.Name),
        AttributeExpr       => NajaTypes.Unknown,
        SubscriptExpr s     => ResolveGenericAnnotation(s),
        NoneLiteral         => NajaTypes.None,
        _                   => NajaTypes.Unknown
    };

    private NajaType ResolveGenericAnnotation(SubscriptExpr s)
    {
        if (s.Object is not NameExpr name) return NajaTypes.Unknown;

        return name.Name switch
        {
            "list"  => new ListType(ResolveAnnotation(s.Index)),
            "set"   => new SetType(ResolveAnnotation(s.Index)),
            "dict"  => s.Index is TupleExpr t && t.Elements.Count == 2
                        ? new DictType(ResolveAnnotation(t.Elements[0]), ResolveAnnotation(t.Elements[1]))
                        : new DictType(NajaTypes.Unknown, NajaTypes.Unknown),
            "Optional" => ResolveAnnotation(s.Index),
            _       => NajaTypes.Unknown
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetType(AstNode node, NajaType type) => _types[node] = type;
}

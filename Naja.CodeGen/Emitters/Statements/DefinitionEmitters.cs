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
        // Register the FunctionDef so NameEmitters can access it for wrapping with defaults
        _ctx.FunctionDefs[s.Name] = s;


        var seen = new HashSet<string>();
        foreach (var p in s.Params)
            if (!seen.Add(p.Name))
                throw new CodeGenException(
                    $"duplicate argument '{p.Name}' in function definition", s.Line, s.Column);

        // Detect outer-scope parameters captured by this nested function.
        // For non-generators, use the NajaFunction snapshot pattern: compile them as
        // extra trailing params so each call to the outer function gets its own copy.
        bool isGenerator = ContainsYield(s.Body);
        var capturedOuterParamNames = new List<string>();
        if (!isGenerator && _ctx.HoistedParams.Count > 0)
        {
            var innerBodyRefs = CollectReferencedNames(s.Body);
            var innerOwnParams = new HashSet<string>(s.Params.Select(p => p.Name));
            var innerAssigned = CollectAssignedNames(s.Body);
            var innerNonlocals = CollectNonlocalNames(s.Body);
            foreach (var nl in innerNonlocals) innerAssigned.Remove(nl);
            foreach (var hp in _ctx.HoistedParams)
            {
                if (innerOwnParams.Contains(hp)) continue;
                if (innerAssigned.Contains(hp)) continue;
                if (innerNonlocals.Contains(hp)) continue;
                if (innerBodyRefs.Contains(hp))
                    capturedOuterParamNames.Add(hp);
            }
        }

        // Detect outer-scope locals stored in per-call cells that this nested function references.
        // Each captured cell variable gets an extra object[] param (__cell_varname) so the inner
        // function can read/write the same cell as the outer call that created it.
        var capturedCellNames = new List<string>();
        var innerBodyRefs2 = CollectReferencedNames(s.Body);
        if (!isGenerator && (_ctx.CellLocals.Count > 0 || _ctx.CellParamOf.Count > 0))
        {
            var innerOwnParams2 = new HashSet<string>(s.Params.Select(p => p.Name));
            var innerNonlocals2 = CollectNonlocalNames(s.Body);
            foreach (var cv in _ctx.CellLocals.Keys.Concat(_ctx.CellParamOf.Keys))
            {
                if (innerOwnParams2.Contains(cv)) continue;
                if (!innerBodyRefs2.Contains(cv) && !innerNonlocals2.Contains(cv)) continue;
                if (!capturedCellNames.Contains(cv))
                    capturedCellNames.Add(cv);
            }
        }

        // BUG-A3: Handle recursive nested functions that reference their own name.
        // If this nested function references its own name (for recursion),
        // we need to make sure it's available as a cell so it can call itself.
        if (!isGenerator && innerBodyRefs2.Contains(s.Name))
        {
            // Promote this function's name to a cell variable in the outer scope
            if (!capturedCellNames.Contains(s.Name))
            {
                // Create a cell for this function in the outer scope if not already exists
                if (!_ctx.CellLocals.ContainsKey(s.Name))
                {
                    _ctx.CellLocals[s.Name] = _ctx.Locals.Declare($"__cell_{s.Name}", typeof(object[]));
                }
                capturedCellNames.Add(s.Name);
            }
        }

        var pts = s.Params.Select(_ => typeof(object))
            .Concat(capturedOuterParamNames.Select(_ => typeof(object)))
            .Concat(capturedCellNames.Select(_ => typeof(object[])))
            .ToArray();
        var allParamNames = s.Params.Select(p => p.Name)
            .Concat(capturedOuterParamNames)
            .Concat(capturedCellNames.Select(cv => $"__cell_{cv}"))
            .ToList();
        var mb = _ctx.TypeBuilder.DefineMethod(
            $"{s.Name}_{s.Line}",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(object),
            pts);

        for (int i = 0; i < allParamNames.Count; i++)
            mb.DefineParameter(i + 1, ParameterAttributes.None, allParamNames[i]);

        var paramNames = allParamNames;
        var fnCtx = new EmitContext(mb.GetILGenerator(), _ctx.Model,
                                    _ctx.TypeBuilder, _ctx.Module,
                                    typeof(object), paramNames);
        fnCtx.IsInsideFunction = true;

        // Propagate all outer-scope lookups into the nested function context
        foreach (var (k, v) in _ctx.Fields) fnCtx.Fields[k] = v;
        // Remove captured outer params from fnCtx.Fields — they are now explicit params
        // of this function, so NameEmitters resolves them via ldarg instead of ldsfld.
        foreach (var cap in capturedOuterParamNames)
            fnCtx.Fields.Remove(cap);
        // Remove captured cell vars from fnCtx.Fields — they are accessed via CellParamOf.
        foreach (var cv in capturedCellNames)
            fnCtx.Fields.Remove(cv);
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
        // Nested functions are always compiled as Private Static methods — never instance methods.
        // Propagating IsInstanceMethod=true from a class method context would cause
        // TryEmitLoadParam to offset arg indices by +1, emitting ldarg_1 for the first
        // parameter on a method that only has ldarg_0.
        fnCtx.IsInstanceMethod = false;

        // Register cell params so NameEmitters/AssignmentEmitters use the cell-param pattern
        // (read/write through the object[] reference passed as __cell_varname).
        foreach (var cv in capturedCellNames)
            fnCtx.CellParamOf[cv] = $"__cell_{cv}";
        // Register which cells this inner function captures so NajaFunction wrapping
        // in NameEmitters can pass cell references as trailing defaults.
        if (capturedCellNames.Count > 0)
            _ctx.FunctionCapturedCells[s.Name] = capturedCellNames.ToList();

        // Pre-scan for nonlocal declarations in this function body and promote those
        // variables to static fields NOW (before emitting the body) so any inner
        // lambdas/closures also see them as fields.
        var nonlocalNames = CollectNonlocalNames(s.Body);
        foreach (var nlName in nonlocalNames)
        {
            // If the var is already accessible via a cell param, no static field needed.
            if (fnCtx.CellParamOf.ContainsKey(nlName)) continue;
            if (!fnCtx.Fields.ContainsKey(nlName))
            {
                var nlField = _ctx.TypeBuilder.DefineField($"__nl_{nlName}", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
                fnCtx.Fields[nlName] = nlField;
                _ctx.Fields[nlName] = nlField;  // also visible in outer scope
            }
        }

        // Hoist variables referenced by nested functions, including parameters:
        // if an inner function references a name that is assigned *or passed as a
        // parameter* in this function, promote it to a static field.
        // Skip vars already handled as cell params (per-call cells).
        var nestedRefs = CollectNamesReferencedByNestedFunctions(s.Body);
        var assigned = CollectAssignedNames(s.Body);
        // Include capturedOuterParamNames so that params captured from an outer scope
        // can be further hoisted for doubly-nested functions (multi-level closure).
        var paramNamesSet = new HashSet<string>(s.Params.Select(p => p.Name).Concat(capturedOuterParamNames));
        foreach (var r in nestedRefs.Intersect(assigned.Union(paramNamesSet)))
        {
            if (fnCtx.CellParamOf.ContainsKey(r)) continue;  // cell param handles this
            if (!fnCtx.Fields.ContainsKey(r))
            {
                // Reuse existing outer-scope field instead of creating a duplicate
                // (handles the case where a capturedOuterParam needs further hoisting)
                if (_ctx.Fields.ContainsKey(r))
                    fnCtx.Fields[r] = _ctx.Fields[r];
                else
                {
                    var hoisted = _ctx.TypeBuilder.DefineField($"__nl_{r}", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
                    fnCtx.Fields[r] = hoisted;
                    _ctx.Fields[r] = hoisted;
                }
            }
            if (paramNamesSet.Contains(r))
                fnCtx.HoistedParams.Add(r);
        }
        // Copy original hoisted parameter values into their static fields at function entry
        for (int hi = 0; hi < s.Params.Count; hi++)
        {
            if (!fnCtx.HoistedParams.Contains(s.Params[hi].Name)) continue;
            switch (hi) { case 0: fnCtx.IL.Emit(OpCodes.Ldarg_0); break; case 1: fnCtx.IL.Emit(OpCodes.Ldarg_1); break; case 2: fnCtx.IL.Emit(OpCodes.Ldarg_2); break; case 3: fnCtx.IL.Emit(OpCodes.Ldarg_3); break; default: fnCtx.IL.Emit(OpCodes.Ldarg_S, (byte)hi); break; }
            fnCtx.IL.Emit(OpCodes.Stsfld, fnCtx.Fields[s.Params[hi].Name]);
        }
        // Copy capturedOuterParams that need further hoisting for doubly-nested functions
        for (int ci = 0; ci < capturedOuterParamNames.Count; ci++)
        {
            var capName = capturedOuterParamNames[ci];
            if (!fnCtx.HoistedParams.Contains(capName)) continue;
            int argIdx = s.Params.Count + ci;
            switch (argIdx) { case 0: fnCtx.IL.Emit(OpCodes.Ldarg_0); break; case 1: fnCtx.IL.Emit(OpCodes.Ldarg_1); break; case 2: fnCtx.IL.Emit(OpCodes.Ldarg_2); break; case 3: fnCtx.IL.Emit(OpCodes.Ldarg_3); break; default: fnCtx.IL.Emit(OpCodes.Ldarg_S, (byte)argIdx); break; }
            fnCtx.IL.Emit(OpCodes.Stsfld, fnCtx.Fields[capName]);
        }

        // (Closure hoisting moved to AssemblyEmitter.EmitFunctionBody so promotion
        // happens before the outer function body is emitted.)

        // Check if this function contains yield statements (is a generator)
        // (isGenerator already computed during captured-param detection above)
        if (isGenerator)
        {
            // ── Generator: two-method compilation ─────────────────────────────────
            // Wrapper (mb): packs original args into object[] and returns NajaGenerator.
            // Body (__gen_body__): void (NajaGenerator, object[]) coroutine.

            var bodyMethod = _ctx.TypeBuilder.DefineMethod(
                $"{s.Name}_{s.Line}__gen_body__",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(void),
                new System.Type[] { typeof(NajaGenerator), typeof(object[]) });
            bodyMethod.DefineParameter(1, ParameterAttributes.None, "__gen");
            bodyMethod.DefineParameter(2, ParameterAttributes.None, "__args");

            // Emit wrapper into mb: build args[], create delegate, new NajaGenerator, ret
            fnCtx.IL.Emit(OpCodes.Ldc_I4, s.Params.Count);
            fnCtx.IL.Emit(OpCodes.Newarr, typeof(object));
            for (int wi = 0; wi < s.Params.Count; wi++)
            {
                fnCtx.IL.Emit(OpCodes.Dup);
                fnCtx.IL.Emit(OpCodes.Ldc_I4, wi);
                switch (wi) { case 0: fnCtx.IL.Emit(OpCodes.Ldarg_0); break; case 1: fnCtx.IL.Emit(OpCodes.Ldarg_1); break; case 2: fnCtx.IL.Emit(OpCodes.Ldarg_2); break; case 3: fnCtx.IL.Emit(OpCodes.Ldarg_3); break; default: fnCtx.IL.Emit(OpCodes.Ldarg_S, (byte)wi); break; }
                fnCtx.IL.Emit(OpCodes.Stelem_Ref);
            }
            var wrapperArgsLocal = fnCtx.Locals.Declare("__gen_args", typeof(object[]));
            fnCtx.IL.Emit(OpCodes.Stloc, wrapperArgsLocal);
            fnCtx.IL.Emit(OpCodes.Ldnull);
            fnCtx.IL.Emit(OpCodes.Ldftn, bodyMethod);
            fnCtx.IL.Emit(OpCodes.Newobj, typeof(Action<NajaGenerator, object[]>).GetConstructors()[0]);
            fnCtx.IL.Emit(OpCodes.Ldloc, wrapperArgsLocal);
            fnCtx.IL.Emit(OpCodes.Newobj, NajaBuiltinsMethodCache.NajaGenerator_Ctor);
            fnCtx.IL.Emit(OpCodes.Ret);

            // Build body method context
            var bodyIL = bodyMethod.GetILGenerator();
            var bodyCtx = new EmitContext(bodyIL, _ctx.Model, _ctx.TypeBuilder, _ctx.Module,
                                          typeof(void), System.Array.Empty<string>());
            bodyCtx.IsInsideFunction = true;
            bodyCtx.IsGeneratorBody = true;

            foreach (var (k, v) in fnCtx.Fields) bodyCtx.Fields[k] = v;
            foreach (var (k, v) in fnCtx.Methods) bodyCtx.Methods[k] = v;
            foreach (var (k, v) in fnCtx.MethodParamTypes) bodyCtx.MethodParamTypes[k] = v;
            foreach (var (k, v) in fnCtx.ClassTypes) bodyCtx.ClassTypes[k] = v;
            foreach (var (k, v) in fnCtx.ClassConstructors) bodyCtx.ClassConstructors[k] = v;
            foreach (var (k, v) in fnCtx.InstanceFields) bodyCtx.InstanceFields[k] = v;
            foreach (var (k, v) in fnCtx.AllClassMethods) bodyCtx.AllClassMethods[k] = v;
            foreach (var (k, v) in fnCtx.AllClassMethodParamTypes) bodyCtx.AllClassMethodParamTypes[k] = v;
            foreach (var mn in fnCtx.ClassMethods) bodyCtx.ClassMethods.Add(mn);
            foreach (var (k, v) in fnCtx.ImportMap) bodyCtx.ImportMap[k] = v;
            foreach (var (k, v) in fnCtx.NamespaceImports) bodyCtx.NamespaceImports[k] = v;
            bodyCtx.SelfName = fnCtx.SelfName;
            bodyCtx.IsInstanceMethod = false;

            // Unpack original params from __args[i] into locals; also init hoisted fields
            for (int bi = 0; bi < s.Params.Count; bi++)
            {
                var pname = s.Params[bi].Name;
                var plocal = bodyCtx.Locals.Declare(pname, typeof(object));
                bodyIL.Emit(OpCodes.Ldarg_1);
                bodyIL.Emit(OpCodes.Ldc_I4, bi);
                bodyIL.Emit(OpCodes.Ldelem_Ref);
                bodyIL.Emit(OpCodes.Stloc, plocal);
                if (bodyCtx.Fields.TryGetValue(pname, out var hField))
                {
                    bodyIL.Emit(OpCodes.Ldloc, plocal);
                    bodyIL.Emit(OpCodes.Stsfld, hField);
                }
            }

            // Pre-register wrapper for recursive self-calls inside the generator body
            bodyCtx.Methods[s.Name] = mb;
            bodyCtx.MethodParamTypes[s.Name] = pts;

            var bodyEmitter = new StatementEmitter(bodyCtx);
            bodyEmitter.EmitAll(s.Body);

            // Propagate nested-class field contexts from generator body upward
            foreach (var (k, v) in bodyCtx.NestedClassFieldContexts)
                _ctx.NestedClassFieldContexts[k] = v;

            if (bodyCtx.MethodReturnLabel.HasValue)
                bodyIL.MarkLabel(bodyCtx.MethodReturnLabel.Value);
            bodyIL.Emit(OpCodes.Ret);

            // Register wrapper in current context
            _ctx.Methods[s.Name] = mb;
            _ctx.MethodParamTypes[s.Name] = pts;
            return;
        }

        var fnIL = fnCtx.IL;

        // Pre-register for recursive self-calls before compiling the body
        fnCtx.Methods[s.Name] = mb;
        fnCtx.MethodParamTypes[s.Name] = pts;

        var bodyEmitter2 = new StatementEmitter(fnCtx);
        bodyEmitter2.EmitAll(s.Body);

        // Propagate nested-class field contexts upward so the AssemblyEmitter can
        // supply the correct field bindings for each nested class in Pass 3.
        foreach (var (k, v) in fnCtx.NestedClassFieldContexts)
            _ctx.NestedClassFieldContexts[k] = v;

        // Non-generator epilog
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

        // Register in current context so calls within scope find it
        _ctx.Methods[s.Name] = mb;
        _ctx.MethodParamTypes[s.Name] = pts;
        if (capturedOuterParamNames.Count > 0)
            _ctx.FunctionCapturedParams[s.Name] = capturedOuterParamNames;

        // Apply non-trivial decorators (evaluate top-to-bottom, apply bottom-to-top)
        var nonTrivialDecs = s.Decorators
            .Where(d => !(d is NameExpr { Name: "staticmethod" or "classmethod" or "property" }))
            .ToList();
        if (nonTrivialDecs.Count > 0)
        {
            // Phase 1: evaluate all decorator expressions top-to-bottom
            var decLocals = new List<LocalBuilder>();
            for (int d = 0; d < nonTrivialDecs.Count; d++)
            {
                var decLocal = _ctx.Locals.Declare($"__fdec_{s.Name}_{d}", typeof(object));
                TypeMapper.EmitBox(IL, _exprEmitter.Emit(nonTrivialDecs[d]));
                IL.Emit(OpCodes.Stloc, decLocal);
                decLocals.Add(decLocal);
            }

            // Phase 2: create NajaFunction wrapping the compiled method
            var typeArgsForDec = pts.Concat(new[] { typeof(object) }).ToArray();
            var delegateTypeForDec = System.Linq.Expressions.Expression.GetFuncType(typeArgsForDec);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Ldftn, mb);
            IL.Emit(OpCodes.Newobj, delegateTypeForDec.GetConstructors()[0]);
            var totalDefaultsForDec = capturedOuterParamNames.Count + capturedCellNames.Count
                + s.Params.Count(p => p.Default is not null);
            IL.Emit(OpCodes.Ldc_I4, totalDefaultsForDec);
            IL.Emit(OpCodes.Newarr, typeof(object));
            for (int ci = 0; ci < capturedOuterParamNames.Count; ci++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, ci);
                if (_ctx.Fields.TryGetValue(capturedOuterParamNames[ci], out var capField))
                    IL.Emit(OpCodes.Ldsfld, capField);
                else
                    IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            for (int ci = 0; ci < capturedCellNames.Count; ci++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, capturedOuterParamNames.Count + ci);
                if (_ctx.CellLocals.TryGetValue(capturedCellNames[ci], out var cellLoc))
                    IL.Emit(OpCodes.Ldloc, cellLoc);
                else
                    IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            int defIdxForDec = 0;
            foreach (var param in s.Params)
            {
                if (param.Default is not null)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, capturedOuterParamNames.Count + capturedCellNames.Count + defIdxForDec);
                    TypeMapper.EmitBox(IL, _exprEmitter.Emit(param.Default));
                    IL.Emit(OpCodes.Stelem_Ref);
                    defIdxForDec++;
                }
            }
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CreateFunctionWithDefaults_Method);

            // Set __name__ on the NajaFunction
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldstr, "__name__");
            IL.Emit(OpCodes.Ldstr, s.Name);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.SetAttr_Method);

            // Phase 3: apply decorators bottom-to-top
            for (int d = nonTrivialDecs.Count - 1; d >= 0; d--)
            {
                var tmp = _ctx.Locals.Declare($"__fval_{s.Name}_{d}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                IL.Emit(OpCodes.Ldloc, decLocals[d]);
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Newarr, typeof(object));
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Stelem_Ref);
                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CallCallable_Method);
            }

            if (_ctx.Fields.TryGetValue(s.Name, out var staticField))
            {
                IL.Emit(OpCodes.Stsfld, staticField);
            }
            else if (_ctx.CellParamOf.TryGetValue(s.Name, out var cellPName))
            {
                var tmp = _ctx.Locals.Declare($"__cpf_{s.Name}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                _ctx.TryEmitLoadParam(cellPName);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            else if (_ctx.CellLocals.TryGetValue(s.Name, out var cellLoc))
            {
                var tmp = _ctx.Locals.Declare($"__clf_{s.Name}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                IL.Emit(OpCodes.Ldloc, cellLoc);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            else
            {
                var funcLocal = _ctx.Locals.Declare(s.Name, typeof(object));
                IL.Emit(OpCodes.Stloc, funcLocal);
            }
            _ctx.Methods.Remove(s.Name);
        }
        else
        {
            // No non-trivial decorators: emit NajaFunction creation inline and store in
            // a local so subsequent references (NameEmitters section 3) load this same
            // instance rather than creating a new one each time (fixes BUG-A5).
            var typeArgsForFn = pts.Concat(new[] { typeof(object) }).ToArray();
            var delegateTypeForFn = System.Linq.Expressions.Expression.GetFuncType(typeArgsForFn);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Ldftn, mb);
            IL.Emit(OpCodes.Newobj, delegateTypeForFn.GetConstructors()[0]);
            var totalDefaultsForFn = capturedOuterParamNames.Count + capturedCellNames.Count
                + s.Params.Count(p => p.Default is not null);
            IL.Emit(OpCodes.Ldc_I4, totalDefaultsForFn);
            IL.Emit(OpCodes.Newarr, typeof(object));
            for (int ci = 0; ci < capturedOuterParamNames.Count; ci++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, ci);
                if (_ctx.Fields.TryGetValue(capturedOuterParamNames[ci], out var capFieldFn))
                    IL.Emit(OpCodes.Ldsfld, capFieldFn);
                else
                    IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            for (int ci = 0; ci < capturedCellNames.Count; ci++)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, capturedOuterParamNames.Count + ci);
                if (_ctx.CellLocals.TryGetValue(capturedCellNames[ci], out var cellLocFn))
                    IL.Emit(OpCodes.Ldloc, cellLocFn);
                else
                    IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            int defIdxFn = 0;
            foreach (var param in s.Params)
            {
                if (param.Default is not null)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, capturedOuterParamNames.Count + capturedCellNames.Count + defIdxFn);
                    TypeMapper.EmitBox(IL, _exprEmitter.Emit(param.Default));
                    IL.Emit(OpCodes.Stelem_Ref);
                    defIdxFn++;
                }
            }
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CreateFunctionWithDefaults_Method);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldstr, "__name__");
            IL.Emit(OpCodes.Ldstr, s.Name);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.SetAttr_Method);
            if (_ctx.Fields.TryGetValue(s.Name, out var staticFieldNoDec))
            {
                IL.Emit(OpCodes.Stsfld, staticFieldNoDec);
            }
            else if (_ctx.CellParamOf.TryGetValue(s.Name, out var cellPNameNoDec))
            {
                var tmp = _ctx.Locals.Declare($"__cpf_{s.Name}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                _ctx.TryEmitLoadParam(cellPNameNoDec);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            else if (_ctx.CellLocals.TryGetValue(s.Name, out var cellLocNoDec))
            {
                var tmp = _ctx.Locals.Declare($"__clf_{s.Name}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                IL.Emit(OpCodes.Ldloc, cellLocNoDec);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            else
            {
                var funcLocalFn = _ctx.Locals.Declare(s.Name, typeof(object));
                IL.Emit(OpCodes.Stloc, funcLocalFn);
            }
        }
    }

    // ── Class def ─────────────────────────────────────────────────────────────

    public void EmitClassDef(ClassDef s)
    {
        // Nested class definitions (inside function bodies) are pre-declared in Pass 1
        // using the unique IL name "{s.Name}_L{s.Line}". Here we register them under
        // the original Python name so runtime calls like `MyClass()` resolve correctly.
        var uniqueName = $"{s.Name}_L{s.Line}";
        if (_ctx.ClassTypes.TryGetValue(uniqueName, out var tb)
            && _ctx.ClassConstructors.TryGetValue(uniqueName, out var ctor))
        {
            _ctx.ClassTypes[s.Name] = tb;
            _ctx.ClassConstructors[s.Name] = ctor;

            if (_ctx.ClassCtorArgCounts.TryGetValue(uniqueName, out var argCount))
                _ctx.ClassCtorArgCounts[s.Name] = argCount;

            // Snapshot the current hoisted-field bindings for this nested class.
            // This allows Pass 3 to use the exact fields visible at the class definition
            // site instead of the coarse merged dict (which suffers TryAdd collisions when
            // multiple enclosing methods hoist different fields under the same variable name).
            _ctx.NestedClassFieldContexts[uniqueName] = new Dictionary<string, FieldBuilder>(_ctx.Fields);

            // Alias method stubs from unique-name prefix to original-name prefix
            foreach (var key in _ctx.AllClassMethods.Keys
                .Where(k => k.StartsWith(uniqueName + ".")).ToList())
            {
                var suffix = key[(uniqueName.Length + 1)..];
                var aliasKey = $"{s.Name}.{suffix}";
                if (!_ctx.AllClassMethods.ContainsKey(aliasKey))
                {
                    _ctx.AllClassMethods[aliasKey] = _ctx.AllClassMethods[key];
                    _ctx.AllClassMethodParamTypes[aliasKey] = _ctx.AllClassMethodParamTypes[key];
                    _ctx.ClassMethods.Add(aliasKey);
                }
            }
        }

        // Apply class decorators (evaluate top-to-bottom, apply bottom-to-top)
        if (s.Decorators.Count > 0)
        {
            var decLocals = new List<LocalBuilder>();
            for (int d = 0; d < s.Decorators.Count; d++)
            {
                var decLocal = _ctx.Locals.Declare($"__cdec_{s.Name}_{d}", typeof(object));
                TypeMapper.EmitBox(IL, _exprEmitter.Emit(s.Decorators[d]));
                IL.Emit(OpCodes.Stloc, decLocal);
                decLocals.Add(decLocal);
            }
            var typeName = tb?.Name ?? uniqueName;
            IL.Emit(OpCodes.Ldstr, typeName);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ResolveTypeByName_Method);
            for (int d = s.Decorators.Count - 1; d >= 0; d--)
            {
                var tmp = _ctx.Locals.Declare($"__cval_{s.Name}_{d}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                IL.Emit(OpCodes.Ldloc, decLocals[d]);
                IL.Emit(OpCodes.Ldc_I4_1);
                IL.Emit(OpCodes.Newarr, typeof(object));
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Stelem_Ref);
                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CallCallable_Method);
            }
            if (_ctx.Fields.TryGetValue(s.Name, out var staticField))
            {
                IL.Emit(OpCodes.Stsfld, staticField);
            }
            else if (_ctx.CellParamOf.TryGetValue(s.Name, out var cellPName))
            {
                var tmp = _ctx.Locals.Declare($"__cpc_{s.Name}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                _ctx.TryEmitLoadParam(cellPName);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            else if (_ctx.CellLocals.TryGetValue(s.Name, out var cellLoc))
            {
                var tmp = _ctx.Locals.Declare($"__clc_{s.Name}", typeof(object));
                IL.Emit(OpCodes.Stloc, tmp);
                IL.Emit(OpCodes.Ldloc, cellLoc);
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldloc, tmp);
                IL.Emit(OpCodes.Stelem_Ref);
            }
            else
            {
                var classLocal = _ctx.Locals.Declare(s.Name, typeof(object));
                IL.Emit(OpCodes.Stloc, classLocal);
            }
            
            _ctx.ClassTypes.Remove(s.Name);
            _ctx.ClassConstructors.Remove(s.Name);
        }
    }

    // ── Yield detection ───────────────────────────────────────────────────────

    /// <summary>
    /// Apply class decorators for a TOP-LEVEL class definition (Pass 2).
    /// Python applies decorators at class-creation time, which for top-level
    /// classes is module-execution order — Pass 2's Main() walk, NOT Pass 3.
    /// Pass 3 (EmitClassBody) only emits method bodies; decorator application
    /// never happens there, so top-level decorated classes silently kept their
    /// undecorated TypeBuilder. This mirrors the nested-class path in
    /// EmitClassDef: evaluate decorators, resolve the baked type by name,
    /// call them bottom-up, store the result where the class name resolves.
    /// </summary>
    public void EmitTopLevelClassDecorators(ClassDef s, System.Reflection.Emit.TypeBuilder? tb, string typeName)
    {
        if (s.Decorators.Count == 0) return;

        var decLocals = new List<LocalBuilder>();
        for (int d = 0; d < s.Decorators.Count; d++)
        {
            var decLocal = _ctx.Locals.Declare($"__cdec_{s.Name}_{d}", typeof(object));
            TypeMapper.EmitBox(IL, _exprEmitter.Emit(s.Decorators[d]));
            IL.Emit(OpCodes.Stloc, decLocal);
            decLocals.Add(decLocal);
        }
        IL.Emit(OpCodes.Ldstr, typeName);
        IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ResolveTypeByName_Method);
        for (int d = s.Decorators.Count - 1; d >= 0; d--)
        {
            var tmp = _ctx.Locals.Declare($"__cval_{s.Name}_{d}", typeof(object));
            IL.Emit(OpCodes.Stloc, tmp);
            IL.Emit(OpCodes.Ldloc, decLocals[d]);
            IL.Emit(OpCodes.Ldc_I4_1);
            IL.Emit(OpCodes.Newarr, typeof(object));
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Ldloc, tmp);
            IL.Emit(OpCodes.Stelem_Ref);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CallCallable_Method);
        }
        if (_ctx.Fields.TryGetValue(s.Name, out var staticField))
        {
            IL.Emit(OpCodes.Stsfld, staticField);
        }
        else
        {
            var classLocal = _ctx.Locals.Declare(s.Name, typeof(object));
            IL.Emit(OpCodes.Stloc, classLocal);
        }
    }

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

    private static HashSet<string> CollectNamesReferencedByNestedFunctions(IReadOnlyList<Statement> body)
    {
        var names = new HashSet<string>();
        foreach (var stmt in body)
        {
            if (stmt is FunctionDef fn)
            {
                // Only hoist names that are FREE in the nested function:
                // referenced but NOT locally assigned AND NOT a parameter of that function.
                // Names declared nonlocal are free vars (they reference the enclosing scope).
                var referenced = CollectReferencedNames(fn.Body);
                // Recursively collect names needed by doubly-nested functions that pass
                // through this function (multi-level closure capture).
                var transitivelyNeeded = CollectNamesReferencedByNestedFunctions(fn.Body);
                var locallyAssigned = CollectAssignedNames(fn.Body);
                var nonlocalNames = CollectNonlocalNames(fn.Body);
                foreach (var nl in nonlocalNames) locallyAssigned.Remove(nl);
                foreach (var p in fn.Params) locallyAssigned.Add(p.Name);
                foreach (var n in referenced.Union(transitivelyNeeded))
                    if (!locallyAssigned.Contains(n))
                        names.Add(n);
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

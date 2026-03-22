using System.Reflection.Emit;
using Naja.CodeGen.Builtins;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen.Emitters.Expressions;

/// <summary>
/// Emits IL opcodes for name resolution across all scopes and binding contexts.
/// Handles parameters, locals, fields, methods, classes, builtins, and imports.
/// </summary>
public sealed class NameEmitters : ExpressionEmitterBase
{
    private readonly ExpressionEmitter _mainEmitter;

    public NameEmitters(EmitContext ctx, ExpressionEmitter mainEmitter) : base(ctx)
    {
        _mainEmitter = mainEmitter;
    }

    public override NajaType Emit(Expression expr)
    {
        throw new NotImplementedException("Use EmitName directly");
    }

    /// <summary>
    /// Resolves a name through the following priority order:
    /// 0. self/cls in instance methods (ldarg.0)
    /// 1. Parameters (ldarg.1, ldarg.2, ...)
    /// 2. Static fields - BEFORE locals (hoisted closure variables take precedence)
    ///    2a. Scoped hoisted comprehension loop variables
    ///    2b. Scoped hoisted fields (for lambdas in comprehensions)
    ///    2c. General static fields (module variables)
    /// 3. Locals (after fields for correct precedence)
    /// 4. Methods (first-class function references)
    /// 4b. User-defined classes
    /// 5. Builtin constants (True, False, None)
    /// 5b. Builtin container types (list, dict, set, frozenset, tuple)
    /// 5c. Module builtins (__name__, __file__)
    /// 6. Exception types
    /// 6a. Namespace imports
    /// 6b. .NET imported types
    /// Throws CodeGenException if name cannot be resolved.
    /// </summary>
    public NajaType EmitName(NameExpr e)
    {
        // 0. 'self' / 'cls' in instance methods → ldarg.0
        if (_ctx.IsInstanceMethod && e.Name == (_ctx.SelfName ?? "self"))
        {
            IL.Emit(OpCodes.Ldarg_0);
            return NajaTypes.Unknown;
        }

        // 1. Check parameters first (ldarg.1, ldarg.2, ...)
        if (_ctx.TryEmitLoadParam(e.Name))
        {
            var sym = _ctx.Model.GetSymbol(e);
            return sym?.Type ?? NajaTypes.Unknown;
        }

        // 1b. Cell param: variable is stored in an object[] cell accessible via a named param
        // (inner function case: outer nonlocal var is passed as __cell_varname of type object[])
        if (_ctx.CellParamOf.TryGetValue(e.Name, out var cellParamName))
        {
            if (_ctx.TryEmitLoadParam(cellParamName))
            {
                IL.Emit(OpCodes.Ldc_I4_0);
                IL.Emit(OpCodes.Ldelem_Ref);
                return NajaTypes.Unknown;
            }
        }

        // 1c. Cell local: variable is stored in an object[] cell in this function's locals
        // (outer function case: per-call mutable cell allocated at method entry)
        if (_ctx.CellLocals.TryGetValue(e.Name, out var cellLocalLB))
        {
            IL.Emit(OpCodes.Ldloc, cellLocalLB);
            IL.Emit(OpCodes.Ldc_I4_0);
            IL.Emit(OpCodes.Ldelem_Ref);
            return NajaTypes.Unknown;
        }

        // 2. PRIORITY: Check static fields BEFORE locals (module-level variables and hoisted closure variables)
        // MUST be checked BEFORE locals so that nonlocal/hoisted variables take precedence.
        // This ensures that when a variable is promoted to a static field for closure semantics,
        // we load from that field, not from any accidental local copy.

        // 2a. Check for scoped hoisted comprehension loop variables (e.g., __hoisted_i_comp_1_9)
        if (!string.IsNullOrEmpty(_ctx.ComprehensionScopeId))
        {
            var scopedFieldName = $"__hoisted_{e.Name}_{_ctx.ComprehensionScopeId}";
            if (_ctx.Fields.TryGetValue(scopedFieldName, out var scopedField))
            {
                IL.Emit(OpCodes.Ldsfld, scopedField);
                return NajaTypes.Unknown;
            }
        }

        // 2b. Check for any scoped hoisted field with this name (for lambdas inside comprehensions
        // that reference comprehension loop variables). Since the scope ID might not be set in the lambda
        // context, search for any field matching __hoisted_{name}_comp_*
        var scopedMatch = _ctx.Fields.Keys.FirstOrDefault(k =>
            k.StartsWith($"__hoisted_{e.Name}_comp_"));
        if (scopedMatch != null)
        {
            IL.Emit(OpCodes.Ldsfld, _ctx.Fields[scopedMatch]);
            return NajaTypes.Unknown;
        }

        // 2c. Check static fields (module-level variables and hoisted closure variables)
        if (_ctx.Fields.TryGetValue(e.Name, out var field))
        {
            IL.Emit(OpCodes.Ldsfld, field);
            // Return the correct NajaType based on the field's actual CLR type
            if (field.FieldType == typeof(long)) return NajaTypes.Int;
            if (field.FieldType == typeof(double)) return NajaTypes.Float;
            if (field.FieldType == typeof(bool)) return NajaTypes.Bool;
            if (field.FieldType == typeof(string)) return NajaTypes.Str;
            if (field.FieldType == typeof(System.Collections.Generic.List<object>)) return new ListType(NajaTypes.Unknown);
            if (field.FieldType == typeof(System.Collections.Generic.Dictionary<object, object>)) return new DictType(NajaTypes.Unknown, NajaTypes.Unknown);
            if (field.FieldType == typeof(System.Collections.Generic.HashSet<object>)) return new SetType(NajaTypes.Unknown);
            if (field.FieldType == typeof(object[])) return new TupleType(System.Array.Empty<NajaType>());
            return NajaTypes.Unknown;
        }

        // 3. Check locals (after static fields, so hoisted variables take precedence)
        var local = _ctx.Locals.TryGet(e.Name);
        if (local is not null)
        {
            IL.Emit(OpCodes.Ldloc, local);

            // Python 3: 'as e' locals are deleted at the end of except blocks.
            // If the local holds the DeletedSentinel, raise NameError.
            if (_ctx.ExceptionHandlerVars.Contains(e.Name))
            {
                var okLabel = IL.DefineLabel();
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldsfld, NajaBuiltinsMethodCache.DeletedSentinel_Field);
                IL.Emit(OpCodes.Ceq);
                IL.Emit(OpCodes.Brfalse_S, okLabel);
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldstr, $"NameError: name '{e.Name}' is not defined");
                IL.Emit(OpCodes.Newobj, typeof(MissingFieldException).GetConstructor(new[] { typeof(string) })!);
                IL.Emit(OpCodes.Throw);
                IL.MarkLabel(okLabel);
            }

            // If the local's CLR type is `object`, treat as Unknown to avoid
            // incorrect boxing (e.g. for-loop variables stored as object).
            if (local.LocalType == typeof(object))
                return NajaTypes.Unknown;
            // Use the local's CLR type as ground truth. The semantic model may report
            // Unknown for variables reassigned from dynamic operations (e.g. total = total + value),
            // which would suppress EmitBox and leave a raw long/double/bool on the IL stack
            // where object is expected — causing InvalidProgramException at JIT time.
            return TypeMapper.FromClrType(local.LocalType);
        }

        // 4. Check Methods (first-class function references -- H5)
        if (_ctx.Methods.TryGetValue(e.Name, out var method))
        {
            var pts = _ctx.MethodParamTypes.TryGetValue(e.Name, out var t) ? t : System.Array.Empty<Type>();

            // Check if this function has parameter defaults or captures
            _ctx.FunctionCapturedParams.TryGetValue(e.Name, out var capturedParams);
            _ctx.FunctionCapturedCells.TryGetValue(e.Name, out var capturedCells);
            _ctx.FunctionDefs.TryGetValue(e.Name, out var functionDef);
            capturedParams ??= new List<string>();
            capturedCells ??= new List<string>();

            // Count parameters with default values
            int defaultParamCount = 0;
            if (functionDef is not null)
            {
                defaultParamCount = functionDef.Params.Count(p => p.Default is not null);
            }

            // If this function has captures OR defaults, wrap in NajaFunction
            if (capturedParams.Count > 0 || capturedCells.Count > 0 || defaultParamCount > 0)
            {
                var totalDefaults = capturedParams.Count + capturedCells.Count + defaultParamCount;
                var typeArgs = pts.Concat(new[] { typeof(object) }).ToArray();
                var delegateType = System.Linq.Expressions.Expression.GetFuncType(typeArgs);
                var ctor = delegateType.GetConstructors()[0];
                IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Ldftn, method);
                IL.Emit(OpCodes.Newobj, ctor);

                // Build defaults array: captured param values first, then cell references, then default param values
                IL.Emit(OpCodes.Ldc_I4, totalDefaults);
                IL.Emit(OpCodes.Newarr, typeof(object));

                // 1. Captured outer params
                for (int ci = 0; ci < capturedParams.Count; ci++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, ci);
                    if (_ctx.Fields.TryGetValue(capturedParams[ci], out var capField))
                        IL.Emit(OpCodes.Ldsfld, capField);
                    else
                        IL.Emit(OpCodes.Ldnull);
                    IL.Emit(OpCodes.Stelem_Ref);
                }

                // 2. Captured cell references
                for (int ci = 0; ci < capturedCells.Count; ci++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, capturedParams.Count + ci);
                    if (_ctx.CellLocals.TryGetValue(capturedCells[ci], out var cellLoc))
                        IL.Emit(OpCodes.Ldloc, cellLoc);
                    else if (_ctx.CellParamOf.TryGetValue(capturedCells[ci], out var cParamName)
                             && _ctx.TryEmitLoadParam(cParamName))
                    { /* param already emitted by TryEmitLoadParam */ }
                    else
                        IL.Emit(OpCodes.Ldnull);
                    IL.Emit(OpCodes.Stelem_Ref);
                }

                // 3. Function parameter defaults
                if (functionDef is not null)
                {
                    int defIdx = 0;
                    foreach (var param in functionDef.Params)
                    {
                        if (param.Default is not null)
                        {
                            IL.Emit(OpCodes.Dup);
                            IL.Emit(OpCodes.Ldc_I4, capturedParams.Count + capturedCells.Count + defIdx);
                            // Emit the default expression value
                            var defaultType = _mainEmitter.Emit(param.Default);
                            TypeMapper.EmitBox(IL, defaultType);
                            IL.Emit(OpCodes.Stelem_Ref);
                            defIdx++;
                        }
                    }
                }

                IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.CreateFunctionWithDefaults_Method);
                return NajaTypes.Unknown;
            }

            var typeArgs2 = pts.Concat(new[] { method.ReturnType == typeof(void) ? typeof(object) : method.ReturnType }).ToArray();
            var delegateType2 = System.Linq.Expressions.Expression.GetFuncType(typeArgs2);
            var ctor2 = delegateType2.GetConstructors()[0];
            IL.Emit(OpCodes.Ldnull);        // static method target
            IL.Emit(OpCodes.Ldftn, method);
            IL.Emit(OpCodes.Newobj, ctor2);
            return NajaTypes.Unknown;
        }

        // 4b. User-defined class used as a value (e.g. isinstance(err, AppError)).
        // Use runtime type resolution instead of Ldtoken, which fails on unfinished TypeBuilders.
        if (_ctx.ClassTypes.ContainsKey(e.Name))
        {
            IL.Emit(OpCodes.Ldstr, e.Name);
            IL.Emit(OpCodes.Call, NajaBuiltinsMethodCache.ResolveTypeByName_Method);
            return NajaTypes.Unknown;
        }

        // 5. Builtin constants

        switch (e.Name)
        {
            case "True": IL.Emit(OpCodes.Ldc_I4_1); return NajaTypes.Bool;
            case "False": IL.Emit(OpCodes.Ldc_I4_0); return NajaTypes.Bool;
            case "None": IL.Emit(OpCodes.Ldnull); return NajaTypes.None;
        }

        // 5b. Builtin type names used as values (e.g. isinstance(x, int), or pattern matching with `case int:`)
        // Handle both primitive types (int, float, str, bool, bytes) and container types (list, dict, set, tuple, frozenset)
        Type? builtinType = e.Name switch
        {
            "int" => typeof(long),
            "float" => typeof(double),
            "str" => typeof(string),
            "bool" => typeof(bool),
            "bytes" => typeof(byte[]),
            "list" => typeof(System.Collections.Generic.List<object>),
            "dict" => typeof(System.Collections.Generic.Dictionary<object, object>),
            "set" => typeof(System.Collections.Generic.HashSet<object>),
            "frozenset" => typeof(System.Collections.Immutable.ImmutableHashSet<object>),
            "tuple" => typeof(object[]),
            _ => null
        };
        if (builtinType is not null)
        {
            IL.Emit(OpCodes.Ldtoken, builtinType);
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            return NajaTypes.Unknown;
        }

        // 5c. Module builtins when not in Fields (ensures CLI "run" and all paths work)
        if (e.Name == "__name__") { IL.Emit(OpCodes.Ldstr, "__main__"); return NajaTypes.Str; }
        if (e.Name == "__file__") { IL.Emit(OpCodes.Ldstr, ""); return NajaTypes.Str; }

        // Fix 6: Exception names used as values (e.g. raise StopIteration, except ValueError)
        var exType = TypeMapper.ResolveExceptionType(e.Name);
        if (exType is not null)
        {
            IL.Emit(OpCodes.Ldtoken, exType);
            IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
            return NajaTypes.Unknown;
        }

        // 6a. Namespace imports: import System → System is a valid reference
        if (_ctx.NamespaceImports.ContainsKey(e.Name))
        {
            // Special case: Python 're' module returns a NajaReModule singleton instance
            if (e.Name == "re")
            {
                var reField = typeof(NajaReModule).GetField("Instance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
                IL.Emit(OpCodes.Ldsfld, reField);
                return NajaTypes.Unknown;
            }

            // Namespace used as a value (e.g., passed to a function) - return null
            // The actual type resolution happens in EmitAttribute when accessing System.DateTime
            IL.Emit(OpCodes.Ldnull);
            return NajaTypes.Unknown;
        }

        // 6b. .NET imports: from System.X import Y → push the resolved Type object onto the stack.
        //    We search already-loaded assemblies first (avoids partial-AQN failures with
        //    strong-named WinForms / Drawing assemblies).
        if (_ctx.ImportMap.TryGetValue(e.Name, out var import))
        {
            // Try to resolve the type from currently-loaded assemblies.
            var resolvedType =
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(import.TypeName, throwOnError: false, ignoreCase: true))
                    .FirstOrDefault(t => t is not null);

            // If not found in loaded assemblies, try Type.GetType with assembly name (if provided)
            if (resolvedType is null && !string.IsNullOrEmpty(import.AssemblyName))
            {
                resolvedType = Type.GetType(import.TypeName + ", " + import.AssemblyName, throwOnError: false, ignoreCase: true);
            }

            // Last resort: try just the type name without assembly
            resolvedType ??= Type.GetType(import.TypeName, throwOnError: false, ignoreCase: true);

            if (resolvedType is not null)
            {
                // Push the Type constant directly — no runtime GetType call needed.
                IL.Emit(OpCodes.Ldtoken, resolvedType);
                var getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle")!;
                IL.Emit(OpCodes.Call, getTypeFromHandle);
            }
            else
            {
                // Fallback: runtime Type.GetType with the type name only (no assembly).
                // This allows ASP.NET Core and other framework types to be resolved at runtime
                // when the correct assembly is loaded via the runtimeconfig.json framework.
                IL.Emit(OpCodes.Ldstr, import.TypeName);
                var getType = typeof(Type).GetMethod("GetType", new[] { typeof(string) })!;
                IL.Emit(OpCodes.Call, getType);
            }
            return NajaTypes.Unknown;
        }

        throw new CodeGenException($"Undefined name '{e.Name}'", e.Line, e.Column);
    }
}

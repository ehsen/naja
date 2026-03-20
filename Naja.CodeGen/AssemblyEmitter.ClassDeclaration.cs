using Naja.CodeGen;
using Naja.Parser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Naja.CodeGen;

/// <summary>
/// Partial class for Pass 1 class declaration logic: TypeBuilder creation and default constructor stub.
/// This handles the creation of class type stubs before methods are known, enabling forward references.
/// </summary>
public sealed partial class AssemblyEmitter
{
    // ── Class declaration stub ────────────────────────────────────────────────

    private TypeBuilder DeclareClass(
        ClassDef cls,
        ModuleBuilder modBuilder,
        Dictionary<string, (string TypeName, string AssemblyName)> importMap,
        IReadOnlyList<Statement> moduleBody,
        out ConstructorBuilder defaultCtor)
    {
        // Resolve base class — search loaded assemblies first to handle strong-named
        // WinForms types correctly (Type.GetType with AQN is unreliable for them).
        Type baseType = typeof(object);
        if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr baseExpr)
        {
            if (importMap.TryGetValue(baseExpr.Name, out var imp))
            {
                // Resolve from disk metadata first (Roslyn-style), then fall back
                // to AppDomain for types the host already has loaded (BCL etc.).
                baseType =
                    _typeResolver.ResolveType(imp.TypeName)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                           .Select(a => { try { return a.GetType(imp.TypeName, false, true); } catch { return null; } })
                           .FirstOrDefault(t => t is not null)
                    ?? Type.GetType($"{imp.TypeName}, {imp.AssemblyName}")
                    ?? Type.GetType(imp.TypeName)
                    ?? typeof(object);
            }
            else if (_classTypes.TryGetValue(baseExpr.Name, out var localBase))
            {
                baseType = localBase;
            }
            else
            {
                baseType = baseExpr.Name switch
                {
                    "Exception" or "BaseException" => typeof(Exception),
                    "ValueError" => typeof(ArgumentException),
                    "TypeError" => typeof(InvalidCastException),
                    "RuntimeError" => typeof(InvalidOperationException),
                    "NotImplementedError" => typeof(NotImplementedException),
                    _ => typeof(object)
                };
            }

        }

        // Check for @typing.final or @final decorator to make class sealed
        bool isFinal = cls.Decorators.Any(d =>
            d is NameExpr { Name: "final" }
            || (d is AttributeExpr a && a.Attribute == "final" && a.Object is NameExpr { Name: "typing" }));

        var typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (isFinal) typeAttrs |= TypeAttributes.Sealed;

        var tb = modBuilder.DefineType(
            cls.Name,
            typeAttrs,
            baseType);

        // ── Determine constructor parameter count ────────────────────────────
        var initFn = cls.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
        int argCount = 0;
        bool isProxy = false;

        if (initFn != null)
        {
            bool hasSelf = initFn.Params.Count > 0
                        && initFn.Params[0].Name is "self" or "cls";
            argCount = hasSelf ? initFn.Params.Count - 1 : initFn.Params.Count;
        }
        else if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr bExpr)
        {
            // Inherit signature from Naja base class if the class has NO __init__
            var baseClsDef = moduleBody.OfType<ClassDef>().FirstOrDefault(c => c.Name == bExpr.Name);
            if (baseClsDef != null)
            {
                var baseInit = baseClsDef.Body.OfType<FunctionDef>().FirstOrDefault(f => f.Name == "__init__");
                if (baseInit != null)
                {
                    bool hasSelf = baseInit.Params.Count > 0 && baseInit.Params[0].Name is "self" or "cls";
                    argCount = hasSelf ? baseInit.Params.Count - 1 : baseInit.Params.Count;
                    isProxy = true;
                    initFn = baseInit; // Use base init for parameter names
                }
            }
        }

        var ctorParams = Enumerable.Repeat(typeof(object), argCount).ToArray();
        defaultCtor = tb.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            ctorParams);


        // Name parameters for debuggability
        if (initFn != null)
        {
            bool hasSelf = initFn.Params.Count > 0 && initFn.Params[0].Name is "self" or "cls";
            int skip = hasSelf ? 1 : 0;
            for (int i = 0; i < argCount; i++)
                defaultCtor.DefineParameter(i + 1, ParameterAttributes.None,
                    initFn.Params[i + skip].Name);
        }

        var ctorIL = defaultCtor.GetILGenerator();

        // ── Call base constructor ────────────────────────────────────────────
        // We must push the correct number of arguments to the base constructor
        // to avoid InvalidProgramException (stack imbalance).

        ConstructorInfo? baseCtor = null;
        int baseParamCount = 0;
        bool foundNajaBase = false;

        if (cls.Bases.Count > 0 && cls.Bases[0] is NameExpr bExprResolve && _classConstructors.TryGetValue(bExprResolve.Name, out var bc))
        {
            baseCtor = bc;
            _classCtorArgCounts.TryGetValue(bExprResolve.Name, out baseParamCount);
            foundNajaBase = true;
        }

        if (isProxy && baseCtor == null)
        {
            // Proxy: call base constructor with EXACTLY the same signature
            baseCtor = baseType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance, null, ctorParams, null);
            if (baseCtor != null) baseParamCount = argCount;
        }

        // Fallback or non-proxy: find any accessible constructor
        if (baseCtor == null)
        {
            var baseCtors = baseType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            baseCtor = baseCtors.FirstOrDefault(c => c.GetParameters().Length == 0)
                    ?? baseCtors.FirstOrDefault()
                    ?? typeof(object).GetConstructor(Type.EmptyTypes)!;
            baseParamCount = baseCtor.GetParameters().Length;
        }

        ctorIL.Emit(OpCodes.Ldarg_0); // this
        if (isProxy && foundNajaBase && baseParamCount == argCount)
        {
            // Pass through all arguments
            for (int i = 0; i < argCount; i++)
            {
                int argIdx = i + 1;
                if (argIdx == 1) ctorIL.Emit(OpCodes.Ldarg_1);
                else if (argIdx == 2) ctorIL.Emit(OpCodes.Ldarg_2);
                else if (argIdx == 3) ctorIL.Emit(OpCodes.Ldarg_3);
                else ctorIL.Emit(OpCodes.Ldarg_S, (byte)argIdx);
            }
        }
        else
        {
            // Push nulls for any required arguments
            for (int i = 0; i < baseParamCount; i++)
                ctorIL.Emit(OpCodes.Ldnull);
        }

        ctorIL.Emit(OpCodes.Call, baseCtor);

        if (isProxy)
        {
            ctorIL.Emit(OpCodes.Ret);
        }
        else
        {
            // ── Deferred constructor completion ──────────────────────────────
            // At Pass 1 time the __init__ MethodBuilder does not exist yet —
            // it is declared in Pass 1.5.  Emitting Call on a MethodBuilder
            // that belongs to a type not yet created causes InvalidProgramException
            // with both AssemblyBuilder.Run and PersistedAssemblyBuilder.
            //
            // Solution: leave the ILGenerator open (no Ret here) and store it
            // so EmitClassBody (Pass 3) can complete the constructor after
            // __init__ is available. EmitClassBody calls CompleteConstructor().
            _pendingCtorIL[cls.Name] = (ctorIL, argCount);
            // Ret is emitted by CompleteConstructor — NOT here.
        }

        return tb;

    }

    // ── Deferred constructor completion ───────────────────────────────────────

    /// <summary>
    /// Completes the constructor body left open by DeclareClass (Pass 1).
    /// Called at the START of EmitClassBody (Pass 3) after Pass 1.5 has
    /// populated _classMethods with the __init__ MethodBuilder.
    ///
    /// Emits:
    ///   ldarg.0         (self)
    ///   ldarg.1 … N    (constructor parameters)
    ///   call __init__
    ///   [pop if non-void]
    ///   ret
    ///
    /// If no __init__ exists, emits ret only so the constructor is valid IL.
    /// </summary>
    private void CompleteConstructor(string className)
    {
        if (!_pendingCtorIL.TryGetValue(className, out var pending))
            return; // proxy ctor or already completed

        var (ctorIL, argCount) = pending;
        _pendingCtorIL.Remove(className);

        string key = $"{className}.__init__";
        if (_classMethods.TryGetValue(key, out var initMb))
        {
            ctorIL.Emit(OpCodes.Ldarg_0); // self
            for (int i = 0; i < argCount; i++)
            {
                int argIdx = i + 1;
                if (argIdx == 1) ctorIL.Emit(OpCodes.Ldarg_1);
                else if (argIdx == 2) ctorIL.Emit(OpCodes.Ldarg_2);
                else if (argIdx == 3) ctorIL.Emit(OpCodes.Ldarg_3);
                else ctorIL.Emit(OpCodes.Ldarg_S, (byte)argIdx);
            }
            ctorIL.Emit(OpCodes.Call, initMb);
            if (initMb.ReturnType != typeof(void))
                ctorIL.Emit(OpCodes.Pop);
        }

        ctorIL.Emit(OpCodes.Ret);
    }
}

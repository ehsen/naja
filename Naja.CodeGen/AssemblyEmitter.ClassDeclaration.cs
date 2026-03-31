using Naja.CodeGen;
using Naja.Parser;
using Naja.StdLib;
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
        out ConstructorBuilder defaultCtor,
        Dictionary<string, string>? namespaceImports = null,
        string? overrideName = null,
        Dictionary<string, TypeBuilder>? localClassTypes = null)
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
            else if (localClassTypes?.TryGetValue(baseExpr.Name, out var localClassBase) == true)
            {
                // Check local class types first (classes defined earlier in this module)
                baseType = localClassBase;
            }
            else if (_classTypes.TryGetValue(baseExpr.Name, out var moduleBase))
            {
                // Fall back to module-level class types from previous modules
                baseType = moduleBase;
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
        else if (cls.Bases.Count > 0 && cls.Bases[0] is AttributeExpr attrBase
            && attrBase.Object is NameExpr attrNsExpr)
        {
            // Attribute-qualified base class: e.g. unittest.TestCase
            // Check THREE places where the namespace could have been imported:
            // 1. importMap (for stdlib modules like "import unittest")
            // 2. namespaceImports (for "import System" style .NET namespace imports)
            // 3. moduleBody imports (fallback scan)

            bool nsWasImported = 
                importMap.ContainsKey(attrNsExpr.Name) ||
                (namespaceImports is not null && namespaceImports.ContainsKey(attrNsExpr.Name)) ||
                moduleBody.OfType<ImportStatement>()
                    .Any(s => s.Names.Any(a => (a.Alias ?? a.Name.Split('.')[0]) == attrNsExpr.Name));

            if (nsWasImported)
            {
                baseType = (attrNsExpr.Name, attrBase.Attribute) switch
                {
                    ("unittest", "TestCase") => typeof(NajaTestCase),
                    _ => AppDomain.CurrentDomain.GetAssemblies()
                             .Select(a => a.GetType(attrNsExpr.Name + "." + attrBase.Attribute, false, true))
                             .FirstOrDefault(t => t is not null) ?? typeof(object)
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
            overrideName ?? cls.Name,
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

        // Exception subclasses without __init__: auto-generate a 1-arg (message) constructor
        // that forwards the argument to the base exception constructor.
        bool autoExceptionCtor = false;
        if (initFn == null && argCount == 0 && !isProxy)
        {
            bool baseIsClrException = typeof(Exception).IsAssignableFrom(baseType) && baseType != typeof(object);
            string? baseName2 = cls.Bases.Count > 0 && cls.Bases[0] is NameExpr bex2 ? bex2.Name : null;
            bool baseNajaHasMessageCtor = baseName2 != null &&
                _classCtorArgCounts.TryGetValue(baseName2, out var bac2) && bac2 >= 1;
            if (baseIsClrException || baseNajaHasMessageCtor)
            {
                argCount = 1;
                autoExceptionCtor = true;
                _classCtorArgCounts[overrideName ?? cls.Name] = 1;
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

        // For auto-exception-ctor against a CLR base: prefer base(string message) ctor
        if (autoExceptionCtor && !foundNajaBase)
        {
            var stringCtor = baseType.GetConstructor(
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            if (stringCtor != null)
            {
                baseCtor = stringCtor;
                baseParamCount = 1;
            }
        }

        ctorIL.Emit(OpCodes.Ldarg_0); // this

        if (autoExceptionCtor)
        {
            // Exception subclass without __init__: forward message to base
            if (foundNajaBase && baseCtor != null)
            {
                ctorIL.Emit(OpCodes.Ldarg_1);
                ctorIL.Emit(OpCodes.Call, baseCtor);
            }
            else if (baseParamCount == 1 && baseCtor != null)
            {
                ctorIL.Emit(OpCodes.Ldarg_1);
                // Convert object → string when base ctor expects a string parameter.
                // Use Convert.ToString() instead of callvirt ToString() so that null
                // arguments (passed by sub-class constructor stubs) don't throw NRE.
                if (baseCtor.GetParameters()[0].ParameterType == typeof(string))
                    ctorIL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToString", new[] { typeof(object) })!);
                ctorIL.Emit(OpCodes.Call, baseCtor);
            }
            else
            {
                ctorIL.Emit(OpCodes.Call, baseCtor!);
            }
            ctorIL.Emit(OpCodes.Ret);
        }
        else if (isProxy && foundNajaBase && baseParamCount == argCount)
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
            ctorIL.Emit(OpCodes.Call, baseCtor);
            ctorIL.Emit(OpCodes.Ret);
        }
        else
        {
            // Push nulls for any required arguments
            for (int i = 0; i < baseParamCount; i++)
                ctorIL.Emit(OpCodes.Ldnull);
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
                _pendingCtorIL[overrideName ?? cls.Name] = (ctorIL, argCount);
                // Ret is emitted by CompleteConstructor — NOT here.
            }
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

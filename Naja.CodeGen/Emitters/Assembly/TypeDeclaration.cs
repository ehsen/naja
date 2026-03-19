using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using Naja.Parser;
using Naja.Semantics;

namespace Naja.CodeGen
{
    internal static class TypeDeclaration
    {
        public static (MethodBuilder mb, Type[] paramTypes) DeclareMethod(SemanticModel model, FunctionDef fn, TypeBuilder tb)
        {
            if (fn.IsAsync)
                throw new CodeGenException("async/await is not yet supported.", fn.Line, fn.Column);

            var sym = model.ModuleScope.Lookup(fn.Name);
            var fnType = sym?.Type as FunctionType;
            var retType = fnType is not null
                ? TypeMapper.ToReturnType(fnType.ReturnType)
                : typeof(object);

            var pts = fn.Params.Select((p, i) =>
            {
                var pType = fnType?.ParamTypes.ElementAtOrDefault(i);
                var clr = pType is not null ? TypeMapper.ToClrType(pType) : typeof(object);
                return clr == typeof(void) ? typeof(object) : clr;
            }).ToArray();

            var mb = tb.DefineMethod(
                fn.Name,
                MethodAttributes.Public | MethodAttributes.Static,
                retType,
                pts);

            for (int i = 0; i < fn.Params.Count; i++)
                mb.DefineParameter(i + 1, ParameterAttributes.None, fn.Params[i].Name);

            return (mb, pts);
        }

        public static (MethodBuilder mb, Type[] paramTypes, string uniqueName) DeclareInstanceMethod(FunctionDef fn, TypeBuilder ct)
        {
            if (fn.IsAsync)
                throw new CodeGenException("async/await is not yet supported.", fn.Line, fn.Column);

            bool hasSelf = fn.Params.Count > 0 && fn.Params[0].Name is "self" or "cls";
            var clrParams = fn.Params
                .Skip(hasSelf ? 1 : 0)
                .Select(_ => typeof(object))
                .ToArray();

            bool isStatic = fn.Decorators.Any(d => d is NameExpr { Name: "staticmethod" or "classmethod" });
            bool isFinal = fn.Decorators.Any(d =>
                d is NameExpr { Name: "final" }
                || (d is AttributeExpr a && a.Attribute == "final" && a.Object is NameExpr { Name: "typing" }));
            bool isProp = fn.Decorators.Any(d => d is NameExpr { Name: "property" });
            bool isSetter = fn.Decorators.Any(d => d is AttributeExpr a2 && a2.Attribute == "setter");

            string uniqueName = isProp ? "get_" + fn.Name
                              : isSetter ? "set_" + fn.Name
                              : fn.Name;
            string emitName = uniqueName;

            bool isPriv = fn.Name.StartsWith('_')
                       && !(fn.Name.StartsWith("__") && fn.Name.EndsWith("__"));

            var attrs = isPriv ? MethodAttributes.Private : MethodAttributes.Public;

            if (isStatic)
            {
                attrs |= MethodAttributes.Static;
            }
            else
            {
                attrs |= MethodAttributes.Virtual | MethodAttributes.HideBySig;

                // Avoid reflection on TypeBuilder base
                MethodInfo? baseMethod = null;
                if (ct.BaseType != null && ct.BaseType is not TypeBuilder)
                {
                    baseMethod = ct.BaseType.GetMethod(
                        emitName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        null, clrParams, null);
                }

                if (baseMethod == null || !baseMethod.IsVirtual || baseMethod.IsFinal)
                    attrs |= MethodAttributes.NewSlot;
            }


            if (isFinal && !isStatic) attrs |= MethodAttributes.Final;
            if (isProp || isSetter) attrs |= MethodAttributes.SpecialName;

            var mb = ct.DefineMethod(emitName, attrs, typeof(object), clrParams);

            if (!isStatic)
                for (int i = 0; i < clrParams.Length; i++)
                    mb.DefineParameter(i + 1, ParameterAttributes.None,
                        fn.Params[i + (hasSelf ? 1 : 0)].Name);

            return (mb, clrParams, uniqueName);
        }
    }
}

using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Reflection and dynamic call utilities for .NET object interop.
/// Handles dynamic method calls, attribute access, and event subscriptions on .NET objects from Python code.
/// </summary>
public static class ReflectionHelpers
{
    // Dynamic attribute table for Type objects (e.g. decorated classes: C.extra = 'Hello').
    // ConditionalWeakTable does not prevent GC of the key Type, keeping memory clean.
    private static readonly ConditionalWeakTable<Type, Dictionary<string, object?>> _typeAttrs = new();

    // Delegate cache: ensures the same NajaFunction always produces the same delegate instance
    // for a given event handler type, so that -= can find and remove the correct delegate.
    private static readonly ConditionalWeakTable<NajaFunction, Dictionary<Type, Delegate>> _eventDelegateCache = new();

    // Secondary cache keyed on (underlying MethodInfo, handlerType): handles the case where
    // NameEmitters creates a fresh NajaFunction wrapper on each reference to a module-level
    // function name, so the same logical handler always yields the same compiled delegate.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(MethodInfo, Type), Delegate>
        _eventDelegateByMethodCache = new();
    /// <summary>Get a static attribute (property, field, or enum value) from a .NET type.</summary>
    public static object? GetStaticAttr(Type type, string name)
    {
        if (type is null) return null;

        // 1. Static property (Color.White, SystemInformation.WorkingArea, etc.)
        var prop = type.GetProperty(name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (prop?.GetGetMethod() is { } getter)
            return getter.Invoke(null, null);

        // 2. Enum member by name — must come before GetField because enum
        //    literal fields have FieldAttributes.Literal and require special handling
        if (type.IsEnum)
        {
            try { return Enum.Parse(type, name, ignoreCase: true); }
            catch { /* fall through */ }
        }

        // 3. Static field (non-enum, e.g. Color.Empty, IntPtr.Zero)
        FieldInfo? field = null;
        try
        {
            field = type.GetField(name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        }
        catch (AmbiguousMatchException)
        {
            // Multiple fields with same name in hierarchy - use first match
            field = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        if (field is not null)
        {
            var fieldValue = field.GetValue(null);

            // BUG-A9: Handle descriptor protocol for staticmethod/classmethod accessed via class
            if (fieldValue is NajaStaticMethod staticMethod)
                return staticMethod.__func__;
            if (fieldValue is NajaClassMethod classMethod)
                return classMethod.__func__;

            return fieldValue;
        }

        // Return null instead of throwing - caller should handle missing fields
        return null;
    }

    /// <summary>Check if an object has an attribute (property, field, or method).</summary>
    public static bool HasAttr(object obj, object name)
    {
        var nameStr = name?.ToString() ?? "";
        var t = obj?.GetType();
        if (t is null) return false;
        return t.GetProperty(nameStr) is not null ||
               t.GetField(nameStr) is not null ||
               t.GetMethod(nameStr) is not null;
    }

    /// <summary>Get an instance attribute (property or field) from a .NET object.</summary>
    public static object? GetAttr(object obj, string name)
    {
        if (obj is null) return null;

        // Python type dunder attributes + class-level attribute access (Foo.x)
        if (obj is Type typeObj)
        {
            if (name == "__name__" || name == "__qualname__") return typeObj.Name;
            if (name == "__module__") return typeObj.Namespace ?? "";

            // Class-level attribute access: Foo.x → static field/property on the type
            var classFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            try
            {
                var sProp = typeObj.GetProperty(name, classFlags);
                if (sProp is not null) return sProp.GetValue(null);
            }
            catch { }
            try
            {
                var sFld = typeObj.GetField(name, classFlags);
                if (sFld is not null) return sFld.GetValue(null);
            }
            catch { }

            // Static methods: wrap in a delegate so they can be called
            try
            {
                var sMethod = typeObj.GetMethod(name, classFlags);
                if (sMethod is not null)
                {
                    // Build a Func<object[], object> delegate for the static method
                    var paramCount = sMethod.GetParameters().Length;
                    var paramTypes = Enumerable.Repeat(typeof(object), paramCount)
                        .Concat(new[] { typeof(object) }).ToArray();
                    var delegateType = System.Linq.Expressions.Expression.GetFuncType(paramTypes);
                    return Delegate.CreateDelegate(delegateType, sMethod);
                }
            }
            catch { }

            // Instance methods accessed via Type: create a wrapper that expects 'self' as first arg
            // This enables patterns like: bar = classmethod(A().foo)
            var instanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            try
            {
                var iMethod = typeObj.GetMethod(name, instanceFlags);
                if (iMethod is not null)
                {
                    // Return a NajaFunction that wraps the unbound method
                    var paramCount = iMethod.GetParameters().Length + 1; // +1 for self
                    var paramTypes = Enumerable.Repeat(typeof(object), paramCount)
                        .Concat(new[] { typeof(object) }).ToArray();
                    var delegateType = System.Linq.Expressions.Expression.GetFuncType(paramTypes);
                    
                    // Create a wrapper that takes (self, ...args) and invokes iMethod
                    var wrapper = new Func<object[], object?>(args =>
                    {
                        if (args.Length == 0) throw new Exception($"TypeError: {name}() missing 1 required positional argument: 'self'");
                        var self = args[0];
                        var methodArgs = args.Skip(1).ToArray();
                        return iMethod.Invoke(self, methodArgs);
                    });
                    return new NajaFunction(wrapper, Array.Empty<object>());
                }
            }
            catch { }

            // Dynamic attributes set via SetAttr (e.g. decorated class: C.extra = 'Hello')
            if (_typeAttrs.TryGetValue(typeObj, out var typeAttrDict)
                && typeAttrDict.TryGetValue(name, out var typeAttrVal))
                return typeAttrVal;

            throw new Exception($"AttributeError: type '{typeObj.Name}' has no attribute '{name}'");
        }

        // NajaFunction: check __dict__ for dynamically set attributes after reflection fails
        if (obj is NajaFunction najaFunc)
        {
            var t0 = typeof(NajaFunction);
            var flags0 = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var prop0 = t0.GetProperty(name, flags0);
            if (prop0 is not null) return prop0.GetValue(najaFunc);
            var fld0 = t0.GetField(name, flags0);
            if (fld0 is not null) return fld0.GetValue(najaFunc);
            // Fall through to __dict__ for dynamic attributes (e.g. func.author, func.dbval)
            if (najaFunc.__dict__.TryGetValue(name, out var dictVal))
                return dictVal;
            throw new Exception($"AttributeError: 'function' object has no attribute '{name}'");
        }

        // BUG-A7: Descriptor protocol for @staticmethod and @classmethod
        // When accessing a staticmethod/classmethod via instance attribute access,
        // the descriptor should be unwrapped to the underlying function.
        if (obj is NajaStaticMethod staticMethodDesc)
        {
            // staticmethod accessed via instance: unwrap to __func__
            return staticMethodDesc.__func__;
        }

        if (obj is NajaClassMethod classMethodDesc)
        {
            // classmethod accessed via instance: return the bound method (not fully supported yet)
            // For now, just return __func__ like staticmethod
            return classMethodDesc.__func__;
        }

        // Raw delegate (Func<>, Action<>): return None for Python function magic attrs
        if (obj is Delegate del)
        {
            switch (name)
            {
                case "__name__": return del.Method?.Name;
                case "__qualname__": return del.Method?.Name;
                case "__module__": return null;
                case "__doc__": return null;
                case "__annotations__": return null;
                case "__dict__": return new Dictionary<string, object?>();
            }
        }

        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        PropertyInfo? prop = null;
        try { prop = t.GetProperty(name, flags); }
        catch (AmbiguousMatchException)
        {
            // WinForms heavily uses 'new' properties (like Form.Size hiding Control.Size).
            // Grab the first match which is always the most derived type.
            prop = t.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (prop is not null) return prop.GetValue(obj);

        FieldInfo? field = null;
        try { field = t.GetField(name, flags); }
        catch (AmbiguousMatchException)
        {
            field = t.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (field is not null)
        {
            var fieldValue = field.GetValue(obj);

            // BUG-A7: Handle descriptor protocol unwrapping
            // If accessing a staticmethod/classmethod through instance, unwrap it
            if (fieldValue is NajaStaticMethod staticMethod)
                return staticMethod.__func__;
            if (fieldValue is NajaClassMethod classMethod)
                return classMethod.__func__;

            return fieldValue;
        }

        // Python MRO fallback: class variables (static fields) accessible via instance
        FieldInfo? staticField = null;
        try
        {
            staticField = t.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        }
        catch { }
        if (staticField is not null)
        {
            var staticValue = staticField.GetValue(null);

            // BUG-A7: Handle descriptor protocol unwrapping for static fields accessed via instance
            if (staticValue is NajaStaticMethod staticMethod)
                return staticMethod.__func__;
            if (staticValue is NajaClassMethod classMethod)
                return classMethod.__func__;

            return staticValue;
        }

        // Try dict-like storage for Python objects
        if (obj is System.Collections.Generic.Dictionary<string, object> d &&
            d.TryGetValue(name, out var v)) return v;

        // Python exception chaining attributes (__cause__, __context__, __suppress_context__)
        var (chainFound, chainVal) = ExceptionHelpers.TryGetChainingAttr(obj, name);
        if (chainFound) return chainVal;

        // Method lookup: instance methods on compiled Python classes and .NET objects,
        // including inherited methods (e.g. `self.fail` on a class that inherits NajaTestCase).
        {
            var methods = t.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (methods.Length > 0)
            {
                var capturedObj = obj;
                var capturedMethods = methods;
                var wrapper = new Func<object[], object?>(args =>
                {
                    var m = capturedMethods.FirstOrDefault(x => x.GetParameters().Length == args.Length)
                         ?? capturedMethods[0];
                    try { return m.Invoke(capturedObj, args.Length == 0 ? null : (object?[])args); }
                    catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                        throw;
                    }
                });
                return new NajaFunction(wrapper, System.Array.Empty<object>());
            }
        }

        throw new Exception($"AttributeError: '{t.Name}' object has no attribute '{name}'");
    }

    /// <summary>Set an instance attribute (property or field) on a .NET object.</summary>
    public static void SetAttr(object obj, string name, object? value)
    {
        if (obj is null) return;

        // Dynamic attributes on Type objects (e.g. decorated class body: C.attr = value)
        if (obj is Type typeObj)
        {
            _typeAttrs.GetOrCreateValue(typeObj)[name] = value;
            return;
        }

        var t = obj.GetType();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        PropertyInfo? prop = null;
        try { prop = t.GetProperty(name, flags); }
        catch (AmbiguousMatchException)
        {
            prop = t.GetProperties(flags).FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (prop is not null && prop.CanWrite)
        {
            try { prop.SetValue(obj, TypeSystem.CoerceValue(value, prop.PropertyType)); }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            { throw new Exception($"AttributeError: setting '{t.Name}.{name}' failed: {tie.InnerException.Message}", tie.InnerException); }
            return;
        }

        FieldInfo? field = null;
        try { field = t.GetField(name, flags); }
        catch (AmbiguousMatchException)
        {
            field = t.GetFields(flags).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        if (field is not null)
        {
            try { field.SetValue(obj, TypeSystem.CoerceValue(value, field.FieldType)); }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            { throw new Exception($"AttributeError: setting '{t.Name}.{name}' failed: {tie.InnerException.Message}", tie.InnerException); }
            return;
        }

        // Fallback: store unknown attributes in NajaFunction.__dict__
        if (obj is NajaFunction najaFuncSet)
        {
            najaFuncSet.__dict__[name] = value;
            return;
        }

        throw new Exception($"AttributeError: '{t.Name}' object attribute '{name}' is read-only");
    }

    /// <summary>Get an item from a container using Python semantics (list[i], dict[key], etc.).</summary>
    public static object? GetItem(object obj, object key)
    {
        if (obj is null)
            throw new Exception($"TypeError: 'NoneType' object is not subscriptable");

        if (obj is string s)
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += s.Length;
            if (idx < 0 || idx >= s.Length) throw new Exception($"IndexError: string index out of range");
            return s[idx].ToString();
        }

        if (obj is System.Collections.Generic.List<object> l)
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += l.Count;
            if (idx < 0 || idx >= l.Count) throw new Exception($"IndexError: list index out of range");
            return l[idx];
        }

        if (obj is System.Collections.Generic.Dictionary<object, object> d)
            return d.TryGetValue(key, out var val) ? val : throw new Exception($"KeyError: {TypeConversion.Repr(key)}");

        if (obj is object[] arr)
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += arr.Length;
            if (idx < 0 || idx >= arr.Length) throw new Exception($"IndexError: tuple index out of range");
            return arr[idx];
        }

        // Support .NET IList collections (WinForms ControlCollection, ObjectCollection, etc.)
        // Only use integer indexing — string keys must fall through to the Item property indexer below
        // (e.g. DataGridViewCellCollection["Name"] uses the string-keyed overload, not IList[int]).
        if (obj is System.Collections.IList ilist && (key is long || key is int || key is short || key is byte))
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += ilist.Count;
            if (idx < 0 || idx >= ilist.Count) throw new Exception($"IndexError: list index out of range");
            return ilist[idx];
        }

        // Check for DefaultMember / Item property indexer — try all overloads in turn
        // so that types with both int and string indexers (e.g. DataGridViewCellCollection) work.
        var t2 = obj.GetType();
        var defaultMemberAttr = t2.GetCustomAttributes(typeof(System.Reflection.DefaultMemberAttribute), true)
                                  .OfType<System.Reflection.DefaultMemberAttribute>()
                                  .FirstOrDefault();
        string indexerName = defaultMemberAttr?.MemberName ?? "Item";
        var indexerCandidates = t2.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                  .Where(p => p.Name == indexerName && p.GetIndexParameters().Length == 1);
        foreach (var prop in indexerCandidates)
        {
            try
            {
                var convertedKey = TypeSystem.CoerceValue(key, prop.GetIndexParameters()[0].ParameterType);
                return prop.GetValue(obj, new object?[] { convertedKey });
            }
            catch (Exception ex) when (ex is not TargetInvocationException)
            {
                continue; // wrong overload — try next
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        var getitemMethod = obj.GetType().GetMethod("__getitem__",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (getitemMethod is not null)
        {
            try { return getitemMethod.Invoke(obj, new object[] { key }); }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        throw new Exception($"TypeError: '{obj.GetType().Name}' object is not subscriptable");
    }

    /// <summary>Set an item in a container using Python semantics (list[i] = x, dict[key] = x, etc.).</summary>
    public static void SetItem(object obj, object key, object? value)
    {
        if (obj is null) throw new Exception($"TypeError: 'NoneType' object does not support item assignment");

        if (obj is System.Collections.Generic.List<object> l)
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += l.Count;
            if (idx < 0 || idx >= l.Count) throw new Exception($"IndexError: list assignment index out of range");
            l[idx] = value;
            return;
        }

        if (obj is System.Collections.Generic.Dictionary<object, object> d)
        {
            d[key] = value;
            return;
        }

        // Support .NET IList collections — only for integer keys
        if (obj is System.Collections.IList ilist && !ilist.IsReadOnly && (key is long || key is int || key is short || key is byte))
        {
            int idx = SafeToInt32(key);
            if (idx < 0) idx += ilist.Count;
            if (idx < 0 || idx >= ilist.Count) throw new Exception($"IndexError: list assignment index out of range");
            ilist[idx] = value;
            return;
        }

        // Check for settable Item property indexer — try all overloads
        var t3 = obj.GetType();
        var defaultMemberAttr2 = t3.GetCustomAttributes(typeof(System.Reflection.DefaultMemberAttribute), true)
                                   .OfType<System.Reflection.DefaultMemberAttribute>()
                                   .FirstOrDefault();
        string indexerName2 = defaultMemberAttr2?.MemberName ?? "Item";
        var setIndexerCandidates = t3.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                     .Where(p => p.Name == indexerName2 && p.CanWrite && p.GetIndexParameters().Length == 1);
        foreach (var prop in setIndexerCandidates)
        {
            try
            {
                var convertedKey = TypeSystem.CoerceValue(key, prop.GetIndexParameters()[0].ParameterType);
                prop.SetValue(obj, value, new object?[] { convertedKey });
                return;
            }
            catch (Exception ex) when (ex is not TargetInvocationException)
            {
                continue;
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        var setitemMethod = obj.GetType().GetMethod("__setitem__",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (setitemMethod is not null)
        {
            try { setitemMethod.Invoke(obj, new object?[] { key, value }); return; }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        throw new Exception($"TypeError: '{obj.GetType().Name}' object does not support item assignment");
    }

    /// <summary>Call a method dynamically on a .NET object from Python code.</summary>
    public static object? DynamicCall(object obj, string method, object[] args)
    {
        if (obj is null) throw new Exception($"AttributeError: NoneType has no method '{method}'");

        // .NET static call: obj is System.Type → invoke static method
        if (obj is Type type)
        {
            // Uniform stdlib-module dispatch. Modules come in three shapes:
            //   1. singleton + instance methods (os, sys, math, ...) → has static Instance field
            //   2. static-only methods (tempfile, test.support, ...) → no Instance
            //   3. instance methods w/o singleton (shutil, textwrap) → parameterless ctor
            // The generated code may hand us the raw Type for ANY of these (function/class
            // scopes resolve imports to the Type). Dispatch consistently: try static first
            // (keeps .NET interop semantics for real static classes), then the singleton
            // instance, then a lazily-created instance — so every module shape behaves the
            // same no matter where in the source the call appears.
            var staticFlags0 = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase;
            var hasStatic = type.GetMethods(staticFlags0)
                .Any(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase));
            if (hasStatic)
                return StaticCall(type, method, args);

            if (type.GetField("Instance", BindingFlags.Public | BindingFlags.Static) is { } instField
                && instField.GetValue(null) is { } singleton)
            {
                obj = singleton;
            }
            else if (type.GetConstructor(Type.EmptyTypes) is { } defaultCtor
                     && type.Namespace is not null && type.Namespace.StartsWith("Naja.StdLib", StringComparison.Ordinal))
            {
                // Plain-instance stdlib module without a singleton — create one on demand.
                obj = defaultCtor.Invoke(null);
            }
            else
            {
                // Real .NET type (interop): report the static-method miss as before.
                return StaticCall(type, method, args);
            }
        }

        string? bridgeName = null;
        Type? bridgeClass = null;
        if (obj is string)                                                     { bridgeName = "Str"  + method; bridgeClass = typeof(StringFunctions); }
        else if (obj is System.Collections.Generic.List<object>)               { bridgeName = "List" + method; bridgeClass = typeof(Collections); }
        else if (obj is System.Collections.Generic.Dictionary<object, object>) { bridgeName = "Dict" + method; bridgeClass = typeof(Collections); }

        if (bridgeName is not null && bridgeClass is not null)
        {
            var bridgeM = bridgeClass.GetMethod(bridgeName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase);

            if (bridgeM is not null)
            {
                var pParams = bridgeM.GetParameters();
                var invokeArgs = new object[pParams.Length];
                invokeArgs[0] = obj;
                if (pParams.Length >= 2 && pParams[pParams.Length - 1].ParameterType == typeof(object[]))
                {
                    for (int i = 1; i < pParams.Length - 1; i++)
                        invokeArgs[i] = i - 1 < args.Length ? args[i - 1] : Type.Missing;
                    invokeArgs[pParams.Length - 1] = args.Length >= pParams.Length - 1
                        ? args.Skip(pParams.Length - 2).ToArray()
                        : System.Array.Empty<object>();
                }
                else
                {
                    for (int i = 1; i < invokeArgs.Length; i++)
                        invokeArgs[i] = Type.Missing;
                    for (int i = 0; i < args.Length && i + 1 < invokeArgs.Length; i++)
                        invokeArgs[i + 1] = args[i];
                }
                try
                {
                    return bridgeM.Invoke(null, invokeArgs);
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                    throw; // unreachable
                }
            }
        }

        var t = obj.GetType();
        var flags2 = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var candidates2 = t.GetMethods(flags2)
            .Where(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase))
            .Cast<MethodBase>();

        MethodBase mb;
        object?[] boundArgs;
        bool isStaticFallback = false;
        if (!TryBindBestCallable(candidates2, args, out mb, out boundArgs))
        {
            // Fall back to static methods (e.g. @staticmethod decorated methods in Python classes)
            var staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase;
            var staticCandidates = t.GetMethods(staticFlags)
                .Where(m => string.Equals(m.Name, method, StringComparison.OrdinalIgnoreCase))
                .Cast<MethodBase>();
            if (!TryBindBestCallable(staticCandidates, args, out mb, out boundArgs))
                throw new Exception($"AttributeError: '{t.Name}' object has no method '{method}' matching {args.Length} argument(s)");
            isStaticFallback = true;
        }

        var mi = (MethodInfo)mb;
        try { return mi.Invoke(isStaticFallback ? null : obj, boundArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Invoke a static method on a .NET type.</summary>
    public static object? StaticCall(Type type, string methodName, object[] args)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase;
        var candidates = type.GetMethods(flags)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase));

        if (!TryBindBestCallable(candidates, args, out var method, out var boundArgs))
        {
            // Also check static fields/properties for callable values:
            // e.g. bar = classmethod(fn) or bar = staticmethod(fn) stored as a static field.
            var fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var field = type.GetFields(fieldFlags)
                .FirstOrDefault(f => string.Equals(f.Name, methodName, StringComparison.OrdinalIgnoreCase));
            if (field is not null)
            {
                var fieldVal = field.GetValue(null);
                if (fieldVal is not null)
                    return CallCallable(fieldVal, args);
            }

            var prop = type.GetProperties(fieldFlags)
                .FirstOrDefault(p => string.Equals(p.Name, methodName, StringComparison.OrdinalIgnoreCase));
            if (prop?.GetGetMethod(nonPublic: true) is { } propGetter)
            {
                var propVal = propGetter.Invoke(null, null);
                if (propVal is not null)
                    return CallCallable(propVal, args);
            }

            throw new Exception($"AttributeError: type '{type.FullName}' has no static method '{methodName}' matching {args.Length} argument(s)");
        }

        try { return method.Invoke(null, boundArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    // ── Event handler utilities ──────────────────────────────────────────────────

    /// <summary>Subscribe a handler method to an event on a .NET object.</summary>
    public static void AddEventHandler(object target, string eventName, object? handlerTarget, string handlerMethodName)
    {
        if (target is null)
            throw new Exception($"EventError: target is null");

        var t = target.GetType();
        var evt = t.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (evt is null)
            throw new Exception($"EventError: type '{t.Name}' has no event '{eventName}'");

        var handlerType = evt.EventHandlerType!;
        var invoke = handlerType.GetMethod("Invoke")!;

        Delegate del;
        if (handlerTarget is Delegate d)
        {
            del = d;
        }
        else if (handlerTarget is NajaFunction najaFunc)
        {
            // Module-level function wrapped in NajaFunction — create a delegate that calls __call__
            del = CreateEventDelegateFromNajaFunction(handlerType, invoke, najaFunc);
        }
        else if (handlerTarget is null)
        {
            throw new Exception($"EventError: handler is null");
        }
        else
        {
            var ht = handlerTarget.GetType();
            var handlerMethod = ht.GetMethod(handlerMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (handlerMethod is null)
                throw new Exception($"EventError: method '{handlerMethodName}' not found on handler object");

            del = CreateEventDelegate(handlerType, invoke, handlerTarget, handlerMethod);
        }

        evt.AddEventHandler(target, del);
    }

    /// <summary>Unsubscribe a handler method from an event on a .NET object.</summary>
    public static void RemoveEventHandler(object target, string eventName, object? handlerTarget, string handlerMethodName)
    {
        if (target is null) return;
        var t = target.GetType();
        var evt = t.GetEvent(eventName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (evt is null) throw new Exception($"EventError: type '{t.Name}' has no event '{eventName}'");

        var handlerType = evt.EventHandlerType!;

        Delegate del;
        if (handlerTarget is Delegate d)
        {
            del = d;
        }
        else if (handlerTarget is NajaFunction najaFunc)
        {
            var invoke = handlerType.GetMethod("Invoke")!;
            del = CreateEventDelegateFromNajaFunction(handlerType, invoke, najaFunc);
        }
        else if (handlerTarget is null)
        {
            throw new Exception($"EventError: handler is null");
        }
        else
        {
            var ht = handlerTarget.GetType();
            var handlerMethod = ht.GetMethod(handlerMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (handlerMethod is null)
                throw new Exception($"EventError: method '{handlerMethodName}' not found on handler object");

            var handlerType2 = evt.EventHandlerType!;
            var invoke2 = handlerType2.GetMethod("Invoke")!;
            del = CreateEventDelegate(handlerType2, invoke2, handlerTarget, handlerMethod);
        }

        evt.RemoveEventHandler(target, del);
    }

    // ── Helper methods ───────────────────────────────────────────────────────────

    /// <summary>
    /// Create a delegate from a NajaFunction wrapper (module-level Python function).
    /// The result is cached so that the same logical handler always returns the same
    /// delegate instance, enabling correct unsubscription via -=.
    ///
    /// Two-level cache:
    ///  1. Instance cache (ConditionalWeakTable): covers lambdas / closures where the
    ///     NajaFunction IS the stable identity.
    ///  2. Method cache (ConcurrentDictionary): covers module-level functions where
    ///     NameEmitters creates a fresh NajaFunction wrapper on every reference but
    ///     the underlying compiled MethodInfo is always the same.
    /// </summary>
    public static Delegate CreateEventDelegateFromNajaFunction(Type handlerType, MethodInfo invoke, NajaFunction najaFunc)
    {
        // 1. Instance cache — fast path for lambdas and repeated references to same instance.
        var instanceCache = _eventDelegateCache.GetOrCreateValue(najaFunc);
        if (instanceCache.TryGetValue(handlerType, out var cached))
            return cached;

        // 2. Method cache — handles module-level functions that produce a new NajaFunction
        //    wrapper on each name-reference but always compile to the same MethodInfo.
        //    Only safe when there are no captured defaults; closures with different captured
        //    values compile to the same method but must not share a delegate.
        if (najaFunc.HasNoCaptures)
        {
            var methodKey = (najaFunc.UnderlyingDelegate.Method, handlerType);
            if (_eventDelegateByMethodCache.TryGetValue(methodKey, out var methodCached))
            {
                instanceCache[handlerType] = methodCached;
                return methodCached;
            }
        }

        var invokeParams = invoke.GetParameters();
        var paramExprs = invokeParams
            .Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name))
            .ToArray();

        // Build array of event arguments to pass to najaFunc.__call__
        var argsArray = System.Linq.Expressions.Expression.NewArrayInit(
            typeof(object),
            paramExprs.Select(p => System.Linq.Expressions.Expression.Convert(p, typeof(object))));

        // Call najaFunc.__call__(args)
        var callMethod = typeof(NajaFunction).GetMethod("__call__")!;
        var call = System.Linq.Expressions.Expression.Call(
            System.Linq.Expressions.Expression.Constant(najaFunc),
            callMethod,
            argsArray);

        var body = invoke.ReturnType == typeof(void)
            ? System.Linq.Expressions.Expression.Block(call, System.Linq.Expressions.Expression.Empty())
            : (System.Linq.Expressions.Expression)call;

        var del = System.Linq.Expressions.Expression.Lambda(handlerType, body, paramExprs).Compile();

        // Populate both caches.
        instanceCache[handlerType] = del;
        if (najaFunc.HasNoCaptures)
            _eventDelegateByMethodCache.TryAdd((najaFunc.UnderlyingDelegate.Method, handlerType), del);
        return del;
    }

    /// <summary>
    /// Create a delegate of <paramref name="handlerType"/> that calls <paramref name="handlerMethod"/>
    /// on <paramref name="handlerTarget"/>.  Falls back to a DynamicMethod adapter when
    /// <c>Delegate.CreateDelegate</c> fails (e.g. Naja methods return <c>object</c> but
    /// the event expects <c>void</c>).
    /// </summary>
    private static Delegate CreateEventDelegate(
        Type handlerType, MethodInfo invoke,
        object handlerTarget, MethodInfo handlerMethod)
    {
        try
        {
            return Delegate.CreateDelegate(handlerType, handlerTarget, handlerMethod);
        }
        catch (ArgumentException)
        {
            // Signature mismatch (e.g. event is void but Naja method returns object).
            // Build a DynamicMethod that boxes/unboxes and ignores return value as needed.
            var invokeParams = invoke.GetParameters();
            var dmParamTypes = new[] { typeof(object[]) }
                .Concat(invokeParams.Select(p => p.ParameterType))
                .ToArray();
            var dm = new System.Reflection.Emit.DynamicMethod(
                "_naja_ev_", invoke.ReturnType, dmParamTypes,
                typeof(ReflectionHelpers).Module, skipVisibility: true);
            var dil = dm.GetILGenerator();

            // Load target (capture[0]) and method (capture[1]), then call Invoke
            var miInvoke = typeof(MethodInfo).GetMethod("Invoke",
                new[] { typeof(object), typeof(object[]) })!;

            dil.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            dil.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_1);
            dil.Emit(System.Reflection.Emit.OpCodes.Ldelem_Ref);
            dil.Emit(System.Reflection.Emit.OpCodes.Castclass, typeof(MethodInfo));

            dil.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
            dil.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
            dil.Emit(System.Reflection.Emit.OpCodes.Ldelem_Ref);

            dil.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, invokeParams.Length);
            dil.Emit(System.Reflection.Emit.OpCodes.Newarr, typeof(object));
            for (int i = 0; i < invokeParams.Length; i++)
            {
                dil.Emit(System.Reflection.Emit.OpCodes.Dup);
                dil.Emit(System.Reflection.Emit.OpCodes.Ldc_I4, i);
                switch (i + 1)
                {
                    case 1: dil.Emit(System.Reflection.Emit.OpCodes.Ldarg_1); break;
                    case 2: dil.Emit(System.Reflection.Emit.OpCodes.Ldarg_2); break;
                    case 3: dil.Emit(System.Reflection.Emit.OpCodes.Ldarg_3); break;
                    default: dil.Emit(System.Reflection.Emit.OpCodes.Ldarg_S, (byte)(i + 1)); break;
                }
                if (invokeParams[i].ParameterType.IsValueType)
                    dil.Emit(System.Reflection.Emit.OpCodes.Box, invokeParams[i].ParameterType);
                dil.Emit(System.Reflection.Emit.OpCodes.Stelem_Ref);
            }
            dil.Emit(System.Reflection.Emit.OpCodes.Callvirt, miInvoke);
            if (invoke.ReturnType == typeof(void))
                dil.Emit(System.Reflection.Emit.OpCodes.Pop);
            dil.Emit(System.Reflection.Emit.OpCodes.Ret);

            var capture = new object[] { handlerTarget, handlerMethod };
            return dm.CreateDelegate(handlerType, capture);
        }
    }

    private static int SafeToInt32(object key)
    {
        if (key is int i) return i;
        if (key is long l) return (int)l;
        if (key is short s) return s;
        return Convert.ToInt32(key);
    }

    // ── Type checking and identity operations ────────────────────────────────

    /// <summary>Check if an object is an instance of a class or type.</summary>
    public static bool IsInstance(object obj, object classOrType)
    {
        if (classOrType is Type t)
        {
            // Check Python numeric hierarchy first: isinstance(1, float) should be True
            if (IsInstanceNumericHierarchy(obj, t))
                return true;
            return t.IsInstanceOfType(obj);
        }
        if (classOrType is NajaFunction)
            return obj is NajaFunction;
        return false;
    }

    /// <summary>Check Python numeric type hierarchy (e.g., isinstance(1, float) → True).</summary>
    private static bool IsInstanceNumericHierarchy(object obj, Type expectedType)
    {
        if (obj is null) return false;

        var actualType = obj.GetType();

        // Python: isinstance(x, float) accepts int, long, float, decimal
        if (expectedType == typeof(double) || expectedType == typeof(float))
        {
            return actualType == typeof(int) || actualType == typeof(long) || 
                   actualType == typeof(double) || actualType == typeof(float) ||
                   actualType == typeof(decimal);
        }

        // bool is a special case
        if (expectedType == typeof(bool))
        {
            return actualType == typeof(bool);
        }

        return false;
    }

    /// <summary>Check if a class is a subclass of another class or type.</summary>
    public static bool IsSubclass(object cls, object classOrType)
    {
        Type? t1 = cls as Type;
        Type? t2 = classOrType as Type;
        if (t1 is null || t2 is null) return false;
        return t2.IsAssignableFrom(t1);
    }

    /// <summary>Check if an object is callable (has __call__ method or is a delegate/function).</summary>
    public static bool Callable(object obj)
    {
        if (obj is null) return false;
        if (obj is Delegate or NajaFunction) return true;
        var t = obj.GetType();
        return t.GetMethod("__call__", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) is not null
            || typeof(Delegate).IsAssignableFrom(t);
    }

    /// <summary>Get the identity (memory address hash) of an object.</summary>
    public static long Id(object obj)
        => obj is null ? 0 : RuntimeHelpers.GetHashCode(obj);

    /// <summary>Get the hash code of an object.</summary>
    public static long Hash(object obj)
    {
        if (obj is null) return 0L;
        if (obj is int i) return (long)i;
        if (obj is long l) return (long)(int)l;
        if (obj is string s) return (long)s.GetHashCode();
        if (obj is double d) return (long)d.GetHashCode();
        if (obj is bool b) return b ? 1L : 0L;
        // object[] is a Naja tuple — hash structurally like Python
        if (obj is object[] arr)
        {
            unchecked
            {
                long h = 0x345678L;
                foreach (var item in arr)
                {
                    long ih = Hash(item);
                    h = (h ^ ih) * 1000003L;
                }
                h ^= arr.Length;
                if (h == -1) h = -2;
                return (long)(int)h;
            }
        }
        return (long)obj.GetHashCode();
    }

    /// <summary>Get the __dict__ of an object (its attributes as a dictionary).</summary>
    public static object Vars(object obj)
    {
        if (obj is null)
            throw new Exception("vars() argument must have __dict__ attribute");

        var d = new Dictionary<object, object>();
        var t = obj.GetType();
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        foreach (var prop in t.GetProperties(flags))
            if (prop.CanRead) d[prop.Name] = prop.GetValue(obj) ?? "";
        foreach (var field in t.GetFields(flags))
            d[field.Name] = field.GetValue(obj) ?? "";

        return d;
    }

    /// <summary>Get a list of attributes of an object.</summary>
    public static List<object> Dir(object obj)
    {
        if (obj is null)
            return new List<object>();

        var names = new HashSet<string>();
        var t = obj.GetType();
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        foreach (var prop in t.GetProperties(flags))
            names.Add(prop.Name);
        foreach (var field in t.GetFields(flags))
            names.Add(field.Name);
        foreach (var method in t.GetMethods(flags))
            names.Add(method.Name);

        return names.OrderBy(n => n).Cast<object>().ToList();
    }

    /// <summary>Get an iterator from an iterable object.</summary>
    public static object Iter(object obj)
    {
        if (obj is System.Collections.IEnumerable e)
            return e.GetEnumerator();
        throw new Exception($"TypeError: '{obj?.GetType().Name}' object is not iterable");
    }

    /// <summary>Call any Python callable: delegate, MethodInfo, or __call__ object.</summary>
    public static object? CallCallable(object? func, object[] args)
    {
        if (func is null)
            throw new Exception("TypeError: 'NoneType' object is not callable");

        if (func is Delegate d)
        {
            var ps = d.Method.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == typeof(object[]))
                return d.DynamicInvoke(new object[] { args });
            return d.DynamicInvoke(args.Length == 0 ? null : (object?[])args);
        }

        if (func is MethodInfo mi)
        {
            var ps = mi.GetParameters();
            var invokeArgs = new object?[ps.Length];
            for (int i = 0; i < Math.Min(args.Length, ps.Length); i++)
            {
                try { invokeArgs[i] = Convert.ChangeType(args[i],
                    Nullable.GetUnderlyingType(ps[i].ParameterType) ?? ps[i].ParameterType,
                    System.Globalization.CultureInfo.InvariantCulture); }
                catch { invokeArgs[i] = args[i]; }
            }
            return mi.Invoke(null, invokeArgs);
        }

        if (func is Type t)
            return TypeSystem.CreateDotNet(t, args);

        var callMethod = func.GetType().GetMethod("__call__");
        if (callMethod is not null)
        {
            var ps = callMethod.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == typeof(object[]))
                return callMethod.Invoke(func, new object?[] { args });
            return callMethod.Invoke(func, (object?[])args);
        }

        throw new Exception($"TypeError: '{func.GetType().Name}' object is not callable");
    }

    /// <summary>Check if an object contains a key or item (__contains__).</summary>
    public static bool Contains(object container, object item)
    {
        if (container is null) return false;
        if (container is string s)
            return s.Contains(item is string si ? si : NajaBuiltins.ToStr(item));
        if (container is System.Collections.Generic.List<object> l)
            return l.Any(x => ComparisonOperators.DynamicEq(x, item));
        if (container is System.Collections.Generic.Dictionary<object, object> d)
            return d.Keys.Any(k => ComparisonOperators.DynamicEq(k, item));
        if (container is System.Collections.Generic.HashSet<object> h)
            return h.Any(x => ComparisonOperators.DynamicEq(x, item));
        // Custom __contains__ dunder for user-defined types
        var cm = container.GetType().GetMethod("__contains__",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (cm is not null)
            return Convert.ToBoolean(cm.Invoke(container, new object[] { item }));
        if (container is System.Collections.IEnumerable e)
            return e.Cast<object>().Any(x => ComparisonOperators.DynamicEq(x, item));
        return false;
    }

    /// <summary>
    /// Best-effort method binding for dynamic calls with argument coercion.
    /// Tries to match the given arguments to the best overload of a callable.
    /// </summary>
    internal static bool TryBindBestCallable(
        IEnumerable<MethodBase> candidates,
        object[] args,
        out MethodBase method,
        out object[] boundArgs)
    {
        method = null!;
        boundArgs = null!;

        // Filter by parameter count — exclude params-array methods (handled by the dedicated loop below)
        var exact = candidates.Where(m => {
            var ps = m.GetParameters();
            return ps.Length == args.Length &&
                   (ps.Length == 0 || !ps[ps.Length - 1].IsDefined(typeof(ParamArrayAttribute), false));
        }).ToList();
        if (exact.Count > 0)
        {
            // Try to find one with perfect type match
            foreach (var mb in exact)
            {
                var ps = mb.GetParameters();
                var bound = new object?[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    bound[i] = args[i] is null && ps[i].ParameterType.IsValueType ? null : args[i];
                    if (bound[i] is not null && !ps[i].ParameterType.IsAssignableFrom(bound[i].GetType()))
                        ok = false;
                }
                if (ok)
                {
                    method = mb;
                    boundArgs = bound!;
                    return true;
                }
            }

            // Try each exact-count candidate with coercion + post-coercion assignability check
            foreach (var mb in exact)
            {
                var ps = mb.GetParameters();
                var bound = new object?[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    try
                    {
                        bound[i] = TypeSystem.CoerceValue(args[i], ps[i].ParameterType);
                        // Verify the coerced value is actually assignable (CoerceValue may return the
                        // original value unchanged for incompatible types instead of throwing)
                        if (bound[i] != null && !ps[i].ParameterType.IsAssignableFrom(bound[i]!.GetType()))
                        {
                            ok = false;
                            break;
                        }
                    }
                    catch
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    method = mb;
                    boundArgs = bound!;
                    return true;
                }
            }
        }

        // Try by count and type coercion (non-params overloads only)
        foreach (var mb in candidates)
        {
            var ps = mb.GetParameters();
            bool isParamsMb = ps.Length > 0 && ps[ps.Length - 1].IsDefined(typeof(ParamArrayAttribute), false);
            if (ps.Length != args.Length || isParamsMb) continue;

            var bound = new object?[ps.Length];
            bool ok = true;
            for (int i = 0; i < ps.Length; i++)
            {
                try
                {
                    bound[i] = TypeSystem.CoerceValue(args[i], ps[i].ParameterType);
                    if (bound[i] != null && !ps[i].ParameterType.IsAssignableFrom(bound[i]!.GetType()))
                    {
                        ok = false;
                        break;
                    }
                }
                catch
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                method = mb;
                boundArgs = bound;
                return true;
            }
        }

        // Try optional-parameter overloads (e.g., main(module=None, exit=None) called with 0 args).
        foreach (var mb in candidates)
        {
            var ps = mb.GetParameters();
            // Skip params-array overloads — the dedicated params-array loop below handles them.
            if (ps.Length > 0 && ps[ps.Length - 1].IsDefined(typeof(ParamArrayAttribute), false)) continue;
            // Must supply at least as many args as required (non-optional) params,
            // and no more than the total number of params.
            int requiredCount = ps.Count(p => !p.IsOptional && !p.HasDefaultValue);
            if (args.Length < requiredCount || args.Length > ps.Length) continue;

            var bound = new object?[ps.Length];
            bool ok = true;
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < args.Length)
                {
                    try { bound[i] = TypeSystem.CoerceValue(args[i], ps[i].ParameterType); }
                    catch { ok = false; break; }
                }
                else
                {
                    // Fill missing optional params with their declared defaults.
                    bound[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : Type.Missing;
                }
            }
            if (!ok) continue;
            method = mb;
            boundArgs = bound!;
            return true;
        }

        // Try params-array overloads (e.g., Path.Combine(string, string, string, string, string))
        foreach (var mb in candidates)
        {
            var ps = mb.GetParameters();
            if (ps.Length == 0) continue;
            var lastParam = ps[ps.Length - 1];
            if (!lastParam.IsDefined(typeof(ParamArrayAttribute), false)) continue;
            int fixedCount = ps.Length - 1;
            if (args.Length < fixedCount) continue;
            var elementType = lastParam.ParameterType.GetElementType()!;
            var bound = new object?[ps.Length];
            bool ok = true;
            for (int i = 0; i < fixedCount; i++)
            {
                try { bound[i] = TypeSystem.CoerceValue(args[i], ps[i].ParameterType); }
                catch { ok = false; break; }
            }
            if (!ok) continue;
            int paramsCount = args.Length - fixedCount;
            var paramsArr = Array.CreateInstance(elementType, paramsCount);
            for (int i = 0; i < paramsCount; i++)
            {
                try { paramsArr.SetValue(TypeSystem.CoerceValue(args[fixedCount + i], elementType), i); }
                catch { ok = false; break; }
            }
            if (!ok) continue;
            bound[fixedCount] = paramsArr;
            method = mb;
            boundArgs = bound!;
            return true;
        }

        return false;
    }
}

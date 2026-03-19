using System.Reflection;

namespace Naja.CodeGen.Builtins;

/// <summary>
/// Type resolution, coercion, and system utilities.
/// Handles type name resolution, value coercion for property assignment, and .NET object creation.
/// </summary>
public static class TypeSystem
{
    /// <summary>
    /// Coerce a value to the target type, always succeeding if any conversion exists.
    /// This is used when assigning values to .NET properties or fields from Python code.
    /// </summary>
    public static object? CoerceValue(object? value, Type targetType)
    {
        if (value is null) return null;
        if (targetType.IsAssignableFrom(value.GetType())) return value;
        if (TryConvertArg(value, targetType, false, out var converted, out _)) return converted;

        // Allow explicit coercion to string for attribute assignment: Python semantics
        // commonly expect things like lbl.Text = 100 to become "100".
        if (targetType == typeof(string)) return NajaBuiltins.ToStr(value);

        // Last-resort: if value is long and target is int32/int16/byte/sbyte, narrow it
        if (value is long l)
        {
            if (targetType == typeof(int) || targetType == typeof(int?)) return (int)l;
            if (targetType == typeof(short) || targetType == typeof(short?)) return (short)l;
            if (targetType == typeof(byte) || targetType == typeof(byte?)) return (byte)l;
            if (targetType == typeof(sbyte) || targetType == typeof(sbyte?)) return (sbyte)l;
            if (targetType == typeof(float) || targetType == typeof(float?)) return (float)l;
            if (targetType == typeof(double) || targetType == typeof(double?)) return (double)l;
            if (targetType.IsEnum) return Enum.ToObject(targetType, (int)l);
        }
        if (value is double d2)
        {
            if (targetType == typeof(float) || targetType == typeof(float?)) return (float)d2;
            if (targetType == typeof(int) || targetType == typeof(int?)) return (int)d2;
        }
        return value; // let SetValue throw with a clear CLR message
    }

    /// <summary>
    /// Resolves a Naja user-defined class by simple name from all loaded assemblies.
    /// Used by EmitName instead of ldtoken, which fails on unfinished TypeBuilders.
    /// </summary>
    public static Type? ResolveTypeByName(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(name, throwOnError: false, ignoreCase: false);
                if (t is not null) return t;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Creates a .NET object via reflection, supporting both parameterless and parameterized constructors.
    /// </summary>
    public static object? CreateDotNet(Type type, object[] args)
    {
        // Try direct instantiation
        try
        {
            if (args.Length == 0)
            {
                var ctor0 = type.GetConstructor(Type.EmptyTypes);
                if (ctor0 != null) return ctor0.Invoke(Array.Empty<object>());
            }

            // Try finding a matching constructor by argument count
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(c => c.GetParameters().Length == args.Length)
                .ToList();

            if (ctors.Count > 0)
            {
                var ctor = ctors[0];
                var ps = ctor.GetParameters();

                // Coerce arguments to match parameter types
                var coercedArgs = new object?[args.Length];
                for (int i = 0; i < args.Length; i++)
                    coercedArgs[i] = CoerceValue(args[i], ps[i].ParameterType);

                return ctor.Invoke(coercedArgs);
            }

            throw new Exception($"No matching constructor found for type '{type.Name}' with {args.Length} arguments");
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            throw tie.InnerException;
        }
    }

    /// <summary>Helper method to try converting an argument to a target type.</summary>
    private static bool TryConvertArg(object? value, Type targetType, bool strict, out object? result, out string? error)
    {
        result = null;
        error = null;

        if (value is null)
        {
            if (targetType.IsValueType && !IsNullableType(targetType))
            {
                error = $"Cannot convert null to non-nullable {targetType.Name}";
                return false;
            }
            return true; // null is OK for reference types
        }

        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType))
        {
            result = value;
            return true;
        }

        // Numeric coercions
        if (IsNumeric(valueType) && IsNumeric(targetType))
        {
            try
            {
                result = Convert.ChangeType(value, targetType);
                return true;
            }
            catch { }
        }

        // String-to-primitive
        if (value is string str)
        {
            if (targetType == typeof(int))
            {
                if (int.TryParse(str, out var intVal)) { result = intVal; return true; }
            }
            else if (targetType == typeof(long))
            {
                if (long.TryParse(str, out var longVal)) { result = longVal; return true; }
            }
            else if (targetType == typeof(double))
            {
                if (double.TryParse(str, out var dVal)) { result = dVal; return true; }
            }
            else if (targetType == typeof(bool))
            {
                if (bool.TryParse(str, out var bVal)) { result = bVal; return true; }
            }
        }

        error = $"Cannot convert {valueType.Name} to {targetType.Name}";
        return false;
    }

    private static bool IsNumeric(Type t) =>
        t == typeof(byte) || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort) ||
        t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) ||
        t == typeof(float) || t == typeof(double) || t == typeof(decimal);

    private static bool IsNullableType(Type t) =>
        t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
}

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
        if (targetType == typeof(string)) return TypeConversion.ToStr(value);

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
    /// Checks the currently-executing assembly first to avoid returning stale types
    /// from previous Eval() runs when the engine is shared across test runs.
    /// </summary>
    public static Type? ResolveTypeByName(string name)
    {
        if (_currentAssembly is not null)
        {
            try
            {
                var t = _currentAssembly.GetType(name, throwOnError: false, ignoreCase: false);
                if (t is not null) return t;
            }
            catch { }
        }

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

    [ThreadStatic]
    private static Assembly? _currentAssembly;

    public static void SetCurrentAssembly(Assembly? asm) => _currentAssembly = asm;

    /// <summary>
    /// Creates a .NET object via reflection, supporting both parameterless and parameterized constructors.
    /// </summary>
    public static object? CreateDotNet(Type type, object[] args)
    {
        if (type is null)
            throw new TypeLoadException("Cannot instantiate type: null");

        // Enum: FontStyle(1) → (FontStyle)1
        if (type.IsEnum)
        {
            if (args.Length == 0) return Enum.ToObject(type, 0);
            if (args.Length == 1) return Enum.ToObject(type, Convert.ToInt64(args[0]));
            throw new ArgumentException($"Enum '{type.FullName}' accepts 0 or 1 argument, got {args.Length}");
        }

        var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var candidates = ctors.Cast<MethodBase>();

        if (!ReflectionHelpers.TryBindBestCallable(candidates, args, out var ctor, out var boundArgs))
        {
            var available = string.Join(", ", ctors.Select(c =>
                "(" + string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name)) + ")"));
            throw new MissingMethodException(
                $"No matching constructor for '{type.FullName}' with {args.Length} arg(s). Available: [{available}]");
        }

        try { return ((ConstructorInfo)ctor).Invoke(boundArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Return the CLR type of an object (type() builtin).</summary>
    public static object TypeOf(object obj) => obj?.GetType() ?? typeof(void);

    /// <summary>Convert integer to character (chr builtin).</summary>
    public static string Chr(object code) => ((char)Convert.ToInt32(code)).ToString();

    /// <summary>Get character code (ord builtin).</summary>
    public static long Ord(object c) => (long)((string)c)[0];

    /// <summary>Convert integer to hexadecimal string (hex builtin).</summary>
    public static string Hex(object n) => "0x" + Convert.ToInt64(n).ToString("x");

    /// <summary>Convert integer to binary string (bin builtin).</summary>
    public static string Bin(object n) => "0b" + Convert.ToString(Convert.ToInt64(n), 2);

    /// <summary>Convert integer to octal string (oct builtin).</summary>
    public static string Oct(object n) => "0o" + Convert.ToString(Convert.ToInt64(n), 8);

    // ── Helper methods ───────────────────────────────────────────────────────

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

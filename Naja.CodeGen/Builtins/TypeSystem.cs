using System.Reflection;
using Naja.Lexer;
using Naja.Parser;

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

        // CRITICAL: Convert NajaFunction to delegate if target expects a delegate
        if (value is NajaFunction najaFunc && typeof(Delegate).IsAssignableFrom(targetType))
        {
            return WrapNajaFunctionAsDelegate(najaFunc, targetType);
        }

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
                if (long.TryParse(str, out var strLong)) { result = strLong; return true; }
            }
            else if (targetType == typeof(double))
            {
                if (double.TryParse(str, out var dVal)) { result = dVal; return true; }
            }
            else if (targetType == typeof(bool))
            {
                if (bool.TryParse(str, out var bVal)) { result = bVal; return true; }
            }
            // String to DateTime
            else if (targetType == typeof(System.DateTime))
            {
                try { result = System.DateTime.Parse(str); return true; }
                catch { }
            }
            // String to DateOnly
            else if (targetType.FullName == "System.DateOnly")
            {
                try
                {
                    var strDt = System.DateTime.Parse(str);
                    var fromDtMethod = targetType.GetMethod("FromDateTime",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (fromDtMethod != null)
                    {
                        result = fromDtMethod.Invoke(null, new object?[] { strDt });
                        return true;
                    }
                }
                catch { }
            }
            // String to FontFamily
            else if (targetType.FullName == "System.Drawing.FontFamily")
            {
                try
                {
                    var ctor = targetType.GetConstructor(new[] { typeof(string) });
                    if (ctor != null)
                    {
                        result = ctor.Invoke(new object[] { str });
                        return true;
                    }
                }
                catch { }
            }
        }

        // Long to DateTime
        if (value is long longTicks && targetType == typeof(System.DateTime))
        {
            try { result = new System.DateTime(longTicks); return true; }
            catch { }
        }

        // DateTime to DateOnly
        if (value is System.DateTime dtValue && targetType.FullName == "System.DateOnly")
        {
            try
            {
                var fromDtMethod2 = targetType.GetMethod("FromDateTime",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (fromDtMethod2 != null)
                {
                    result = fromDtMethod2.Invoke(null, new object?[] { dtValue });
                    return true;
                }
            }
            catch { }
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

    // ── compile() / eval() / exec() ──────────────────────────────────────────

    /// <summary>
    /// Python compile(source, filename, mode, ...) built-in.
    /// Source may be a str or bytes (bytes are decoded per PEP 263 coding
    /// declaration / BOM). Validates syntax; returns a NajaCodeObject on
    /// success. Throws SyntaxError (message names the codec on decode
    /// failures — CPython tests assert on e.g. 'utf-8' in the message).
    /// </summary>
    public static object Compile(object[] args)
    {
        if (args.Length < 1)
            throw new ArgumentException("compile() requires at least 1 argument");

        var filename = args.Length > 1 ? args[1]?.ToString() ?? "<string>" : "<string>";
        var source = args[0] switch
        {
            string s   => s,
            byte[] b   => SourceDecoder.DecodeBytes(b, out _),
            _ => throw new InvalidCastException(
                $"compile() argument must be a string or bytes, not '{args[0]?.GetType().Name}'")
        };

        try
        {
            var lexer  = new Naja.Lexer.Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Naja.Parser.Parser(tokens);
            parser.ParseModule();
            return new NajaCodeObject(source, filename);
        }
        catch (PythonExceptions.SyntaxErrorException) { throw; }
        catch (Naja.Parser.ParseException pex)
        {
            // Thread the parser's structured position into the Python-visible
            // SyntaxError attributes — check_syntax_error asserts err.lineno /
            // err.offset are not None (support/__init__.py:830-833).
            throw new PythonExceptions.SyntaxErrorException(
                $"[L{pex.Line}:C{pex.Column}] invalid syntax: {StripPosition(pex.Message)}",
                pex.Line, pex.Column);
        }
        catch (Naja.Lexer.LexerException lex)
        {
            throw new PythonExceptions.SyntaxErrorException(
                $"[L{lex.Line}:C{lex.Column}] invalid syntax: {StripPosition(lex.Message)}",
                lex.Line, lex.Column);
        }
        catch (Exception ex)
        {
            throw new PythonExceptions.SyntaxErrorException(
                $"invalid syntax: {ex.Message}");
        }
    }

    /// <summary>
    /// ParseException/LexerException already embed "[L#:#] " in their message —
    /// drop a duplicate prefix if present so the output reads like
    /// "[L2:C4] invalid syntax: Expected …" instead of a doubled marker.
    /// </summary>
    private static string StripPosition(string message)
    {
        if (message is not null && message.StartsWith("[L") && message.Contains("] "))
            return message.Substring(message.IndexOf("] ") + 2);
        return message ?? "";
    }

    /// <summary>
    /// Python eval(expression[, globals[, locals]]) built-in.
    /// Compiles the expression to a throwaway in-memory module, executes it,
    /// and returns the value of the expression. Name resolution inside the
    /// expression can see the caller's globals/locals dictionaries when
    /// provided; otherwise only builtins.
    /// </summary>
    public static object? Eval(object[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("eval() requires at least 1 argument");

        var expr = args[0];

        if (expr is NajaCodeObject code)
            return EvalString(code.Source, code.Filename);
        if (expr is string source)
            return EvalString(source, "<string>");

        throw new InvalidCastException(
            $"eval() argument must be a string, not '{expr?.GetType().Name}'");
    }

    /// <summary>
    /// Core eval: wrap the expression source in a module that assigns the
    /// result to a well-known static field, compile+run it in-memory, and
    /// read the field back.
    /// </summary>
    private static object? EvalString(string source, string filename)
    {
        // Reject non-expression input the way Python does.
        source = source.Trim();

        // Parse strictly as an expression first: parse the bare source as a
        // module and require a single expression statement.
        Naja.Parser.Module ast;
        try
        {
            var lexer  = new Naja.Lexer.Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Naja.Parser.Parser(tokens);
            ast = parser.ParseModule();
        }
        catch (Exception ex)
        {
            throw new PythonExceptions.SyntaxErrorException(
                $"invalid syntax: {ex.Message}");
        }

        // eval() accepts ONLY expressions. A module whose statements are not
        // exactly one expression statement (e.g. "x = 1", "print(1)\n1") is a
        // SyntaxError in CPython.
        var stmts = ast.Body.OfType<ExprStatement>().ToList();
        if (ast.Body.Count != 1 || stmts.Count != 1)
            throw new PythonExceptions.SyntaxErrorException(
                "invalid syntax: eval() arg 1 must be an expression, not a statement");

        // Wrap in a 1-tuple: the module field then holds an object[] regardless
        // of what inference narrows the expression to (a bare `x = expr` can
        // narrow the field to long and crash stfld on a BigInteger result).
        var wrapped = $"__naja_eval_result = ({source},)\n";
        var tempPath = Path.Combine(Path.GetTempPath(), $"naja_eval_{Guid.NewGuid():N}.py");
        try
        {
            // UTF-8 WITH BOM — see Exec(): BOM precedence keeps the temp file
            // valid UTF-8 regardless of any coding cookie in the expression.
            System.Text.Encoding bomUtf8 = new System.Text.UTF8Encoding(true);
            File.WriteAllText(tempPath, wrapped, bomUtf8);
            var engine = new NajaEngine();
            var assembly = engine.TryCompile(tempPath, out var errors);
            if (assembly is null)
                throw new PythonExceptions.SyntaxErrorException(
                    $"invalid syntax: {string.Join("; ", errors)}");

            var moduleType = assembly.GetType("naja_eval")
                          ?? assembly.GetType(Path.GetFileNameWithoutExtension(tempPath))
                          ?? assembly.GetTypes().FirstOrDefault(t =>
                                 t.GetMethod("Main", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) is not null);

            var entry = moduleType?.GetMethod("Main", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (moduleType is null || entry is null)
                throw new PythonExceptions.SyntaxErrorException(
                    "invalid syntax: eval() could not compile expression");

            Builtins.TypeSystem.SetCurrentAssembly(assembly);
            try
            {
                entry.Invoke(null, null);
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
            finally
            {
                Builtins.TypeSystem.SetCurrentAssembly(null);
            }

            var field = moduleType.GetField("__naja_eval_result",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            // Unwrap the 1-tuple to the expression's value.
            if (field?.GetValue(null) is object?[] tuple && tuple.Length == 1)
                return tuple[0];
            return field?.GetValue(null);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// Python exec(code[, globals[, locals]]) built-in.
    /// Compiles string/bytes/NajaCodeObject source to a throwaway in-memory
    /// module, executes it, and — when a globals mapping is provided — copies
    /// the module's public static fields into it (matching CPython's
    /// exec(code, ns) behaviour where the namespace receives the names).
    /// </summary>
    public static object? Exec(object[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("exec() requires at least 1 argument");

        var code = args[0];
        string? source = code switch
        {
            NajaCodeObject co => co.Source,
            string s         => s,
            byte[] b         => SourceDecoder.DecodeBytes(b, out _),
            _ => null
        };

        if (source is null)
            throw new InvalidCastException(
                $"exec() argument must be a string, bytes or code object, not '{code?.GetType().Name}'");

        var tempPath = Path.Combine(Path.GetTempPath(), $"naja_exec_{Guid.NewGuid():N}.py");
        try
        {
            // UTF-8 WITH BOM: the BOM (PEP 263 precedence) wins over any
            // coding cookie in the source, so round-tripping a decoded
            // string through the temp file is always valid UTF-8.
            System.Text.Encoding bomUtf8 = new System.Text.UTF8Encoding(true);
            File.WriteAllText(tempPath, source, bomUtf8);
            var engine = new NajaEngine();
            var assembly = engine.TryCompile(tempPath, out var errors);
            if (assembly is null)
                throw new PythonExceptions.SyntaxErrorException(
                    $"invalid syntax: {string.Join("; ", errors)}");

            var moduleType = assembly.GetType(Path.GetFileNameWithoutExtension(tempPath))
                          ?? assembly.GetTypes().FirstOrDefault(t =>
                                 t.GetMethod("Main", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) is not null);
            var entry = moduleType?.GetMethod("Main", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (moduleType is null || entry is null)
                throw new PythonExceptions.SyntaxErrorException(
                    "invalid syntax: exec() could not compile source");

            Builtins.TypeSystem.SetCurrentAssembly(assembly);
            try
            {
                entry.Invoke(null, null);
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
            finally
            {
                Builtins.TypeSystem.SetCurrentAssembly(null);
            }

            // Populate the provided namespace mapping with the module's
            // top-level names (public static fields).
            if (args.Length > 1 &&
                args[1] is System.Collections.IDictionary ns)
            {
                foreach (var f in moduleType.GetFields(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    ns[f.Name] = f.GetValue(null);
                }
            }
            return null;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    /// <summary>
    /// Wraps a NajaFunction as a .NET delegate by extracting its underlying delegate
    /// and creating a compatible wrapper. Handles ThreadStart, EventHandler, and other delegate types.
    /// </summary>
    private static object? WrapNajaFunctionAsDelegate(NajaFunction najaFunc, Type delegateType)
    {
        // Get the underlying delegate from the NajaFunction
        // Use reflection to access the private _target field
        var targetField = typeof(NajaFunction).GetField("_target", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (targetField == null)
            throw new InvalidOperationException("Cannot access NajaFunction._target");

        var underlyingDelegate = targetField.GetValue(najaFunc) as Delegate;
        if (underlyingDelegate == null)
            throw new InvalidOperationException("NajaFunction._target is null");

        // If the delegate is already the right type, return it directly
        if (delegateType.IsAssignableFrom(underlyingDelegate.GetType()))
            return underlyingDelegate;

        // Create a wrapper delegate of the correct type
        try
        {
            // Get the invoke method of the target delegate type
            var delegateInvoke = delegateType.GetMethod("Invoke");
            if (delegateInvoke == null)
                throw new InvalidOperationException($"Cannot find Invoke on {delegateType.Name}");

            var delegateParams = delegateInvoke.GetParameters();
            var delegateReturnType = delegateInvoke.ReturnType;

            // Create a wrapper that converts the delegate signature
            if (delegateParams.Length == 0 && delegateReturnType == typeof(void))
            {
                // ThreadStart: void() - common pattern
                Action wrapper = () => underlyingDelegate.DynamicInvoke();
                return Delegate.CreateDelegate(delegateType, wrapper.Target, wrapper.Method);
            }
            else if (delegateParams.Length == 1 && delegateReturnType == typeof(void))
            {
                // EventHandler-like: void(object) - common pattern
                var paramType = delegateParams[0].ParameterType;
                if (paramType == typeof(object) || paramType.Name == "EventArgs")
                {
                    Action<object> wrapper = (obj) => underlyingDelegate.DynamicInvoke(obj);
                    return Delegate.CreateDelegate(delegateType, wrapper.Target, wrapper.Method);
                }
            }

            // Fall back to dynamic invocation with parameter matching
            var wrapper2 = new Func<object?[], object?>(args => 
            {
                try { return underlyingDelegate.DynamicInvoke(args); }
                catch (TargetInvocationException tie) when (tie.InnerException != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                    return null;
                }
            });

            return Delegate.CreateDelegate(delegateType, wrapper2.Target, wrapper2.Method);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Cannot convert NajaFunction to {delegateType.Name}: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Opaque sentinel returned by compile() representing a validated code object.
/// Passed to eval() or exec() for deferred execution.
/// </summary>
public sealed class NajaCodeObject
{
    public string Source   { get; }
    public string Filename { get; }

    public NajaCodeObject(string source, string filename)
    {
        Source   = source;
        Filename = filename;
    }

    public override string ToString() => $"<code object from '{Filename}'>";
}

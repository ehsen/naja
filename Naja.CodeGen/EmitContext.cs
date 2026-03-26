using System.Reflection;
using System.Reflection.Emit;
using Naja.Semantics;
using Naja.Inference;

namespace Naja.CodeGen;

/// <summary>
/// All state needed to emit IL for a single method body.
/// Passed down through all emitter methods so nothing is global.
/// </summary>
public sealed class EmitContext
{
    public ILGenerator IL { get; }
    public LocalsManager Locals { get; }
    public SemanticModel Model { get; }
    public TypeBuilder TypeBuilder { get; }
    public ModuleBuilder Module { get; }

    /// <summary>Return type of the current method — used to validate return statements.</summary>
    public Type ReturnType { get; }

    /// <summary>Parameter names in order — used for ldarg emission.</summary>
    public IReadOnlyList<string> Parameters { get; }

    /// <summary>
    /// Methods defined so far in this type — used for forward calls.
    /// Populated by the AssemblyEmitter before body emission starts.
    /// </summary>
    public Dictionary<string, MethodBuilder> Methods { get; } = new();

    /// <summary>
    /// Parameter types for each method — stored separately so we never call
    /// GetParameters() on an unfinished MethodBuilder (throws NotSupportedException).
    /// </summary>
    public Dictionary<string, Type[]> MethodParamTypes { get; } = new();

    /// <summary>Instance fields of the current class — for self.x access.</summary>
    public Dictionary<string, FieldBuilder> InstanceFields { get; } = new();

    /// <summary>Defined class types in the module scope — for class instantiation.</summary>
    public Dictionary<string, TypeBuilder> ClassTypes { get; } = new();

    public Dictionary<string, ConstructorBuilder> ClassConstructors { get; } = new();
    public Dictionary<string, int> ClassCtorArgCounts { get; } = new();


    public Dictionary<string, MethodBuilder> AllClassMethods { get; } = new();
    public Dictionary<string, Type[]> AllClassMethodParamTypes { get; } = new();
    /// <summary>Set of defined class methods (Format: ClassName.MethodName) to check for method presence.</summary>
    public HashSet<string> ClassMethods { get; } = new();

    /// <summary>True when emitting an instance method (ldarg.0 = this).</summary>
    public bool IsInstanceMethod { get; set; }

    /// <summary>True when emitting inside a function body (not module level).</summary>
    public bool IsInsideFunction { get; set; }

    /// <summary>Names declared with 'global' in the current function scope — used to detect nonlocal+global conflicts.</summary>
    public HashSet<string> GlobalNames { get; } = new();

    /// <summary>Name bound to 'self' / 'cls' in the source.</summary>
    public string? SelfName { get; set; }

    /// <summary>
    /// Fields defined in this type — used for class variable access.
    /// </summary>
    public Dictionary<string, FieldBuilder> Fields { get; } = new();

    /// <summary>
    /// .NET imports from "from System.X import Y" — name → (FullTypeName, AssemblyName).
    /// Used to emit Type.GetType and static method calls.
    /// </summary>
    public Dictionary<string, (string TypeName, string AssemblyName)> ImportMap { get; } = new();

    /// <summary>
    /// .NET namespace imports from "import System" — namespace name → AssemblyName.
    /// Used to resolve types like System.DateTime where System is the namespace.
    /// </summary>
    public Dictionary<string, string> NamespaceImports { get; } = new();

    /// <summary>
    /// Inference results for this module.
    /// Null when running without the inference pass (e.g. unit tests that
    /// construct EmitContext directly).
    /// </summary>
    public InferenceResult? Inference { get; init; }

    /// <summary>
    /// Diagnostic sink for reporting dynamic sites, explicit dynamic() calls,
    /// and cast() assertions. Null in permissive mode (no reporting overhead).
    /// </summary>
    public Naja.Inference.DiagnosticSink? Diagnostics { get; init; }

    /// <summary>
    /// Names of Application.* methods already emitted by the WinForms preamble in
    /// AssemblyEmitter.EmitModule(). StatementEmitter must skip these to prevent
    /// double-emit (EnableVisualStyles called twice etc.).
    /// </summary>
    public HashSet<string> WinFormsPreambleEmitted { get; } = new();

    /// <summary>
    /// For generator functions: local variable holding the list of yielded values.
    /// Initialized at function entry if the function contains yield statements.
    /// </summary>
    public LocalBuilder? GeneratorListLocal { get; set; }

    /// <summary>
    /// True when emitting the body of a generator function (the __gen_body__ method).
    /// When true, yield expressions call NajaGenerator.Yield(v) via ldarg.0,
    /// and return statements throw NajaGeneratorReturn instead of returning normally.
    /// </summary>
    public bool IsGeneratorBody { get; set; }

    /// <summary>
    /// Names of function parameters that have been hoisted to static fields (for closure capture).
    /// When a name is in this set, <see cref="TryEmitLoadParam"/> returns false so the
    /// hoisted field takes priority in name resolution.
    /// </summary>
    public HashSet<string> HoistedParams { get; } = new();

    /// <summary>
    /// Tracks nesting depth of open exception blocks (BeginExceptionBlock increments,
    /// EndExceptionBlock decrements). Used to determine when `ret`/`br` must be
    /// replaced with `leave` to legally exit a protected region.
    /// </summary>
    public int ExceptionBlockDepth { get; set; }

    /// <summary>
    /// Label placed after all exception blocks in a function; targeted by `leave`
    /// instructions emitted for `return` statements inside protected regions.
    /// Lazily initialised the first time a return-inside-exception is encountered.
    /// </summary>
    public System.Reflection.Emit.Label? MethodReturnLabel { get; set; }

    /// <summary>
    /// Temp local used to hold a return value when the function body contains
    /// `return` inside a protected region. The epilog loads this local and executes `ret`.
    /// </summary>
    public LocalBuilder? ReturnValueLocal { get; set; }

    /// <summary>
    /// When emitting handler body code inside a catch dispatch block, holds the
    /// <see cref="LocalBuilder"/> that stores the caught exception. Set before
    /// emitting handler.Body and restored to the previous value afterward.
    /// <c>null</c> when not inside a handler body.
    /// Used by <c>EmitRaise</c> to set <c>__context__</c> on newly raised exceptions
    /// (Python implicit exception chaining).
    /// </summary>
    public LocalBuilder? ActiveHandlerExceptionLocal { get; set; }

    /// <summary>
    /// Names of locals that were bound as exception handler variables (except … as e)
    /// and have been deleted at the end of their handler. Accessing them at runtime
    /// throws NameError (MissingFieldException) to match Python 3 semantics.
    /// </summary>
    public HashSet<string> ExceptionHandlerVars { get; } = new();

    /// <summary>
    /// For comprehension helpers: unique scope ID for hoisted loop variables.
    /// Used to avoid name collisions when multiple comprehensions use the same variable name.
    /// Format: "comp_{line}_{col}" or null if not in a comprehension helper.
    /// </summary>
    public string? ComprehensionScopeId { get; set; }

    /// <summary>
    /// For nested functions that capture outer-scope parameters (NajaFunction closure pattern):
    /// maps inner function name → ordered list of captured outer parameter names.
    /// When NameEmitters resolves such a function name as a value, it wraps the delegate
    /// in a NajaFunction with the current captured values snapshotted as defaults.
    /// </summary>
    public Dictionary<string, List<string>> FunctionCapturedParams { get; } = new();

    /// <summary>
    /// For module-level and nested functions: maps variable name → a local variable of type
    /// <c>object[]</c> (length 1) that acts as a mutable per-call cell for that variable.
    /// Variables hoisted to cells are NOT added to <see cref="Fields"/>; reads/writes go
    /// through index [0] of the array so each outer-function call gets its own cell.
    /// </summary>
    public Dictionary<string, LocalBuilder> CellLocals { get; } = new();

    /// <summary>
    /// For inner functions that capture outer-scope cell variables: maps the Python variable
    /// name to the name of the <c>object[]</c> parameter that holds the cell reference.
    /// E.g. "n" → "__cell_n" means load param "__cell_n" then index [0] to read/write n.
    /// </summary>
    public Dictionary<string, string> CellParamOf { get; } = new();

    /// <summary>
    /// Maps nested function name → list of outer-scope cell-variable names whose cell
    /// references are passed as trailing defaults when wrapping the inner function in a
    /// <see cref="NajaFunction"/> (analogous to <see cref="FunctionCapturedParams"/> but
    /// for mutable per-call cells rather than value snapshots).
    /// </summary>
    public Dictionary<string, List<string>> FunctionCapturedCells { get; } = new();

    /// <summary>
    /// Maps function name (module-level or nested) → pre-evaluated default argument values
    /// for missing parameters. Used by NameEmitters to wrap functions in NajaFunction
    /// when calling them with fewer arguments than the function declares.
    /// This allows Python-style default parameters to work correctly at call time.
    /// </summary>
    public Dictionary<string, object?[]> FunctionDefaults { get; } = new();

    /// <summary>
    /// Maps function name → the original FunctionDef AST node.
    /// Used by NameEmitters to access parameter Default expressions when wrapping
    /// functions in NajaFunction for default parameter handling.
    /// </summary>
    public Dictionary<string, Parser.FunctionDef> FunctionDefs { get; } = new();

    /// <summary>
    /// Per-nested-class field context snapshot: maps the nested class unique name
    /// (e.g. "C_L677") to the <see cref="Fields"/> dictionary as it was at the
    /// moment the class was defined inside the enclosing function body.
    /// Propagated upward through nested EmitContext instances so the AssemblyEmitter
    /// can supply the correct hoisted-field bindings when compiling each nested class
    /// body in Pass 3, instead of using the coarse merged _innerFunctionFields dict
    /// which suffers from TryAdd collisions when multiple methods hoist same-named vars.
    /// </summary>
    public Dictionary<string, Dictionary<string, FieldBuilder>> NestedClassFieldContexts { get; } = new();

    public EmitContext(
        ILGenerator il,
        SemanticModel model,
        TypeBuilder typeBuilder,
        ModuleBuilder moduleBuilder,
        Type returnType,
        IReadOnlyList<string> parameters)
    {
        IL = il;
        Locals = new LocalsManager(il);
        Model = model;
        TypeBuilder = typeBuilder;
        Module = moduleBuilder;
        ReturnType = returnType;
        Parameters = parameters;
    }

    /// <summary>
    /// Get the argument index for a parameter name.
    /// Returns -1 if not a parameter.
    /// </summary>
    public int GetParamIndex(string name)
    {
        for (int i = 0; i < Parameters.Count; i++)
            if (Parameters[i] == name) return i;
        return -1;
    }

    /// <summary>
    /// Emit ldarg for a parameter by name.
    /// </summary>
    public bool TryEmitLoadParam(string name)
    {
        if (HoistedParams.Contains(name)) return false;
        int idx = GetParamIndex(name);
        if (idx < 0) return false;
        
        int ilIdx = IsInstanceMethod ? idx + 1 : idx;

        // Use efficient short-form opcodes where possible
        switch (ilIdx)
        {
            case 0: IL.Emit(OpCodes.Ldarg_0); break;
            case 1: IL.Emit(OpCodes.Ldarg_1); break;
            case 2: IL.Emit(OpCodes.Ldarg_2); break;
            case 3: IL.Emit(OpCodes.Ldarg_3); break;
            default: IL.Emit(OpCodes.Ldarg_S, (byte)ilIdx); break;
        }
        return true;
    }
}
namespace Naja.CodeGen;

/// <summary>
/// Lightweight function wrapper used to store a delegate plus pre-evaluated
/// default arguments. Exposes a Python-style __call__(object[] args) method
/// so the runtime can invoke it uniformly when called with fewer args than
/// the underlying delegate expects.
/// </summary>
public sealed class NajaFunction
{
    private readonly Delegate _target;
    private readonly object?[] _defaults; // pre-evaluated defaults for trailing parameters

    public string? __name__ { get; set; }
    public string? __qualname__ { get; set; }
    public string? __module__ { get; set; }
    public string? __doc__ { get; set; }
    public Dictionary<string, object?> __dict__ { get; } = new();
    public Dictionary<string, object?>? __annotations__ { get; set; }

    public NajaFunction(Delegate target, object?[] defaults)
    {
        _target = target;
        _defaults = defaults ?? System.Array.Empty<object?>();
    }

    /// <summary>The underlying delegate — exposed so the event subsystem can key the
    /// delegate cache on the compiled method rather than on the NajaFunction instance.</summary>
    internal Delegate UnderlyingDelegate => _target;

    /// <summary>True when this function has no captured values or parameter defaults,
    /// meaning the underlying MethodInfo alone identifies the logical handler.</summary>
    internal bool HasNoCaptures => _defaults.Length == 0;

    // Python-style call entry: receives positional args as object[]
    public object? __call__(object[] args)
    {
        // Merge provided args with pre-evaluated defaults for missing trailing parameters
        var method = _target.Method;
        int paramCount = method.GetParameters().Length;
        var finalArgs = new object?[paramCount];
        
        // Copy provided arguments
        for (int i = 0; i < args.Length && i < paramCount; i++)
        {
            finalArgs[i] = args[i];
        }
        
        // Fill trailing parameters with defaults
        int defaultsToUse = Math.Min(_defaults.Length, paramCount - args.Length);
        for (int i = 0; i < defaultsToUse; i++)
        {
            finalArgs[args.Length + i] = _defaults[i];
        }
        
        // Invoke delegate with merged argument list
        try
        {
            return _target.DynamicInvoke(finalArgs);
        }
        catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // DynamicInvoke wraps exceptions — unwrap so the original exception type
            // (e.g. ValueError from a Naja event handler) propagates correctly.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            throw; // unreachable
        }
    }
}

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

    public NajaFunction(Delegate target, object?[] defaults)
    {
        _target = target;
        _defaults = defaults ?? System.Array.Empty<object?>();
    }

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
        return _target.DynamicInvoke(finalArgs);
    }
}

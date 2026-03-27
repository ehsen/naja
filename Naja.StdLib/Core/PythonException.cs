namespace Naja.StdLib.Core;

/// <summary>
/// Shared exception factory for Python-compliant error types.
/// All modules should use these to ensure exception messages and types match CPython exactly.
/// </summary>
public static class PythonException
{
    // ── Exception factory methods ──────────────────────────────────────────────

    /// <summary>Create a ValueError with the given message.</summary>
    public static Exception ValueError(string message) =>
        new PythonValueError(message);

    /// <summary>Create a TypeError with the given message.</summary>
    public static Exception TypeError(string message) =>
        new PythonTypeError(message);

    /// <summary>Create a RuntimeError with the given message.</summary>
    public static Exception RuntimeError(string message) =>
        new PythonRuntimeError(message);

    /// <summary>Create a KeyError with the given key.</summary>
    public static Exception KeyError(object key) =>
        new PythonKeyError(key);

    /// <summary>Create an IndexError with the given message.</summary>
    public static Exception IndexError(string message) =>
        new PythonIndexError(message);

    /// <summary>Create an AttributeError with the given message.</summary>
    public static Exception AttributeError(string message) =>
        new PythonAttributeError(message);

    /// <summary>Create an OSError with the given message and optional errno.</summary>
    public static Exception OSError(string message, int? errno = null) =>
        new PythonOSError(message, errno);

    /// <summary>Create a ZeroDivisionError with the given message.</summary>
    public static Exception ZeroDivisionError(string message) =>
        new PythonZeroDivisionError(message);

    /// <summary>Create an OverflowError with the given message.</summary>
    public static Exception OverflowError(string message) =>
        new PythonOverflowError(message);

    /// <summary>Create a NotImplementedError with the given message.</summary>
    public static Exception NotImplementedError(string message) =>
        new PythonNotImplementedError(message);
}

// ── Exception type definitions ─────────────────────────────────────────────────

/// <summary>Base class for Python exceptions mapped to .NET.</summary>
public class PythonExceptionBase : Exception
{
    public PythonExceptionBase(string message) : base(message) { }
}

public class PythonValueError : ArgumentException
{
    public PythonValueError(string message) : base(message) { }
}

public class PythonTypeError : PythonExceptionBase
{
    public PythonTypeError(string message) : base(message) { }
}

public class PythonRuntimeError : PythonExceptionBase
{
    public PythonRuntimeError(string message) : base(message) { }
}

public class PythonKeyError : PythonExceptionBase
{
    public object Key { get; }
    public PythonKeyError(object key) : base($"'{key}'") => Key = key;
}

public class PythonIndexError : PythonExceptionBase
{
    public PythonIndexError(string message) : base(message) { }
}

public class PythonAttributeError : PythonExceptionBase
{
    public PythonAttributeError(string message) : base(message) { }
}

public class PythonOSError : PythonExceptionBase
{
    public int? ErrNo { get; }
    public PythonOSError(string message, int? errno = null) : base(message) => ErrNo = errno;
}

public class PythonZeroDivisionError : PythonExceptionBase
{
    public PythonZeroDivisionError(string message) : base(message) { }
}

public class PythonOverflowError : PythonExceptionBase
{
    public PythonOverflowError(string message) : base(message) { }
}

public class PythonNotImplementedError : PythonExceptionBase
{
    public PythonNotImplementedError(string message) : base(message) { }
}

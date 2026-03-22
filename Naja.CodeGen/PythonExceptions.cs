namespace Naja.CodeGen;

/// <summary>
/// Python-compatible exception types that don't have direct .NET equivalents.
/// These allow catch blocks and isinstance checks to work correctly with CPython test suite.
/// </summary>
public static class PythonExceptions
{
    /// <summary>Raised when a syntax error is encountered during parsing.</summary>
    public class SyntaxErrorException : Exception
    {
        public SyntaxErrorException(string message) : base(message) { }
        public SyntaxErrorException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Raised when indentation is incorrect (subset of SyntaxError).</summary>
    public class IndentationErrorException : SyntaxErrorException
    {
        public IndentationErrorException(string message) : base(message) { }
    }

    /// <summary>Raised when an assertion fails.</summary>
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
        public AssertionException(string message, Exception inner) : base(message, inner) { }
    }
}

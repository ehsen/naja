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

        // Python SyntaxError attributes. CPython populates lineno/offset for
        // parse-time errors and leaves them None for `raise SyntaxError(...)`.
        // test.support.check_syntax_error asserts both are not None after a
        // failed compile() — so TypeSystem.Compile must thread the parser's
        // line/column through (ParseException/LexerException carry them).
        public long? lineno { get; init; }
        public long? offset { get; init; }

        public SyntaxErrorException(string message, long? line, long? col) : base(message)
        {
            lineno = line is > 0 ? line : null;
            offset = col is > 0 ? col : null;
        }
    }

    /// <summary>Raised when indentation is incorrect (subset of SyntaxError).</summary>
    public class IndentationErrorException : SyntaxErrorException
    {
        public IndentationErrorException(string message) : base(message) { }
    }

    /// <summary>
    /// Raised when an assertion fails (Python AssertionError).
    /// Extends Naja.StdLib.AssertionException so that 'except AssertionError:'
    /// also catches AssertionException thrown by NajaTestCase assertion methods.
    /// </summary>
    public class AssertionException : Naja.StdLib.AssertionException
    {
        public AssertionException(string message) : base(message) { }
        public AssertionException(string message, Exception inner) : base(message, inner) { }
    }
}

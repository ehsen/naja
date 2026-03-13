namespace Naja.Lexer;

public sealed class LexerException(string message, int line = 0, int column = 0)
    : Exception(line > 0 ? $"[L{line}:C{column}] {message}" : message)
{
    public int Line   { get; } = line;
    public int Column { get; } = column;
}

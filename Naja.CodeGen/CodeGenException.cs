namespace Naja.CodeGen;

public sealed class CodeGenException(string message, int line = 0, int col = 0)
    : Exception(line > 0 ? $"[L{line}:C{col}] {message}" : message)
{
    public int Line   { get; } = line;
    public int Column { get; } = col;
}

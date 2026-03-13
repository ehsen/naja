namespace Naja.Lexer;

/// <summary>
/// Manages Python's indentation stack, emitting INDENT and DEDENT tokens.
///
/// Python's grammar is whitespace-sensitive. Each logical line that starts
/// at a greater column than the previous block emits an INDENT token.
/// When a line returns to a lesser column, one or more DEDENT tokens are
/// emitted — potentially multiple DEDENTs for a single line.
///
/// Reference: https://docs.python.org/3/reference/lexical_analysis.html#indentation
/// </summary>
internal sealed class IndentTracker
{
    // Indent stack — always starts with a single 0 (module level)
    private readonly Stack<int> _stack = new(new[] { 0 });

    // Pending DEDENT tokens to be flushed before the current line's tokens
    private readonly Queue<Token> _pending = new();

    public bool HasPending => _pending.Count > 0;

    public Token DequeuePending() => _pending.Dequeue();

    /// <summary>
    /// Called at the start of each non-empty, non-continuation logical line
    /// with its measured indentation level (number of spaces, tabs=8).
    /// </summary>
    /// <returns>
    ///   +1  → emit one INDENT token (caller creates it)
    ///    0  → no change
    ///   -N  → N DEDENT tokens queued in _pending
    /// </returns>
    public int Process(int newLevel, int line, int col)
    {
        int current = _stack.Peek();

        if (newLevel > current)
        {
            _stack.Push(newLevel);
            return 1;   // signal: emit INDENT
        }

        if (newLevel == current)
            return 0;

        // DEDENT — may be multiple
        int dedentCount = 0;
        while (_stack.Count > 1 && _stack.Peek() > newLevel)
        {
            _stack.Pop();
            dedentCount++;
            // Queue a DEDENT token for each level popped
            _pending.Enqueue(new Token(TokenType.Dedent, "", line, col, 0, 0));
        }

        if (_stack.Peek() != newLevel)
            throw new LexerException(
                $"Inconsistent indentation at line {line}: " +
                $"dedented to level {newLevel} but no matching indent level found.");

        return -dedentCount;
    }

    /// <summary>
    /// At end of file, flush remaining indent levels as DEDENTs.
    /// </summary>
    public IEnumerable<Token> FlushEof(int line)
    {
        while (_stack.Count > 1)
        {
            _stack.Pop();
            yield return new Token(TokenType.Dedent, "", line, 0, 0, 0);
        }
    }
}

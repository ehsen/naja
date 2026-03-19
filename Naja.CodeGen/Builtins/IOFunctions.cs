namespace Naja.CodeGen.Builtins;

/// <summary>
/// Input/output and file operations.
/// Implements print(), input(), open() and related file I/O functions.
/// </summary>
public static class IOFunctions
{
    /// <summary>Print Python values to console, matching Python's print() behavior.</summary>
    public static void Print(object[] args)
    {
        var output = string.Join(" ", args.Select(arg => TypeConversion.ToStr(arg)));
        Console.WriteLine(output);
    }

    /// <summary>Read a line from standard input with optional prompt.</summary>
    public static string Input(string prompt = "")
    {
        if (!string.IsNullOrEmpty(prompt))
            Console.Write(prompt);
        return Console.ReadLine() ?? "";
    }

    /// <summary>Open a file, returning a file object (not yet implemented).</summary>
    public static object? Open(object[] args)
    {
        // TODO: Implement file handling
        throw new NotImplementedException("File operations (open) not yet implemented");
    }
}

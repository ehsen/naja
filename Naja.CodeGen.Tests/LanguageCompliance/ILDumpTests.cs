using Xunit;
using Naja.CodeGen;
using System;
using System.IO;

namespace Naja.CodeGen.Tests.LanguageCompliance;

public sealed class ILDumpTests
{
    private static readonly NajaEngine Engine = new();

    [Fact]
    public void DumpExceptionsFullIL()
    {
        var tempDir = Path.GetTempPath();
        var dumpFile = Path.Combine(tempDir, "naja_il_exceptions_full.txt");
        
        // Clean up old dump if it exists
        if (File.Exists(dumpFile))
            File.Delete(dumpFile);

        Console.WriteLine($"IL dump will be written to: {dumpFile}");

        try
        {
            Engine.Eval(
                Path.Combine("testdata", "languagecompliance", "exceptions", "exceptions_full.naja"),
                dumpIL: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Execution error (expected): {ex.GetType().Name}: {ex.Message}");
        }

        // Check if dump file was created
        if (File.Exists(dumpFile))
        {
            Console.WriteLine($"IL dump file exists: {dumpFile}");
            var lines = File.ReadAllLines(dumpFile);
            Console.WriteLine($"IL dump has {lines.Length} lines");
            
            // Print first 100 lines containing "classify_error"
            var classifyErrorLines = Array.FindAll(lines, l => l.Contains("classify_error"));
            Console.WriteLine($"Found {classifyErrorLines.Length} lines mentioning classify_error");
            
            // Just show that we can read it
            foreach (var line in classifyErrorLines.Take(20))
                Console.WriteLine(line);
        }
        else
        {
            Console.WriteLine($"IL dump file NOT created at: {dumpFile}");
        }
    }
}

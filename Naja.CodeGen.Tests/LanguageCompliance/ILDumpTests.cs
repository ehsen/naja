using Xunit;
using Naja.CodeGen;
using System;
using System.IO;

namespace Naja.CodeGen.Tests.LanguageCompliance;

public sealed class ILDumpTests
{
    private static readonly NajaEngine Engine = new();

    [Fact]
    public void DumpGeneratorBodiesIL()
    {
        // Dump gen_range (closures_generators.naja)
        var dumpFile = Path.Combine(Path.GetTempPath(), "naja_il_closures_generators.txt");
        if (File.Exists(dumpFile)) File.Delete(dumpFile);
        try
        {
            Engine.Eval(
                Path.Combine("testdata", "languagecompliance", "generators", "closures_generators.naja"),
                dumpIL: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Execution error (expected): {ex.GetType().Name}: {ex.Message}");
        }
        if (File.Exists(dumpFile))
        {
            var lines = File.ReadAllLines(dumpFile);
            Console.WriteLine($"Generator IL dump has {lines.Length} lines");
            bool inGenRange = false;
            foreach (var line in lines)
            {
                if (line.Contains("gen_range")) inGenRange = true;
                if (inGenRange && line.Contains("=== Type:") && !line.Contains("gen_range")) inGenRange = false;
                if (inGenRange) Console.WriteLine(line);
            }
        }
        else
        {
            Console.WriteLine($"IL dump NOT created at: {dumpFile}");
        }

        // Dump accumulator (SendValue test)
        var src = """
            def accumulator():
                total = 0
                while True:
                    value = yield total
                    if value is None:
                        break
                    total = total + value

            gen = accumulator()
            next(gen)
            gen.send(10)
            result = gen.send(5)
            assert result == 15
            """;
        var dir = Path.Combine(Path.GetTempPath(), "naja_generator_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"gen_{Math.Abs(src.GetHashCode())}.naja");
        File.WriteAllText(path, src.TrimStart());
        var accDumpFile = Path.Combine(Path.GetTempPath(), $"naja_il_gen_{Math.Abs(src.GetHashCode())}.txt");
        if (File.Exists(accDumpFile)) File.Delete(accDumpFile);
        try { Engine.Eval(path, dumpIL: true); }
        catch (Exception ex) { Console.WriteLine($"Accumulator error: {ex.GetType().Name}: {ex.Message}"); }
        if (File.Exists(accDumpFile))
        {
            var lines = File.ReadAllLines(accDumpFile);
            Console.WriteLine($"Accumulator IL dump has {lines.Length} lines");
            bool inAcc = false;
            foreach (var line in lines)
            {
                if (line.Contains("accumulator")) inAcc = true;
                if (inAcc && line.Contains("=== Type:") && !line.Contains("accumulator")) inAcc = false;
                if (inAcc) Console.WriteLine(line);
            }
        }
        else
        {
            Console.WriteLine($"Accumulator IL dump NOT created at: {accDumpFile}");
        }
    }

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

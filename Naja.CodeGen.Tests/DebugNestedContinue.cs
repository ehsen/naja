using Xunit;
using Naja.CodeGen;
using System.IO;

namespace Naja.CodeGen.Tests.Debug;

public class DebugNestedContinue
{
    private static readonly NajaEngine Engine = new();

    private static void Run(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "naja_debug_tests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"debug_{Math.Abs(source.GetHashCode())}.naja");
        File.WriteAllText(path, source.TrimStart());
        Engine.Eval(path, dumpIL: true);
    }

    [Fact]
    public void Inner_Loop_Sees_Outer_Variable()
        => Run("""
            result = []
            for i in range(4):
                for j in range(1):
                    result.append(i)
            assert 2 in result
            assert result == [0, 1, 2, 3]
            """);

    [Fact]
    public void Inner_Loop_Comparison_Works()
        => Run("""
            matched = []
            for i in range(4):
                for j in range(1):
                    if i == 2:
                        matched.append(i)
            assert 2 in matched
            """);

    [Fact]
    public void Continue_NestedLoop_Diagnostic()
        => Run("""
            continued = []
            result = []
            for i in range(4):
                for j in range(4):
                    if i == 2:
                        continued.append((i, j))
                        continue
                    result.append((i, j))
            assert (2, 0) not in result, "continue did not prevent append"
            assert (2, 0) in continued, "continue body did not execute"
            """);

    [Fact]
    public void Continue_NestedLoop_NoBreak()
        => Run("""
            result = []
            for i in range(4):
                for j in range(4):
                    if i == 2:
                        continue
                    result.append((i, j))
            assert (2, 0) not in result
            assert (0, 0) in result
            """);

    [Fact]
    public void Continue_NestedLoop_WithBreak()
        => Run("""
            result = []
            for i in range(4):
                for j in range(4):
                    if j == 2:
                        break
                    if i == 2:
                        continue
                    result.append((i, j))
            assert (2, 0) not in result
            assert (0, 0) in result
            """);

    [Fact]
    public void Continue_SingleLoop_Basic()
        => Run("""
            result = []
            for i in range(4):
                if i == 2:
                    continue
                result.append(i)
            assert 0 in result
            assert 2 not in result
            """);

    [Fact]
    public void Continue_NestedLoop_AppendInt()
        => Run("""
            result = []
            for i in range(4):
                for j in range(4):
                    if i == 2:
                        continue
                    result.append(i)
            assert 0 in result
            assert 2 not in result
            """);

    [Fact]
    public void TupleInList_Basic()
        => Run("""
            result = []
            result.append((0, 0))
            result.append((1, 2))
            assert (0, 0) in result
            assert (1, 2) in result
            """);
}

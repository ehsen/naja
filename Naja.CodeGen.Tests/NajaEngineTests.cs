using System.Reflection;
using Naja.CodeGen;
using Xunit;

namespace Naja.CodeGen.Tests;

/// <summary>
/// Tests for the NajaEngine scripting entry point.
/// </summary>
public class NajaEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NajaEngine _engine;
    private readonly StringWriter _consoleOutput;
    private readonly TextWriter _originalConsoleOut;

    public NajaEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"NajaEngineTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _engine = new NajaEngine();
        
        // Capture console output
        _originalConsoleOut = Console.Out;
        _consoleOutput = new StringWriter();
        Console.SetOut(_consoleOutput);
    }

    public void Dispose()
    {
        Console.SetOut(_originalConsoleOut);
        _consoleOutput.Dispose();
        
        // Clean up temp directory
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private string CreateTempScript(string content, string? filename = null)
    {
        filename ??= $"test_{Guid.NewGuid():N}.naja";
        var path = Path.Combine(_tempDir, filename);
        File.WriteAllText(path, content);
        return path;
    }

    private string GetConsoleOutput()
    {
        return _consoleOutput.ToString().TrimEnd().Replace("\r\n", "\n");
    }

    private void ClearConsoleOutput()
    {
        _consoleOutput.GetStringBuilder().Clear();
    }

    // =========================================================================
    // Basic Execution Tests
    // =========================================================================

    [Fact]
    public void Eval_HelloWorld_PrintsCorrectOutput()
    {
        var script = CreateTempScript("print(\"Hello from NajaEngine!\")");
        
        _engine.Eval(script);
        
        Assert.Equal("Hello from NajaEngine!", GetConsoleOutput());
    }

    [Fact]
    public void Eval_VariableAssignmentAndPrint()
    {
        var script = CreateTempScript(@"
x = 42
print(x)
");
        
        _engine.Eval(script);
        
        Assert.Equal("42", GetConsoleOutput());
    }

    [Fact]
    public void Eval_ArithmeticOperations()
    {
        var script = CreateTempScript(@"
print(3 + 4)
print(10 - 3)
print(6 * 7)
");
        
        _engine.Eval(script);
        
        Assert.Equal("7\n7\n42", GetConsoleOutput());
    }

    [Fact]
    public void Eval_FunctionDefinitionAndCall()
    {
        var script = CreateTempScript(@"
def greet(name):
    print(f""Hello, {name}!"")

greet(""World"")
greet(""Naja"")
");
        
        _engine.Eval(script);
        
        Assert.Equal("Hello, World!\nHello, Naja!", GetConsoleOutput());
    }

    [Fact]
    public void Eval_ForLoop()
    {
        var script = CreateTempScript(@"
for i in range(3):
    print(i)
");
        
        _engine.Eval(script);
        
        Assert.Equal("0\n1\n2", GetConsoleOutput());
    }

    [Fact]
    public void Eval_IfStatement()
    {
        var script = CreateTempScript(@"
x = 10
if x > 5:
    print(""big"")
else:
    print(""small"")
");
        
        _engine.Eval(script);
        
        Assert.Equal("big", GetConsoleOutput());
    }

    [Fact]
    public void Eval_ClassDefinition()
    {
        var script = CreateTempScript(@"
class Point:
    def __init__(self, x, y):
        self.x = x
        self.y = y
    
    def sum(self):
        return self.x + self.y

p = Point(3, 4)
print(p.sum())
");
        
        _engine.Eval(script);
        
        Assert.Equal("7", GetConsoleOutput());
    }

    [Fact]
    public void Eval_ListComprehension()
    {
        var script = CreateTempScript(@"
squares = [x * x for x in range(5)]
print(squares)
");
        
        _engine.Eval(script);
        
        Assert.Equal("[0, 1, 4, 9, 16]", GetConsoleOutput());
    }

    // =========================================================================
    // Error Handling Tests
    // =========================================================================

    [Fact]
    public void Eval_NonExistentFile_ThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(_tempDir, "does_not_exist.naja");
        
        var ex = Assert.Throws<FileNotFoundException>(() => _engine.Eval(nonExistentPath));
        Assert.Contains("does_not_exist.naja", ex.Message);
    }

    [Fact]
    public void Eval_SyntaxError_ThrowsCodeGenException()
    {
        var script = CreateTempScript("print(");  // Syntax error: unclosed parenthesis
        
        var ex = Assert.Throws<CodeGenException>(() => _engine.Eval(script));
        // Should contain information about the parse error
    }

    [Fact]
    public void Eval_InvalidIndentation_ThrowsCodeGenException()
    {
        var script = CreateTempScript(@"
def foo():
print(""indented wrong"")
");
        
        Assert.Throws<CodeGenException>(() => _engine.Eval(script));
    }

    [Fact]
    public void Eval_RuntimeException_PropagatesToCaller()
    {
        // Use integer floor division by zero which throws in .NET
        var script = CreateTempScript(@"
x = 10 // 0
print(x)
");
        
        // Runtime exceptions should propagate unwrapped
        Assert.ThrowsAny<Exception>(() => _engine.Eval(script));
    }

    [Fact]
    public void Eval_FailedAssert_ThrowsException()
    {
        var script = CreateTempScript(@"
assert 1 == 2, ""This should fail""
");
        
        Assert.Throws<Exception>(() => _engine.Eval(script));
    }

    // =========================================================================
    // Complex Program Tests
    // =========================================================================

    [Fact]
    public void Eval_FizzBuzz()
    {
        var script = CreateTempScript(@"
for i in range(1, 16):
    if i % 15 == 0:
        print(""FizzBuzz"")
    elif i % 3 == 0:
        print(""Fizz"")
    elif i % 5 == 0:
        print(""Buzz"")
    else:
        print(i)
");
        
        _engine.Eval(script);
        
        var expected = string.Join("\n",
            "1", "2", "Fizz", "4", "Buzz", "Fizz", "7", "8",
            "Fizz", "Buzz", "11", "Fizz", "13", "14", "FizzBuzz");
        Assert.Equal(expected, GetConsoleOutput());
    }

    [Fact]
    public void Eval_RecursiveFactorial()
    {
        var script = CreateTempScript(@"
def factorial(n):
    if n == 0:
        return 1
    return n * factorial(n - 1)

print(factorial(5))
");
        
        _engine.Eval(script);
        
        Assert.Equal("120", GetConsoleOutput());
    }

    [Fact]
    public void Eval_ExceptionHandling()
    {
        var script = CreateTempScript(@"
try:
    raise ValueError(""oops"")
except:
    print(""caught"")
finally:
    print(""finally"")
");
        
        _engine.Eval(script);
        
        Assert.Equal("caught\nfinally", GetConsoleOutput());
    }

    [Fact]
    public void Eval_StringMethods()
    {
        var script = CreateTempScript(@"
text = ""hello world""
print(text.upper())
print(text.replace(""world"", ""Naja""))
print(len(text))
");
        
        _engine.Eval(script);
        
        Assert.Equal("HELLO WORLD\nhello Naja\n11", GetConsoleOutput());
    }

    [Fact]
    public void Eval_DictOperations()
    {
        var script = CreateTempScript(@"
d = {""a"": 1, ""b"": 2}
d[""c""] = 3
print(d[""a""])
print(len(d))
");
        
        _engine.Eval(script);
        
        Assert.Equal("1\n3", GetConsoleOutput());
    }

    [Fact]
    public void Eval_WhileLoop()
    {
        var script = CreateTempScript(@"
i = 0
while i < 3:
    print(i)
    i += 1
");
        
        _engine.Eval(script);
        
        Assert.Equal("0\n1\n2", GetConsoleOutput());
    }

    [Fact]
    public void Eval_MultipleScripts_SequentialExecution()
    {
        var script1 = CreateTempScript("print(\"script1\")", "script1.naja");
        ClearConsoleOutput();
        
        _engine.Eval(script1);
        Assert.Equal("script1", GetConsoleOutput());
        
        ClearConsoleOutput();
        
        var script2 = CreateTempScript("print(\"script2\")", "script2.naja");
        _engine.Eval(script2);
        Assert.Equal("script2", GetConsoleOutput());
    }

    [Fact]
    public void Eval_EmptyScript_ExecutesWithoutError()
    {
        var script = CreateTempScript("# Just a comment");
        
        _engine.Eval(script);
        
        // Should execute without throwing
        Assert.Equal("", GetConsoleOutput());
    }

    [Fact]
    public void Eval_LambdaExpression()
    {
        var script = CreateTempScript(@"
double = lambda x: x * 2
print(double(21))
");
        
        _engine.Eval(script);
        
        Assert.Equal("42", GetConsoleOutput());
    }

    [Fact]
    public void Eval_ListOperations()
    {
        // Note: Negative indexing (lst[-1]) is not supported in Naja
        var script = CreateTempScript(@"
lst = [1, 2, 3]
lst.append(4)
print(len(lst))
print(lst[0])
print(lst[3])
");
        
        _engine.Eval(script);
        
        Assert.Equal("4\n1\n4", GetConsoleOutput());
    }

    [Fact]
    public void Eval_TupleUnpacking()
    {
        var script = CreateTempScript(@"
a, b = 1, 2
print(a)
print(b)
");
        
        _engine.Eval(script);
        
        Assert.Equal("1\n2", GetConsoleOutput());
    }

    [Fact]
    public void Eval_MatchCase()
    {
        var script = CreateTempScript(@"
for x in [1, 2, 99]:
    match x:
        case 1:
            print(""one"")
        case 2:
            print(""two"")
        case _:
            print(""other"")
");
        
        _engine.Eval(script);
        
        Assert.Equal("one\ntwo\nother", GetConsoleOutput());
    }

    [Fact]
    public void Eval_WalrusOperator()
    {
        var script = CreateTempScript(@"
if (n := 10) > 5:
    print(n)
");
        
        _engine.Eval(script);
        
        Assert.Equal("10", GetConsoleOutput());
    }
}

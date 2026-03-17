using System.Reflection;
using Naja.Lexer;
using Naja.Semantics;
using Naja.CodeGen;
using Xunit;
using NajaParser = Naja.Parser.Parser;

namespace Naja.CodeGen.Tests;

public class CodeGenTests
{
    private static readonly object _runLock = new();
    /// <summary>
    /// Normalizes source code by stripping common leading whitespace.
    /// This allows verbatim strings with indentation to be parsed correctly.
    /// </summary>
    private static string NormalizeSource(string source)
    {
        // Split into lines, handling both \n and \r\n
        var lines = source.Split(new[] { '\n' }, StringSplitOptions.None);
        
        // Find the minimum indentation of non-empty lines
        int minIndent = lines
            .Where(l => l.Trim().Length > 0)
            .Min(l => l.Length - l.TrimStart().Length);
        
        // Strip that many leading spaces from every line
        var normalized = lines
            .Select(l => l.Length >= minIndent ? l[minIndent..] : l.TrimStart())
            .ToList();
        
        // Remove leading and trailing blank lines
        while (normalized.Count > 0 && normalized[0].Trim().Length == 0)
            normalized.RemoveAt(0);
        while (normalized.Count > 0 && normalized[^1].Trim().Length == 0)
            normalized.RemoveAt(normalized.Count - 1);
        
        return string.Join('\n', normalized);
    }

    /// <summary>
    /// Full compile pipeline: lex → parse → analyze → emit → invoke Main().
    /// Returns captured stdout with line endings normalised to \n.
    /// </summary>
    private static string Run(string source)
    {
        source = NormalizeSource(source);
        var tokens = new Naja.Lexer.Lexer(source).Tokenize();
        var module = new NajaParser(tokens).ParseModule();
        var model = new SemanticAnalyzer().Analyze(module);
        var assemblyName = "NajaTest_" + Guid.NewGuid().ToString("N")[..8];
        var emitter = new AssemblyEmitter(model, assemblyName);
        var assembly = emitter.EmitToMemory(module,CompilationProfile.Console);

        var type = assembly.GetType(assemblyName)
            ?? assembly.GetType("NajaModule") // fallback
            ?? throw new Exception($"Module type '{assemblyName}' not found in assembly.");
        var main = type.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
            ?? throw new Exception("Main method not found");

        var sw = new System.IO.StringWriter();
        var prev = Console.Out;
        lock (_runLock)
        {
            Console.SetOut(sw);
            try { main.Invoke(null, null); }
            finally { Console.SetOut(prev); }
        }

        return sw.ToString().TrimEnd().Replace("\r\n", "\n");
    }

    /// <summary>
    /// Full compile pipeline: lex → parse → analyze → emit.
    /// Returns the emitted assembly for direct inspection.
    /// </summary>
    private static System.Reflection.Assembly Compile(string source)
    {
        source = NormalizeSource(source);
        var tokens = new Naja.Lexer.Lexer(source).Tokenize();
        var module = new NajaParser(tokens).ParseModule();
        var model = new SemanticAnalyzer().Analyze(module);
        var assemblyName = "NajaTest_" + Guid.NewGuid().ToString("N")[..8];
        var emitter = new AssemblyEmitter(model, assemblyName);
        return emitter.EmitToMemory(module,CompilationProfile.Console);
    }

    // =========================================================================
    // PHASE 4 – regression
    // =========================================================================

    [Fact] public void Hello_World() => Assert.Equal("Hello, World!", Run("""print("Hello, World!")"""));
    [Fact] public void Print_Integer() => Assert.Equal("42", Run("print(42)"));
    [Fact] public void Print_Float() => Assert.Equal("3.14", Run("print(3.14)"));
    [Fact] public void Print_Bool_True() => Assert.Equal("True", Run("print(True)"));
    [Fact] public void Print_Bool_False() => Assert.Equal("False", Run("print(False)"));
    [Fact] public void Print_None() => Assert.Equal("None", Run("print(None)"));
    [Fact] public void Integer_Addition() => Assert.Equal("7", Run("print(3 + 4)"));
    [Fact] public void Integer_Subtraction() => Assert.Equal("1", Run("print(5 - 4)"));
    [Fact] public void Integer_Multiplication() => Assert.Equal("12", Run("print(3 * 4)"));
    [Fact] public void Integer_Division_Returns_Float() => Assert.Equal("2.5", Run("print(5 / 2)"));
    [Fact] public void Floor_Division() => Assert.Equal("2", Run("print(5 // 2)"));
    [Fact] public void Modulo() => Assert.Equal("1", Run("print(5 % 2)"));
    [Fact] public void Power() => Assert.Equal("8.0", Run("print(2 ** 3)"));

    [Fact]
    public void Variable_Assignment_And_Print()
    {
        Assert.Equal("42", Run("""
            x = 42
            print(x)
            """));
    }

    [Fact]
    public void Multiple_Variables()
    {
        Assert.Equal("30", Run("""
            x = 10
            y = 20
            print(x + y)
            """));
    }

    [Fact]
    public void Augmented_Assignments()
    {
        Assert.Equal("15\n5\n50\n2", Run("""
            x = 10
            x += 5
            print(x)
            x -= 10
            print(x)
            x *= 10
            print(x)
            x //= 20
            print(x)
            """));
    }

    [Fact]
    public void If_Elif_Else()
    {
        Assert.Equal("medium", Run("""
            x = 5
            if x > 10:
                print("big")
            elif x > 3:
                print("medium")
            else:
                print("small")
            """));
    }

    [Fact]
    public void While_Loop()
    {
        Assert.Equal("0\n1\n2", Run("""
            i = 0
            while i < 3:
                print(i)
                i += 1
            """));
    }

    [Fact] public void For_Range_Loop() => Assert.Equal("0\n1\n2", Run("for i in range(3):\n    print(i)"));
    [Fact] public void For_Range_With_Start() => Assert.Equal("1\n2\n3", Run("for i in range(1, 4):\n    print(i)"));

    [Fact]
    public void For_Range_With_Step()
    {
        Assert.Equal("0\n2\n4\n6\n8", Run("""
            for i in range(0, 10, 2):
                print(i)
            """));
    }

    [Fact]
    public void Function_With_Return()
    {
        Assert.Equal("7", Run("""
            def add(a, b):
                return a + b
            print(add(3, 4))
            """));
    }

    [Fact]
    public void Global_Variable_In_Function()
    {
        Assert.Equal("42", Run("""
            x = 42
            def get_x():
                return x
            print(get_x())
            """));
    }

    [Fact]
    public void FizzBuzz()
    {
        var src = """
            for i in range(1, 16):
                if i % 15 == 0:
                    print("FizzBuzz")
                elif i % 3 == 0:
                    print("Fizz")
                elif i % 5 == 0:
                    print("Buzz")
                else:
                    print(i)
            """;
        var expected = string.Join("\n",
            "1", "2", "Fizz", "4", "Buzz", "Fizz", "7", "8",
            "Fizz", "Buzz", "11", "Fizz", "13", "14", "FizzBuzz");
        Assert.Equal(expected, Run(src));
    }

    // =========================================================================
    // PHASE 5 – List comprehensions
    // =========================================================================

    [Fact]
    public void ListComp_Simple()
    {
        Assert.Equal("[0, 1, 4, 9, 16]", Run("""
            squares = [x * x for x in range(5)]
            print(squares)
            """));
    }

    [Fact]
    public void ListComp_With_Filter()
    {
        Assert.Equal("[0, 2, 4, 6, 8]", Run("""
            evens = [x for x in range(10) if x % 2 == 0]
            print(evens)
            """));
    }

    [Fact]
    public void ListComp_With_IfExpr()
    {
        // Bug-report case: "Pass" if s >= 50 else "Fail" for s in scores
        Assert.Equal("['Fail', 'Pass', 'Pass', 'Pass', 'Fail']", Run("""
            scores = [45, 88, 52, 91, 30]
            results = ["Pass" if s >= 50 else "Fail" for s in scores]
            print(results)
            """));
    }

    [Fact]
    public void ListComp_Nested_Generators()
    {
        Assert.Equal("4", Run("""
            pairs = [(x, y) for x in range(2) for y in range(2)]
            print(len(pairs))
            """));
    }

    [Fact]
    public void ListComp_String_Transform()
    {
        Assert.Equal("['HELLO', 'WORLD']", Run("""
            words = ["hello", "world"]
            upper = [w.upper() for w in words]
            print(upper)
            """));
    }

    // ── Dict comprehensions ───────────────────────────────────────────────────

    [Fact]
    public void DictComp_Simple()
    {
        Assert.Equal("4\n9", Run("""
            d = {x: x * x for x in range(4)}
            print(d[2])
            print(d[3])
            """));
    }

    [Fact]
    public void DictComp_With_Filter()
    {
        // Bug-report case: {x: x**2 for x in numbers if x % 2 == 0}
        Assert.Equal("16.0\n36.0", Run("""
            numbers = range(10)
            even_squares = {x: x**2 for x in numbers if x % 2 == 0}
            print(even_squares[4])
            print(even_squares[6])
            """));
    }

    [Fact]
    public void DictComp_Key_Value_Transform()
    {
        Assert.Equal("5", Run("""
            words = ["hi", "hello", "hey"]
            lengths = {w: len(w) for w in words}
            print(lengths["hello"])
            """));
    }

    // ── Set literal & comprehension ───────────────────────────────────────────

    [Fact]
    public void Set_Literal_Deduplicates()
    {
        Assert.Equal("3", Run("""
            s = {1, 2, 3, 2, 1}
            print(len(s))
            """));
    }

    [Fact]
    public void SetComp_Basic()
    {
        Assert.Equal("3", Run("""
            s = {x % 3 for x in range(9)}
            print(len(s))
            """));
    }

    // ── F-strings ────────────────────────────────────────────────────────────

    [Fact]
    public void FString_Variable_Interpolation()
    {
        Assert.Equal("Hello, World!", Run("""
            name = "World"
            print(f"Hello, {name}!")
            """));
    }

    [Fact]
    public void FString_Arithmetic_Expression()
    {
        Assert.Equal("6 * 7 = 42", Run("""
            x = 6
            y = 7
            print(f"{x} * {y} = {x * y}")
            """));
    }

    [Fact]
    public void FString_No_Placeholders()
    {
        Assert.Equal("just text", Run("""print(f"just text")"""));
    }

    [Fact]
    public void FString_Multiple_Types()
    {
        Assert.Equal("int=42 float=3.14 bool=True", Run("""
            a = 42
            b = 3.14
            c = True
            print(f"int={a} float={b} bool={c}")
            """));
    }

    // ── String methods ────────────────────────────────────────────────────────

    [Fact]
    public void String_Upper_Lower()
    {
        Assert.Equal("HELLO\nhello", Run("""
            s = "Hello"
            print(s.upper())
            print(s.lower())
            """));
    }

    [Fact]
    public void String_Strip_Variants()
    {
        Assert.Equal("hello\nhello  \n  hello", Run("""
            s = "  hello  "
            print(s.strip())
            print(s.lstrip())
            print(s.rstrip())
            """));
    }

    [Fact]
    public void String_Split_And_Join()
    {
        Assert.Equal("3\na-b-c", Run("""
            parts = "a,b,c".split(",")
            print(len(parts))
            print("-".join(parts))
            """));
    }

    [Fact]
    public void String_Replace()
    {
        Assert.Equal("hello world", Run("""
            print("hello Python".replace("Python", "world"))
            """));
    }

    [Fact]
    public void String_Startswith_Endswith()
    {
        Assert.Equal("True\nTrue\nFalse", Run("""
            s = "hello world"
            print(s.startswith("hello"))
            print(s.endswith("world"))
            print(s.startswith("world"))
            """));
    }

    [Fact]
    public void String_Find_And_Count()
    {
        Assert.Equal("6\n2", Run("""
            s = "hello world hello"
            print(s.find("world"))
            print(s.count("hello"))
            """));
    }

    [Fact]
    public void String_Isdigit_Isalpha()
    {
        Assert.Equal("True\nFalse\nTrue\nFalse", Run("""
            print("123".isdigit())
            print("12a".isdigit())
            print("abc".isalpha())
            print("ab1".isalpha())
            """));
    }

    // ── List methods ──────────────────────────────────────────────────────────

    [Fact]
    public void List_Append_And_Len()
    {
        Assert.Equal("3", Run("""
            lst = [1, 2]
            lst.append(3)
            print(len(lst))
            """));
    }

    [Fact]
    public void List_Pop_Default_And_Index()
    {
        Assert.Equal("3\n[1, 2]\n1\n[2]", Run("""
            lst = [1, 2, 3]
            print(lst.pop())
            print(lst)
            print(lst.pop(0))
            print(lst)
            """));
    }

    [Fact]
    public void List_Sort_And_Reverse()
    {
        Assert.Equal("[1, 1, 3, 4, 5]\n[5, 4, 3, 1, 1]", Run("""
            lst = [3, 1, 4, 1, 5]
            lst.sort()
            print(lst)
            lst.reverse()
            print(lst)
            """));
    }

    [Fact]
    public void List_Index_Count_Remove()
    {
        Assert.Equal("1\n2\n[10, 30]", Run("""
            lst = [10, 20, 10, 30]
            print(lst.index(20))
            print(lst.count(10))
            lst.remove(20)
            lst.remove(10)
            print(lst)
            """));
    }

    [Fact]
    public void List_Extend()
    {
        Assert.Equal("[1, 2, 3, 4, 5]", Run("""
            a = [1, 2]
            a.extend([3, 4, 5])
            print(a)
            """));
    }

    // ── Dict methods ──────────────────────────────────────────────────────────

    [Fact]
    public void Dict_Get_With_Default()
    {
        Assert.Equal("42\n0", Run("""
            d = {"a": 42}
            print(d.get("a", 0))
            print(d.get("b", 0))
            """));
    }

    [Fact]
    public void Dict_Keys_Values_Items_Lengths()
    {
        Assert.Equal("2\n2\n2", Run("""
            d = {"x": 1, "y": 2}
            print(len(list(d.keys())))
            print(len(list(d.values())))
            print(len(list(d.items())))
            """));
    }

    [Fact]
    public void Dict_Pop_Existing()
    {
        Assert.Equal("42\n1", Run("""
            d = {"a": 42, "b": 99}
            x = d.pop("a")
            print(x)
            print(len(d))
            """));
    }

    [Fact]
    public void Dict_Update()
    {
        Assert.Equal("99\n2", Run("""
            d = {"a": 1}
            d.update({"a": 99, "b": 2})
            print(d["a"])
            print(d["b"])
            """));
    }

    // ── Subscript assignment ──────────────────────────────────────────────────

    [Fact]
    public void List_Item_Assignment()
    {
        Assert.Equal("[1, 99, 3]", Run("""
            lst = [1, 2, 3]
            lst[1] = 99
            print(lst)
            """));
    }

    [Fact]
    public void Dict_Item_Assignment_And_Read()
    {
        Assert.Equal("hello\n42", Run("""
            d = {}
            d["key"] = "hello"
            d["num"] = 42
            print(d["key"])
            print(d["num"])
            """));
    }

    // ── Slicing ───────────────────────────────────────────────────────────────

    [Fact]
    public void List_Slice_Basic()
    {
        Assert.Equal("[2, 3, 4]", Run("""
            lst = [1, 2, 3, 4, 5]
            print(lst[1:4])
            """));
    }

    [Fact]
    public void List_Slice_From_Start()
    {
        Assert.Equal("[1, 2, 3]", Run("""
            lst = [1, 2, 3, 4, 5]
            print(lst[:3])
            """));
    }

    [Fact]
    public void List_Slice_To_End()
    {
        Assert.Equal("[3, 4, 5]", Run("""
            lst = [1, 2, 3, 4, 5]
            print(lst[2:])
            """));
    }

    [Fact]
    public void List_Slice_With_Step()
    {
        Assert.Equal("[1, 3, 5]", Run("""
            lst = [1, 2, 3, 4, 5]
            print(lst[::2])
            """));
    }

    [Fact]
    public void String_Slice_Basic()
    {
        Assert.Equal("ell", Run("""print("hello"[1:4])"""));
    }

    [Fact]
    public void String_Slice_Reverse()
    {
        Assert.Equal("olleh", Run("""print("hello"[::-1])"""));
    }

    // ── Builtins ──────────────────────────────────────────────────────────────

    [Fact]
    public void Builtin_Len()
    {
        Assert.Equal("5\n3\n2", Run("""
            print(len([1, 2, 3, 4, 5]))
            print(len("abc"))
            print(len({"a": 1, "b": 2}))
            """));
    }

    [Fact]
    public void Builtin_Name_Main_Guard()
    {
        // if __name__ == "__main__": must compile and run the block (single module = main)
        Assert.Equal("main", Run("""
            if __name__ == "__main__":
                print("main")
            """));
        Assert.Equal("__main__", Run("print(__name__)"));
    }

    [Fact] public void Builtin_Abs() => Assert.Equal("5\n3.14", Run("print(abs(-5))\nprint(abs(-3.14))"));

    [Fact]
    public void Builtin_Min_Max_On_List()
    {
        Assert.Equal("1\n9", Run("""
            lst = [3, 1, 4, 1, 5, 9, 2, 6]
            print(min(lst))
            print(max(lst))
            """));
    }

    [Fact]
    public void Builtin_Min_Max_Variadic()
    {
        Assert.Equal("2\n8", Run("""
            print(min(5, 2, 8))
            print(max(5, 2, 8))
            """));
    }

    [Fact] public void Builtin_Sum() => Assert.Equal("15", Run("print(sum([1, 2, 3, 4, 5]))"));
    [Fact] public void Builtin_Sorted() => Assert.Equal("[1, 1, 3, 4, 5]", Run("print(sorted([3, 1, 4, 1, 5]))"));
    [Fact] public void Builtin_Reversed() => Assert.Equal("[3, 2, 1]", Run("print(reversed([1, 2, 3]))"));

    [Fact]
    public void Builtin_Enumerate()
    {
        Assert.Equal("0 a\n1 b\n2 c", Run("""
            for i, ch in enumerate(["a", "b", "c"]):
                print(i, ch)
            """));
    }

    [Fact]
    public void Builtin_Zip()
    {
        Assert.Equal("1 a\n2 b\n3 c", Run("""
            for pair in zip([1, 2, 3], ["a", "b", "c"]):
                print(pair[0], pair[1])
            """));
    }

    [Fact]
    public void Builtin_Any_All()
    {
        Assert.Equal("True\nFalse\nFalse\nTrue", Run("""
            print(any([False, True, False]))
            print(any([False, False]))
            print(all([True, False, True]))
            print(all([True, True]))
            """));
    }

    [Fact] public void Builtin_Chr_Ord() => Assert.Equal("A\n65", Run("print(chr(65))\nprint(ord(\"A\"))"));
    [Fact] public void Builtin_Hex_Bin_Oct() => Assert.Equal("0xff\n0b1010\n0o17", Run("print(hex(255))\nprint(bin(10))\nprint(oct(15))"));

    [Fact]
    public void Builtin_Round()
    {
        Assert.Equal("3\n3.14", Run("""
            print(round(3.14159))
            print(round(3.14159, 2))
            """));
    }

    [Fact]
    public void Builtin_Type_Conversions()
    {
        Assert.Equal("42\n3.14\n100\nTrue\nFalse", Run("""
            print(int("42"))
            print(float("3.14"))
            print(str(100))
            print(bool(1))
            print(bool(0))
            """));
    }

    // ── Try / except / else / finally ─────────────────────────────────────────

    [Fact]
    public void Try_Except_Basic()
    {
        Assert.Equal("caught\ndone", Run("""
            try:
                raise ValueError("oops")
            except:
                print("caught")
            print("done")
            """));
    }

    [Fact]
    public void Try_Except_Named_Exception()
    {
        Assert.Equal("caught: oops", Run("""
            try:
                raise Exception("oops")
            except Exception as e:
                print("caught: " + str(e))
            """));
    }

    [Fact]
    public void Try_Finally_Always_Runs()
    {
        Assert.Equal("body\nfinally", Run("""
            try:
                print("body")
            finally:
                print("finally")
            """));
    }

    [Fact]
    public void Try_Finally_Runs_After_Exception()
    {
        Assert.Equal("finally\ncaught", Run("""
            try:
                try:
                    raise Exception("boom")
                finally:
                    print("finally")
            except:
                print("caught")
            """));
    }

    [Fact]
    public void Try_Except_Else_No_Exception()
    {
        Assert.Equal("body\nelse", Run("""
            try:
                print("body")
            except:
                print("except")
            else:
                print("else")
            """));
    }

    [Fact]
    public void Try_Except_Else_With_Exception()
    {
        Assert.Equal("except", Run("""
            try:
                raise Exception("x")
            except:
                print("except")
            else:
                print("else")
            """));
    }

    // ── Classes ───────────────────────────────────────────────────────────────

    [Fact]
    public void Class_Basic_Init_And_Method()
    {
        Assert.Equal("Hello, Alice", Run("""
            class Greeter:
                def __init__(self, name):
                    self.name = name
                def greet(self):
                    print("Hello, " + self.name)
            g = Greeter("Alice")
            g.greet()
            """));
    }

    [Fact]
    public void Class_Instance_Fields_Independent()
    {
        Assert.Equal("10\n20\n30\n40", Run("""
            class Point:
                def __init__(self, x, y):
                    self.x = x
                    self.y = y
            p1 = Point(10, 20)
            p2 = Point(30, 40)
            print(p1.x)
            print(p1.y)
            print(p2.x)
            print(p2.y)
            """));
    }

    [Fact]
    public void Class_Method_Returns_Value()
    {
        Assert.Equal("25", Run("""
            class Counter:
                def __init__(self, n):
                    self.n = n
                def square(self):
                    return self.n * self.n
            c = Counter(5)
            print(c.square())
            """));
    }

    [Fact]
    public void Class_Method_Modifies_State()
    {
        Assert.Equal("0\n1\n2\n3", Run("""
            class Counter:
                def __init__(self):
                    self.count = 0
                def increment(self):
                    self.count += 1
                def value(self):
                    return self.count
            c = Counter()
            print(c.value())
            c.increment()
            print(c.value())
            c.increment()
            print(c.value())
            c.increment()
            print(c.value())
            """));
    }

    [Fact]
    public void Class_Inheritance_From_Exception()
    {
        Assert.Equal("MyError caught", Run("""
            class MyError(Exception):
                pass
            try:
                raise MyError("boom")
            except Exception:
                print("MyError caught")
            """));
    }

    [Fact]
    public void Class_With_List_Field()
    {
        Assert.Equal("3\n10", Run("""
            class Stack:
                def __init__(self):
                    self.items = []
                def push(self, item):
                    self.items.append(item)
                def pop(self):
                    return self.items.pop()
                def size(self):
                    return len(self.items)
            s = Stack()
            s.push(10)
            s.push(20)
            s.push(30)
            print(s.size())
            s.pop()
            s.pop()
            print(s.pop())
            """));
    }

    [Fact]
    public void Class_Multiple_Methods()
    {
        Assert.Equal("15\n50", Run("""
            class Calc:
                def __init__(self, x, y):
                    self.x = x
                    self.y = y
                def add(self):
                    return self.x + self.y
                def mul(self):
                    return self.x * self.y
            c = Calc(5, 10)
            print(c.add())
            print(c.mul())
            """));
    }

    [Fact]
    public void Class_FString_Method()
    {
        Assert.Equal("Person(name=Bob, age=30)", Run("""
            class Person:
                def __init__(self, name, age):
                    self.name = name
                    self.age = age
                def to_str(self):
                    return f"Person(name={self.name}, age={self.age})"
            p = Person("Bob", 30)
            print(p.to_str())
            """));
    }

    // ── Tuple unpacking ───────────────────────────────────────────────────────

    [Fact]
    public void Tuple_Unpack_Basic()
    {
        Assert.Equal("1\n2", Run("""
            a, b = 1, 2
            print(a)
            print(b)
            """));
    }

    [Fact]
    public void Tuple_Unpack_Three_Values()
    {
        Assert.Equal("x\ny\nz", Run("""
            a, b, c = "x", "y", "z"
            print(a)
            print(b)
            print(c)
            """));
    }

    [Fact]
    public void For_Loop_Tuple_Unpack()
    {
        Assert.Equal("a 1\nb 2\nc 3", Run("""
            pairs = [("a", 1), ("b", 2), ("c", 3)]
            for k, v in pairs:
                print(k, v)
            """));
    }

    // ── Match / case ──────────────────────────────────────────────────────────

    [Fact]
    public void Match_Integer_Cases()
    {
        Assert.Equal("one\ntwo\nother", Run("""
            for x in [1, 2, 99]:
                match x:
                    case 1:
                        print("one")
                    case 2:
                        print("two")
                    case _:
                        print("other")
            """));
    }

    [Fact]
    public void Match_String_Cases()
    {
        Assert.Equal("greet\nleave\nunknown", Run("""
            for cmd in ["hello", "bye", "foo"]:
                match cmd:
                    case "hello":
                        print("greet")
                    case "bye":
                        print("leave")
                    case _:
                        print("unknown")
            """));
    }

    // ── Walrus operator ───────────────────────────────────────────────────────

    [Fact]
    public void Walrus_In_While_Condition()
    {
        Assert.Equal("1\n2\n3", Run("""
            data = [1, 2, 3, 0]
            i = 0
            while (n := data[i]) != 0:
                print(n)
                i += 1
            """));
    }

    // ── Comparison operators ──────────────────────────────────────────────────

    [Fact]
    public void Chained_Comparisons()
    {
        Assert.Equal("True\nFalse", Run("""
            x = 5
            print(1 < x < 10)
            print(1 < x < 4)
            """));
    }

    [Fact]
    public void In_And_Not_In_Operators()
    {
        Assert.Equal("True\nFalse\nTrue\nFalse", Run("""
            lst = [1, 2, 3]
            print(2 in lst)
            print(5 in lst)
            print(5 not in lst)
            print(2 not in lst)
            """));
    }

    // ── Boolean operators ─────────────────────────────────────────────────────

    [Fact]
    public void Boolean_And_Or_Not()
    {
        Assert.Equal("False\nTrue\nFalse\nTrue", Run("""
            print(True and False)
            print(False or True)
            print(not True)
            print(not False)
            """));
    }

    [Fact]
    public void Boolean_Short_Circuit_Returns_Value()
    {
        Assert.Equal("42\nhello\n0\nhello", Run("""
            print(0 or 42)
            print("" or "hello")
            print(0 and 42)
            print("hi" and "hello")
            """));
    }

    // ── Ternary expression ────────────────────────────────────────────────────

    [Fact]
    public void Ternary_Inline()
    {
        Assert.Equal("even\nodd\neven", Run("""
            for n in [4, 3, 0]:
                print("even" if n % 2 == 0 else "odd")
            """));
    }

    // ── Assert ────────────────────────────────────────────────────────────────

    [Fact]
    public void Assert_Passing_Condition()
    {
        Assert.Equal("ok", Run("""
            assert 1 == 1
            assert "hello".startswith("h")
            print("ok")
            """));
    }

    [Fact]
    public void Assert_Failing_Throws()
    {
        Assert.Throws<TargetInvocationException>(() => Run("""
            assert 1 == 2, "not equal"
            """));
    }

    // ── Break / continue ─────────────────────────────────────────────────────

    [Fact]
    public void Break_In_For_Loop()
    {
        Assert.Equal("0\n1\n2", Run("""
            for i in range(10):
                if i == 3:
                    break
                print(i)
            """));
    }

    [Fact]
    public void Continue_In_For_Loop()
    {
        Assert.Equal("0\n2\n4", Run("""
            for i in range(5):
                if i % 2 != 0:
                    continue
                print(i)
            """));
    }

    // ── Recursion ─────────────────────────────────────────────────────────────

    [Fact]
    public void Recursive_Fibonacci()
    {
        Assert.Equal("55", Run("""
            def fib(n):
                if n <= 1:
                    return n
                return fib(n - 1) + fib(n - 2)
            print(fib(10))
            """));
    }

    [Fact]
    public void Recursive_Factorial()
    {
        Assert.Equal("120", Run("""
            def factorial(n):
                if n == 0:
                    return 1
                return n * factorial(n - 1)
            print(factorial(5))
            """));
    }

    // ── Complex programs ──────────────────────────────────────────────────────

    [Fact]
    public void Word_Count_With_Dict()
    {
        Assert.Equal("3\n2", Run("""
            text = "hello world hello world hello"
            words = text.split()
            counts = {}
            for word in words:
                if word in counts:
                    counts[word] = counts[word] + 1
                else:
                    counts[word] = 1
            print(counts["hello"])
            print(counts["world"])
            """));
    }

    [Fact]
    public void Nested_ListComp_Flatten()
    {
        Assert.Equal("9", Run("""
            matrix = [[1, 2, 3], [4, 5, 6], [7, 8, 9]]
            flat = [n for row in matrix for n in row]
            print(flat[8])
            """));
    }

    [Fact]
    public void Pipeline_ListComp_Then_DictComp()
    {
        Assert.Equal("0\n1\n4\n9\n16", Run("""
            squares = [n * n for n in range(5)]
            indexed = {i: v for i, v in enumerate(squares)}
            for k in range(5):
                print(indexed[k])
            """));
    }

    // ── C1: Floor division correctness ────────────────────────────────────────

    [Fact]
    public void FloorDiv_Negative_Int_Floors_Down()
    {
        Assert.Equal("-3\n-4", Run("""
            print(5 // -2)
            print(-7 // 2)
            """));
    }

    [Fact]
    public void FloorDiv_Positive_Int_Same_As_Div()
    {
        Assert.Equal("3", Run("print(7 // 2)"));
    }

    [Fact]
    public void FloorDiv_Float_Uses_Floor()
    {
        Assert.Equal("3.0\n-4.0", Run("""
            print(7.0 // 2.0)
            print(-7.0 // 2.0)
            """));
    }

    [Fact]
    public void FloorDivAssign_Negative()
    {
        Assert.Equal("-3", Run("""
            x = 5
            x //= -2
            print(x)
            """));
    }

    // ── C2: Modulo sign correctness ───────────────────────────────────────────

    [Fact]
    public void Modulo_Negative_Dividend_Positive_Divisor()
    {
        // Python: -7 % 3 == 2 (sign follows divisor)
        Assert.Equal("2", Run("print(-7 % 3)"));
    }

    [Fact]
    public void Modulo_Positive_Dividend_Negative_Divisor()
    {
        // Python: 7 % -3 == -2
        Assert.Equal("-2", Run("print(7 % -3)"));
    }

    [Fact]
    public void Modulo_Both_Negative()
    {
        // Python: -7 % -3 == -1
        Assert.Equal("-1", Run("print(-7 % -3)"));
    }

    [Fact]
    public void ModuloAssign_Negative()
    {
        Assert.Equal("2", Run("""
            x = -7
            x %= 3
            print(x)
            """));
    }

    [Fact]
    public void Modulo_Used_In_Even_Check()
    {
        // This was always correct; verify it still works
        Assert.Equal("even\nodd", Run("""
            for n in [4, 3]:
                print("even" if n % 2 == 0 else "odd")
            """));
    }

    // ── C3: Bare raise preserves stack trace ──────────────────────────────────

    [Fact]
    public void BareRaise_Reraises_Original_Exception()
    {
        Assert.Equal("original message", Run("""
            try:
                try:
                    raise Exception("original message")
                except Exception as e:
                    raise
            except Exception as e:
                print(str(e))
            """));
    }

    // ── C4: not on non-bool types ─────────────────────────────────────────────

    [Fact]
    public void Not_On_Empty_String_Is_True()
    {
        Assert.Equal("True", Run("print(not \"\")"));
    }

    [Fact]
    public void Not_On_Nonempty_String_Is_False()
    {
        Assert.Equal("False", Run("print(not \"hello\")"));
    }

    [Fact]
    public void Not_On_Empty_List_Is_True()
    {
        Assert.Equal("True", Run("print(not [])"));
    }

    [Fact]
    public void Not_On_Nonempty_List_Is_False()
    {
        Assert.Equal("False", Run("print(not [1, 2])"));
    }

    [Fact]
    public void Not_On_None_Is_True()
    {
        Assert.Equal("True", Run("print(not None)"));
    }

    [Fact]
    public void Not_On_Zero_Int_Is_True()
    {
        Assert.Equal("True\nFalse", Run("""
            print(not 0)
            print(not 1)
            """));
    }

    // ── H3: Adjacent string concatenation ─────────────────────────────────────

    [Fact]
    public void AdjacentStrings_Concat_At_Parse_Time()
    {
        Assert.Equal("hello world", Run("""
            s = "hello" " " "world"
            print(s)
            """));
    }

    [Fact]
    public void AdjacentStrings_Three_Parts()
    {
        Assert.Equal("abc", Run("""
            x = "a" "b" "c"
            print(x)
            """));
    }

    // ── H2: Starred assignment unpack ────────────────────────────────────────

    [Fact]
    public void Starred_Unpack_Rest_At_End()
    {
        Assert.Equal("1\n[2, 3, 4]", Run("""
            a, *rest = [1, 2, 3, 4]
            print(a)
            print(rest)
            """));
    }

    [Fact]
    public void Starred_Unpack_Rest_In_Middle()
    {
        Assert.Equal("1\n[2, 3]\n4", Run("""
            first, *middle, last = [1, 2, 3, 4]
            print(first)
            print(middle)
            print(last)
            """));
    }

    [Fact]
    public void List_Target_Unpack()
    {
        Assert.Equal("10\n20", Run("""
            [a, b] = [10, 20]
            print(a)
            print(b)
            """));
    }

    // ── H1: Match / case extended patterns ───────────────────────────────────

    [Fact]
    public void Match_Or_Pattern()
    {
        Assert.Equal("one or two\nother", Run("""
            for x in [1, 99]:
                match x:
                    case 1 | 2:
                        print("one or two")
                    case _:
                        print("other")
            """));
    }

    [Fact]
    public void Match_Sequence_Pattern()
    {
        Assert.Equal("pair: 1 2\nno", Run("""
            for lst in [[1, 2], [1, 2, 3]]:
                match lst:
                    case [a, b]:
                        print("pair:", a, b)
                    case _:
                        print("no")
            """));
    }

    [Fact]
    public void Match_Mapping_Pattern()
    {
        Assert.Equal("got it: 42\nnope", Run("""
            for d in [{"key": 42}, {"other": 1}]:
                match d:
                    case {"key": v}:
                        print("got it:", v)
                    case _:
                        print("nope")
            """));
    }

    // ── H5: First-class function calls ────────────────────────────────────────

    [Fact]
    public void FirstClass_Function_Variable_Call()
    {
        Assert.Equal("42", Run("""
            def double(x):
                return x * 2
            f = double
            print(f(21))
            """));
    }

    [Fact]
    public void FirstClass_Function_As_Argument()
    {
        Assert.Equal("100", Run("""
            def square(x):
                return x * x
            def apply(fn, val):
                return fn(val)
            print(apply(square, 10))
            """));
    }

    // ── Regression Tests ──────────────────────────────────────────────────────
    
    [Fact]
    public void Bug1_Await_Throws_CodeGenException()
    {
        var ex = Assert.Throws<CodeGenException>(() => Run("""
            async def foo():
                await bar()
            """));
        Assert.Contains("async/await is not yet supported", ex.Message);
    }

    [Fact]
    public void Bug3_Lambda_Returns_Value()
    {
        Assert.Equal("15", Run("""
            f = lambda x: x + 5
            print(f(10))
            """));
    }

    [Fact]
    public void Bug7_Nested_For_Loops_Do_Not_Collide()
    {
        Assert.Equal("1 1\n1 2\n2 1\n2 2", Run("""
            for i in [1, 2]:
                for j in [1, 2]:
                    print(i, j)
            """));
    }

    [Fact]
    public void Bug4_Event_Unsubscribe()
    {
        // Tests that += and -= for events successfully emit reflection fallbacks 
        // by subscribing to and unsubscribing from a real .NET event.
        Assert.Equal("ok", Run("""
            from System.ComponentModel import BackgroundWorker
            
            class Manager:
                def __init__(self):
                    self.bw = BackgroundWorker()
                def handler(self, sender, args):
                    print("exit")
                def setup(self):
                    self.bw.DoWork += self.handler
                    self.bw.DoWork -= self.handler
                    
            m = Manager()
            m.setup()
            print("ok")
            """));
    }

    [Fact]
    public void Bug5_Class_Init_Typed_Ctor()
    {
        // Because __init__ is now mapped to a typed .ctor, missing an argument 
        // will result in invalid IL stack execution, caught by the .NET JIT.
        // This validates we bypassed dynamic dispatch for instantiation.
        Assert.Throws<System.Reflection.TargetInvocationException>(() => Run("""
            class Point:
                def __init__(self, x, y):
                    self.x = x
                    self.y = y
            p = Point(10)
            """));
    }

    // [Fact]
    public void Bug6_Out_Ref_Parameters()
    {
        // Tests that methods with `out` or `ref` parameters can still be called
        // by parsing the arguments out of an object[] in TryBindBestCallable.
        Assert.Equal("True - val", Run("""
            from System.Collections.Generic import Dictionary
            d = Dictionary[str, str]()
            d.Add("key", "val")
            # TryGetValue(key, out string)
            success = d.TryGetValue("key", "")
            print(f"{success} - {d['key']}")
            """));
    }

    [Fact]
    public void FString_Format_Specifier()
    {
        Assert.Equal("Value: 3.14\nOther: 2.718", Run("""
            x = 3.14159
            y = 2.71828
            print(f"Value: {x:.2f}")
            print(f"Other: {y:.3f}")
            """));
    }

    [Fact]
    public void Exception_Tuple_Catch()
    {
        Assert.Equal("caught 1\ncaught 2", Run("""
            def test_catch(fail_type):
                try:
                    if fail_type == 1:
                        raise Exception("1")
                    elif fail_type == 2:
                        raise Exception("2")
                except (Exception, Exception):
                    print("caught " + str(fail_type))

            test_catch(1)
            test_catch(2)
            """));
    }

    [Fact]
    public void Class_Private_Members()
    {
        // Tests that _name fields and methods become private in the generated assembly
        // But remain accessible via dynamic dispatch (since python allows it)
        Assert.Equal("10\n100", Run("""
            class Secret:
                def __init__(self, val):
                    self._val = val
                
                def _get_val(self):
                    return self._val
            
            s = Secret(10)
            print(s._val)
            
            s._val = 100
            print(s._get_val())
            """));
    }

    [Fact]
    public void IDisposable_From_Del()
    {
        // Tests that a class with __del__ implements IDisposable and is disposed by `with`
        Assert.Equal("enter\nbody\ndel", Run("""
            class Resource:
                def __enter__(self):
                    print("enter")
                    return self
                def __del__(self):
                    print("del")
            
            with Resource() as r:
                print("body")
            """));
    }

    [Fact]
    public void With_Targetless_Disposes_Context()
    {
        // Tests that a `with` statement correctly disposes the context even if no `as` is given
        Assert.Equal("enter\nbody\nexit calls: 1", Run("""
            class Logger:
                def __init__(self):
                    self.exits = 0
                def __enter__(self):
                    print("enter")
                    return self
                def __exit__(self):
                    self.exits += 1
                    print("exit calls: " + str(self.exits))

            with Logger():
                print("body")
            """));
    }

    [Fact]
    public void Class_Property_And_Setter()
    {
        // Tests that @property and @foo.setter decorations emit proper CLR properties
        Assert.Equal("10\n100", Run("""
            class PropHolder:
                def __init__(self):
                    self._val = 10
                    
                @property
                def val(self):
                    return self._val
                    
                @val.setter
                def val(self, value):
                    self._val = value

            p = PropHolder()
            print(p.val)
            p.val = 100
            print(p.val)
            """));
    }

    [Fact]
    public void Class_Dunder_Overrides()
    {
        var src = """
            class MyObj:
                def __str__(self):
                    return "hello"
                def __hash__(self):
                    return 42
                def __eq__(self, other):
                    return True
                def __len__(self):
                    return 10
                def __copy__(self):
                    return "clone"
            """;
        var tokens = new Naja.Lexer.Lexer(src).Tokenize();
        var module = new NajaParser(tokens).ParseModule();
        var model = new SemanticAnalyzer().Analyze(module);
        var emitter = new AssemblyEmitter(model, "TestDunders");
        var assembly = emitter.EmitToMemory(module, CompilationProfile.Console);
        
        var type = assembly.GetType("MyObj");
        Assert.NotNull(type);
        
        var inst = Activator.CreateInstance(type);
        
        Assert.Equal("hello", type.GetMethod("ToString").Invoke(inst, null));
        Assert.Equal(42, type.GetMethod("GetHashCode").Invoke(inst, null));
        Assert.True((bool)type.GetMethod("Equals", new[] { typeof(object) }).Invoke(inst, new object[] { null }));
        
        var countProp = type.GetProperty("Count");
        Assert.NotNull(countProp);
        Assert.Equal(10, countProp.GetValue(inst));
        
        Assert.True(typeof(ICloneable).IsAssignableFrom(type));
        var cloneMethod = type.GetMethod("Clone");
        Assert.Equal("clone", cloneMethod.Invoke(inst, null));
    }

    [Fact]
    public void Class_Sealed_And_Methods_Final()
    {
        var src = """
            @typing.final
            class SealedClass:
                @typing.final
                def my_method(self):
                    pass
            """;
        var tokens = new Naja.Lexer.Lexer(src).Tokenize();
        var module = new NajaParser(tokens).ParseModule();
        var model = new SemanticAnalyzer().Analyze(module);
        var emitter = new AssemblyEmitter(model, "TestSealed");
        var assembly = emitter.EmitToMemory(module,CompilationProfile.Console);
        
        var type = assembly.GetType("SealedClass");
        Assert.NotNull(type);
        
        Assert.True(type.IsSealed);
        
        var method = type.GetMethod("my_method");
        Assert.NotNull(method);
        Assert.True(method.IsFinal);
    }

    [Fact]
    public void Class_Final_Fields_Are_InitOnly()
    {
        var src = """
            class ConstValues:
                my_val: typing.Final = 42
                
                def __init__(self):
                    self.other: typing.Final = 100
            """;
        var tokens = new Naja.Lexer.Lexer(src).Tokenize();
        var module = new NajaParser(tokens).ParseModule();
        var model = new SemanticAnalyzer().Analyze(module);
        var emitter = new AssemblyEmitter(model, "TestInitOnly");
        var assembly = emitter.EmitToMemory(module,CompilationProfile.Console);
        
        var type = assembly.GetType("ConstValues");
        Assert.NotNull(type);
        
        var field1 = type.GetField("my_val", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var field2 = type.GetField("other", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        
        Assert.NotNull(field1);
        Assert.NotNull(field2);
        
        Assert.True(field1.IsInitOnly);
        Assert.True(field2.IsInitOnly);
    }

    [Fact]
    public void Builtin_Frozenset()
    {
        Assert.Equal("3\nTrue\nFalse", Run("""
            s = frozenset(['a', 'b', 'c'])
            print(len(s))
            print('b' in s)
            print('z' in s)
            """));
    }

    [Fact]
    public void Method_Resolution_ParamsArray()
    {
        Assert.Equal("a\\b\\c\\d\\e\na\\b\\c", Run("""
            from System.IO import Path
            print(Path.Combine('a', 'b', 'c', 'd', 'e'))
            print(Path.Combine('a', 'b', 'c'))
            """));
    }

    [Fact]
    public void Method_Resolution_OptionalParams()
    {
        Assert.Equal("0\n2", Run("""
            from System.IO import MemoryStream
            m1 = MemoryStream()
            print(m1.Capacity)
            m2 = MemoryStream(2)
            print(m2.Capacity)
            """));
    }

    [Fact]
    public void Method_Resolution_OutRefParams()
    {
        Assert.Equal("True", Run("""
            from System.Net import IPAddress
            ok = IPAddress.TryParse("127.0.0.1", None) 
            print(ok)
            """));
    }

    [Fact]
    public void Class_Base_Method_Call_And_Virtual_Override()
    {
        Assert.Equal("2\nMyList cleared\n0", Run("""
            from System.Collections import ArrayList
            
            class MyList(ArrayList):
                def Clear(self):
                    super().Clear()
                    print("MyList cleared")
                    
            l = MyList()
            l.Add(1)
            l.Add(2)
            print(l.Count)
            l.Clear()
            print(l.Count)
            """));
    }

    [Fact]
    public void Class_Async_Throws()
    {
        Assert.Throws<CodeGenException>(() => Run("async def foo():\n    pass"));
        Assert.Throws<CodeGenException>(() => Run("""
            async def foo():
                async for i in []:
                    pass
            """));
        Assert.Throws<CodeGenException>(() => Run("""
            async def foo():
                async with open('x') as f:
                    pass
            """));
    }

    [Fact]
    public void Class_Indexers_Get_Set()
    {
        Assert.Equal("val\n100", Run("""
            class MyList:
                def __init__(self):
                    self.items = [1, 2, 3]
                    
                def __getitem__(self, key):
                    if key == "hello":
                        return "val"
                    return self.items[key]
                    
                def __setitem__(self, key, value):
                    self.items[key] = value
                    
            from System.Reflection import DefaultMemberAttribute
            l = MyList()
            print(l["hello"])
            l[1] = 100
            print(l[1])
            """));
    }

    [Fact]
    public void Class_Iterable()
    {
        // Tests both __iter__ mapping to IEnumerable and __next__ mapping to IEnumerator
        Assert.Equal("1\n2", Run("""
            class Counter:
                def __init__(self, limit):
                    self.limit = limit
                    self.current = 0
                    
                def __iter__(self):
                    return self
                    
                def __next__(self):
                    if self.current >= self.limit:
                        raise StopIteration()
                    self.current = self.current + 1
                    return self.current
                    
            c = Counter(2)
            
            ienum = c.GetEnumerator()
            
            ienum.MoveNext()
            print(ienum.Current)
            ienum.MoveNext()
            print(ienum.Current)
            """));
    }
    
    [Fact]
    public void Class_Bool_Operators()
    {
        var asm = Compile(@"
            class TruthyObject:
                def __init__(self, val):
                    self.val = val
                    
                def __bool__(self):
                    return self.val == 1
        ");
            
        var tType = asm.GetType("TruthyObject");
        var obj1 = Activator.CreateInstance(tType, 1);
        var obj0 = Activator.CreateInstance(tType, 0);
        
        var opTrue = tType.GetMethod("op_True", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var opFalse = tType.GetMethod("op_False", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var implicitBool = tType.GetMethod("op_Implicit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        
        Assert.NotNull(opTrue);
        Assert.NotNull(opFalse);
        Assert.NotNull(implicitBool);
        
        Assert.True((bool)opTrue.Invoke(null, new object?[] { obj1 }));
        Assert.False((bool)opTrue.Invoke(null, new object?[] { obj0 }));
        
        Assert.False((bool)opFalse.Invoke(null, new object?[] { obj1 }));
        Assert.True((bool)opFalse.Invoke(null, new object?[] { obj0 }));
        
        Assert.True((bool)implicitBool.Invoke(null, new object?[] { obj1 }));
        Assert.False((bool)implicitBool.Invoke(null, new object?[] { obj0 }));
    }

    // ── New Type Support Tests ─────────────────────────────────────────────────
    // Tests for newly added types: complex, decimal, nint, nuint
    
    // Note: These type annotations are now recognized by the type system:
    // - complex → System.Numerics.Complex
    // - decimal → System.Decimal  
    // - nint → IntPtr
    // - nuint → UIntPtr
    // The TypeMapper.cs and Types.cs have been updated to support these.
}

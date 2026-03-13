using Naja.Lexer;
using Naja.Parser;
using Naja.Semantics;
using Xunit;

namespace Naja.Semantics.Tests;

public class SemanticTests
{
    private static SemanticModel Analyze(string source)
    {
        var tokens = new Naja.Lexer.Lexer(source).Tokenize();
        var module = new Naja.Parser.Parser(tokens).ParseModule();
        return new SemanticAnalyzer().Analyze(module);
    }

    // ── Type inference ────────────────────────────────────────────────────────

    [Fact]
    public void Infers_Int_Literal()
    {
        var model = Analyze("x = 42");
        var sym   = model.ModuleScope.Lookup("x");
        Assert.NotNull(sym);
        Assert.IsType<IntType>(sym.Type);
    }

    [Fact]
    public void Infers_Float_Literal()
    {
        var model = Analyze("x = 3.14");
        Assert.IsType<FloatType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void Infers_String_Literal()
    {
        var model = Analyze("x = \"hello\"");
        Assert.IsType<StrType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void Infers_Bool_Literal()
    {
        var model = Analyze("x = True");
        Assert.IsType<BoolType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void Infers_Int_Addition()
    {
        var model = Analyze("x = 1 + 2");
        Assert.IsType<IntType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void Infers_Float_From_Mixed_Arithmetic()
    {
        var model = Analyze("x = 1 + 2.0");
        Assert.IsType<FloatType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void Division_Always_Float()
    {
        var model = Analyze("x = 10 / 3");
        Assert.IsType<FloatType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void Floor_Division_Int()
    {
        var model = Analyze("x = 10 // 3");
        Assert.IsType<IntType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void String_Concatenation()
    {
        var model = Analyze("x = \"a\" + \"b\"");
        Assert.IsType<StrType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void Comparison_Returns_Bool()
    {
        var model = Analyze("x = 1 < 2");
        Assert.IsType<BoolType>(model.ModuleScope.Lookup("x")!.Type);
    }

    // ── Annotation inference ──────────────────────────────────────────────────

    [Fact]
    public void Annotated_Int()
    {
        var model = Analyze("x: int = 5");
        Assert.IsType<IntType>(model.ModuleScope.Lookup("x")!.Type);
    }

    [Fact]
    public void Annotated_Str()
    {
        var model = Analyze("name: str = \"Alice\"");
        Assert.IsType<StrType>(model.ModuleScope.Lookup("name")!.Type);
    }

    [Fact]
    public void Annotated_List()
    {
        var model = Analyze("xs: list[int] = []");
        var sym   = model.ModuleScope.Lookup("xs")!;
        var list  = Assert.IsType<ListType>(sym.Type);
        Assert.IsType<IntType>(list.ElementType);
    }

    // ── Symbol table ──────────────────────────────────────────────────────────

    [Fact]
    public void Function_Defined_In_Module_Scope()
    {
        var model = Analyze("def add(a, b):\n    return a + b\n");
        var sym   = model.ModuleScope.Lookup("add");
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Function, sym.Kind);
        Assert.IsType<FunctionType>(sym.Type);
    }

    [Fact]
    public void Function_With_Annotations()
    {
        var model = Analyze("def add(a: int, b: int) -> int:\n    return a + b\n");
        var sym   = model.ModuleScope.Lookup("add")!;
        var fn    = Assert.IsType<FunctionType>(sym.Type);
        Assert.IsType<IntType>(fn.ReturnType);
        Assert.All(fn.ParamTypes, t => Assert.IsType<IntType>(t));
    }

    [Fact]
    public void Class_Defined_In_Module_Scope()
    {
        var model = Analyze("class Foo:\n    pass\n");
        var sym   = model.ModuleScope.Lookup("Foo");
        Assert.NotNull(sym);
        Assert.Equal(SymbolKind.Class, sym.Kind);
    }

    [Fact]
    public void Import_Defined_In_Scope()
    {
        var model = Analyze("import os");
        Assert.NotNull(model.ModuleScope.Lookup("os"));
    }

    [Fact]
    public void From_Import_Defined_In_Scope()
    {
        var model = Analyze("from os import path");
        Assert.NotNull(model.ModuleScope.Lookup("path"));
    }

    [Fact]
    public void Builtin_Print_Resolvable()
    {
        var model = Analyze("print(\"hello\")");
        Assert.False(model.Diagnostics.HasErrors);
    }

    // ── Collection inference ──────────────────────────────────────────────────

    [Fact]
    public void List_Literal_Inferred()
    {
        var model = Analyze("xs = [1, 2, 3]");
        var list  = Assert.IsType<ListType>(model.ModuleScope.Lookup("xs")!.Type);
        Assert.IsType<IntType>(list.ElementType);
    }

    [Fact]
    public void Dict_Literal_Inferred()
    {
        var model = Analyze("d = {\"a\": 1, \"b\": 2}");
        var dict  = Assert.IsType<DictType>(model.ModuleScope.Lookup("d")!.Type);
        Assert.IsType<StrType>(dict.KeyType);
        Assert.IsType<IntType>(dict.ValueType);
    }

    // ── Scope isolation ───────────────────────────────────────────────────────

    [Fact]
    public void Function_Params_Not_In_Module_Scope()
    {
        var model = Analyze("def f(x: int):\n    pass\n");
        // 'x' should not be visible at module level
        Assert.Null(model.ModuleScope.LookupLocal("x"));
    }

    [Fact]
    public void Nested_Function_Scope()
    {
        var src = """
            def outer():
                x = 1
                def inner():
                    y = 2
            """;
        var model = Analyze(src);
        Assert.False(model.Diagnostics.HasErrors);
        // outer exists at module scope
        Assert.NotNull(model.ModuleScope.Lookup("outer"));
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    [Fact]
    public void Undefined_Name_Produces_Warning()
    {
        var model = Analyze("x = undefined_var");
        Assert.True(model.Diagnostics.WarningCount > 0);
    }

    [Fact]
    public void Valid_Program_No_Errors()
    {
        var src = """
            def greet(name: str) -> str:
                return "Hello, " + name

            result = greet("World")
            print(result)
            """;
        var model = Analyze(src);
        Assert.False(model.Diagnostics.HasErrors);
    }

    [Fact]
    public void Full_FizzBuzz_No_Errors()
    {
        var src = """
            for i in range(1, 101):
                if i % 15 == 0:
                    print("FizzBuzz")
                elif i % 3 == 0:
                    print("Fizz")
                elif i % 5 == 0:
                    print("Buzz")
                else:
                    print(i)
            """;
        var model = Analyze(src);
        Assert.False(model.Diagnostics.HasErrors);
    }
}

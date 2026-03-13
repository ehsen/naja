using Naja.CodeGen;
using Naja.Parser;
using Naja.Lexer;
using Naja.Semantics;

class Program {
    static void Main() {
        var code = @"
class Greeter:
    def __init__(self, name):
        self.name = name

    def greet(self):
        print('Hello, ' + self.name)

g = Greeter('world')
g.greet()";
        var lexer = new Lexer(code);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var module = parser.ParseModule();
        var analyzer = new SemanticAnalyzer();
        var model = analyzer.Analyze(module);
        var emitter = new AssemblyEmitter(model, "NajaDebugAsm");
        string outputPath = System.IO.Path.GetFullPath("NajaDebugAsm.dll");
        emitter.EmitToFile(module, outputPath);
        
        // AssemblyEmitter might have changed .dll to .exe for Exe project types
        if (!System.IO.File.Exists(outputPath) && System.IO.File.Exists(System.IO.Path.ChangeExtension(outputPath, ".exe")))
            outputPath = System.IO.Path.ChangeExtension(outputPath, ".exe");

        System.Console.WriteLine($"Successfully compiled to {outputPath}");
        var asm = System.Reflection.Assembly.LoadFrom(outputPath);
        var modType = asm.GetType("NajaDebugAsm")!;
        var mainMeth = modType.GetMethod("Main")!;
        mainMeth.Invoke(null, null);
    }
}

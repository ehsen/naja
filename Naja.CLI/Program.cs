using Naja.CLI;

const string Version = "0.1.0";

if (args.Length == 0)
{
    PrintHelp();
    return 0;
}

return args[0] switch
{
    "compile" => Commands.Compile(args[1..]),
    "run" => Commands.Run(args[1..]),
    "publish" => Commands.Publish(args[1..]),
    "version" or "--version" or "-V" => PrintVersion(),
    "help" or "--help" or "-h" => PrintHelp(),
    _ => Error($"Unknown command '{args[0]}'. Run 'naja help' for usage.")
};

static int PrintVersion()
{
    Console.WriteLine($"naja {Version} — Python syntax. .NET venom.");
    return 0;
}

static int PrintHelp()
{
    Console.WriteLine($"""
        naja {Version} — .naja → .NET 10 IL compiler

        USAGE:
            naja <command> [options]

        COMMANDS:
            compile <file.naja | file.py> [file2 ...]     Compile .naja or .py source to IL
            run     <file.naja | file.py>                  Compile to memory and execute
            publish <file.naja | project.najaproj>         Publish a self-contained single-file exe
            version                                        Print version info
            help                                           Show this help

        COMPILE OPTIONS:
            -o, --output <path>           Output .dll or .exe path
            -t, --type   <type>           Output type: exe, winexe, library (default: library)
            -c, --configuration <config>  Build configuration: Debug, Release (default: Debug)
            -v, --verbose                 Show compilation stages

        RUN OPTIONS:
            -v, --verbose                 Show compilation stages before running

        PUBLISH OPTIONS:
            -o, --output <dir>            Output directory (default: bin/Release/publish)
            -t, --type   <type>           Output type: exe, winexe (default: exe)
            -r, --runtime <rid>           Runtime identifier (default: win-x64)
            -v, --verbose                 Show compilation stages and MSBuild output

        EXAMPLES:
            # Native Naja syntax
            naja run hello.naja
            naja run hello.naja -v

            # Python files (automatically compiled to .NET IL)
            naja run test_windows.py
            naja run test_windows.py -v

            # Publishing
            naja publish hello.najaproj
            naja publish hello.naja -t winexe -r win-x64 -o dist/

            # MSBuild calls compile directly — you rarely need this manually:
            naja compile main.naja utils.py -o obj/hello.dll -t exe
        """);
    return 0;
}

static int Error(string msg)
{
    Commands.PrintError(msg);
    return 1;
}
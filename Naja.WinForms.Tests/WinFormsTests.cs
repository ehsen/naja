using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.WinForms.Tetss;

public class WinFormsTests
{
    private readonly ITestOutputHelper _output;

    public WinFormsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ── Script discovery ─────────────────────────────────────────────────────

    public static IEnumerable<object[]> WinFormsScripts =>
        DiscoverScripts("testdata/winforms");

    public static IEnumerable<object[]> CollectionScripts =>
        DiscoverScripts("testdata/collections");

    public static IEnumerable<object[]> EventScripts =>
        DiscoverScripts("testdata/events");

    public static IEnumerable<object[]> LayoutScripts =>
        DiscoverScripts("testdata/layout");

    public static IEnumerable<object[]> DisposalScripts =>
        DiscoverScripts("testdata/disposal");

    public static IEnumerable<object[]> ExceptionScripts =>
        DiscoverScripts("testdata/exceptions");

    public static IEnumerable<object[]> ThreadingScripts =>
        DiscoverScripts("testdata/threading");

    public static IEnumerable<object[]> GraphicsScripts =>
        DiscoverScripts("testdata/graphics");

    // ── Test theories ────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(WinFormsScripts))]
    public void WinForms_Types(string path) => RunScript(path);

    [Theory]
    [MemberData(nameof(CollectionScripts))]
    public void WinForms_Collections(string path) => RunScript(path);

    [Theory]
    [MemberData(nameof(EventScripts))]
    public void WinForms_Events(string path) => RunScript(path);

    [Theory]
    [MemberData(nameof(LayoutScripts))]
    public void WinForms_Layout(string path) => RunScript(path);

    [Theory]
    [MemberData(nameof(DisposalScripts))]
    public void WinForms_Disposal(string path) => RunScript(path);

    [Theory]
    [MemberData(nameof(ExceptionScripts))]
    public void WinForms_Exceptions(string path) => RunScript(path);

    [Theory]
    [MemberData(nameof(ThreadingScripts))]
    public void WinForms_Threading(string path) => RunScript(path);

    [Theory]
    [MemberData(nameof(GraphicsScripts))]
    public void WinForms_Graphics(string path) => RunScript(path);

    // ── Core runner ──────────────────────────────────────────────────────────

    private void RunScript(string path)
    {
        var fileName = Path.GetFileName(path);
        _output.WriteLine($"Running: {path}");

        // Verify the script file actually exists before attempting compilation.
        // MemberData discovery runs at collection time; a missing file here
        // means the .csproj CopyToOutputDirectory is misconfigured.
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Test script not found: {path}\n" +
                $"Ensure <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory> " +
                $"is set in Naja.WinForms.Integration.csproj for testdata/**/*.naja", path);

        Exception? failure = null;

        try
        {
            // NajaEngine.Eval() compiles the script to an in-memory assembly
            // using AssemblyBuilderAccess.Run and invokes the entry point.
            // Any Naja assert failure throws InvalidOperationException.
            // Any compiler error throws CodeGenException.
            // Both propagate naturally into xUnit.
            var engine = new NajaEngine();
            engine.Eval(path);
        }
        catch (CodeGenException cge)
        {
            // Compiler-level failure — emit the location for easy diagnosis
            _output.WriteLine($"COMPILE ERROR [{fileName}]: {cge.Message}");
            failure = cge;
        }
        catch (InvalidOperationException ioe)
        {
            // Naja assert failure (mapped to InvalidOperationException by NajaBuiltins.Assert)
            _output.WriteLine($"ASSERT FAILED [{fileName}]: {ioe.Message}");
            failure = ioe;
        }
        catch (Exception ex)
        {
            // Unexpected runtime exception — log full detail
            _output.WriteLine($"RUNTIME ERROR [{fileName}]: {ex.GetType().Name}: {ex.Message}");
            _output.WriteLine(ex.StackTrace ?? string.Empty);
            failure = ex;
        }

        if (failure != null)
            Assert.Fail($"[{fileName}] {failure?.GetType().Name}: {failure?.Message}");
    }

    // ── Discovery helper ─────────────────────────────────────────────────────

    private static IEnumerable<object[]> DiscoverScripts(string folder)
    {
        // If the folder doesn't exist yet (e.g. during a fresh clone before
        // the testdata is populated) return empty rather than crashing the
        // entire test collection phase.
        if (!Directory.Exists(folder))
            return Enumerable.Empty<object[]>();

        return Directory
            .GetFiles(folder, "*.naja", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f)   // deterministic order across platforms
            .Select(f => new object[] { f });
    }
}

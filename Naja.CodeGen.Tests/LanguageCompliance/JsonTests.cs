using Xunit;
using Xunit.Abstractions;
using Naja.CodeGen;

namespace Naja.CodeGen.Tests.LanguageCompliance;

/// <summary>
/// Comprehensive JSON module test runner.
/// 
/// Tests Naja's JSON implementation (NajaJson) against CPython test_json patterns:
/// - Basic serialization/deserialization (dumps, loads)
/// - Data type conversions (None, bool, int, float, str, list, dict)
/// - Formatting options (indent, sort_keys, ensure_ascii)
/// - Unicode handling and escape sequences
/// - Error handling (JSONDecodeError, TypeError)
/// - Edge cases (deeply nested, large structures, special characters)
/// 
/// All tests replicate CPython semantics without requiring CPython dependencies.
/// Tests are written in Python (test_json.py) and executed via NajaEngine.
/// 
/// Test coverage (82 tests across 12 test classes):
///   TestJsonBasic          (25) - All data types, roundtrips
///   TestJsonIndent         (6)  - Indentation options
///   TestJsonSortKeys       (3)  - Key sorting
///   TestJsonEnsureAscii    (3)  - Unicode/ASCII handling
///   TestJsonErrors         (9)  - Error handling and exceptions
///   TestJsonUnicode        (3)  - Unicode strings and keys
///   TestJsonNumbers        (6)  - Number type preservation
///   TestJsonWhitespace     (5)  - Whitespace parsing
///   TestJsonEdgeCases      (6)  - Large/nested structures
///   TestJsonStringEscapes  (6)  - String escape sequences
///   TestJsonTypeConversions(3)  - Key type conversions
///   TestJsonSimpleTypes    (7)  - Individual type roundtrips
/// </summary>
[Collection("SerialConsole")]
public sealed class JsonTests
{
    private readonly ITestOutputHelper _output;
    private static readonly NajaEngine Engine = new();
    private const string TestFile = "testdata/json/test_json.py";

    public JsonTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "JSON Module Tests"), Trait("category", "json"), Trait("phase", "stdlib")]
    public void RunJsonTests()
    {
        _output.WriteLine($"Running: {TestFile}");
        _output.WriteLine("Test classes: 12");
        _output.WriteLine("Total tests: 82");
        _output.WriteLine("");

        try
        {
            Engine.Eval(TestFile);
            _output.WriteLine("");
            _output.WriteLine("✓ All JSON tests passed");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"✗ Test execution failed: {ex.Message}");
            throw;
        }
    }
}

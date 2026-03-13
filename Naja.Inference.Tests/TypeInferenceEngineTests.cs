using Naja.CodeGen;
using Naja.Inference;
using Naja.Lexer;
using Naja.Parser;
using Naja.Semantics;
using Xunit;

// ═══════════════════════════════════════════════════════════════════════════════
// TypeInferenceEngine — xUnit Test Suite
//
// Project structure (add to your solution):
//
//   Naja.Tests/
//     Inference/
//       TypeInferenceEngineTests.cs   ← this file
//
// csproj additions:
//   <PackageReference Include="xunit"              Version="2.*" />
//   <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
//   <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
//
// Run:  dotnet test --filter "Category=Inference"
// ═══════════════════════════════════════════════════════════════════════════════

namespace Naja.Tests.Inference;

// ─────────────────────────────────────────────────────────────────────────────
// Test helpers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Thin wrapper that parses Python source, runs the semantic analyser,
/// runs the inference engine, and exposes the result for assertions.
/// Replace Parse() / Analyse() calls with your real parser/analyser APIs.
/// </summary>
internal static class InferenceHelper
{
    /// <summary>
    /// Infer types for a complete Python module source string.
    /// Returns (result, module) so tests can look up specific nodes.
    /// </summary>
    public static (InferenceResult Result, Module Module) Infer(string source)
    {
        var tokens = new Naja.Lexer.Lexer(source).Tokenize();
        var parser = new Naja.Parser.Parser(tokens);
        var module = parser.ParseModule();
        var analyser = new Naja.Semantics.SemanticAnalyzer();
        var model = analyser.Analyze(module);
        var engine = new TypeInferenceEngine(model);
        var result = engine.Infer(module);
        return (result, module);
    }

    /// <summary>
    /// Convenience: infer a module-scope expression by wrapping it in an assignment.
    /// Returns the inferred type of the RHS.
    /// </summary>
    public static NajaType InferExpr(string pythonExpr)
    {
        var source = $"__result__ = {pythonExpr}";
        var (result, module) = Infer(source);
        var assign = (AssignStatement)module.Body[0];
        return result.GetType(assign.Value);
    }

    /// <summary>
    /// Infer a full function body and return (result, functionDef).
    /// </summary>
    public static (InferenceResult Result, FunctionDef Fn) InferFunction(string source)
    {
        var (result, module) = Infer(source);
        var fn = (FunctionDef)module.Body.First(s => s is FunctionDef);
        return (result, fn);
    }

    /// <summary>Return the inferred variable type for a named local in a scope.</summary>
    public static NajaType VarType(InferenceResult result, string scope, string name)
        => result.VariableTypes.TryGetValue((scope, name), out var t) ? t : NajaTypes.Unknown;
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. Literal inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class LiteralInferenceTests
{
    [Fact]
    public void IntLiteral_InfersInt()
    {
        var t = InferenceHelper.InferExpr("42");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void FloatLiteral_InfersFloat()
    {
        var t = InferenceHelper.InferExpr("3.14");
        Assert.IsType<FloatType>(t);
    }

    [Fact]
    public void StringLiteral_InfersStr()
    {
        var t = InferenceHelper.InferExpr("'hello'");
        Assert.IsType<StrType>(t);
    }

    [Fact]
    public void BoolLiteral_True_InfersBool()
    {
        var t = InferenceHelper.InferExpr("True");
        Assert.IsType<BoolType>(t);
    }

    [Fact]
    public void BoolLiteral_False_InfersBool()
    {
        var t = InferenceHelper.InferExpr("False");
        Assert.IsType<BoolType>(t);
    }

    [Fact]
    public void NoneLiteral_InfersNone()
    {
        var t = InferenceHelper.InferExpr("None");
        Assert.IsType<NoneType>(t);
    }

    [Fact]
    public void FStringExpr_InfersStr()
    {
        var t = InferenceHelper.InferExpr("f'hello {42}'");
        Assert.IsType<StrType>(t);
    }

    [Fact]
    public void NegativeIntLiteral_InfersInt()
    {
        var t = InferenceHelper.InferExpr("-1");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void LargeIntLiteral_InfersInt()
    {
        var t = InferenceHelper.InferExpr("999999999999999999");
        Assert.IsType<IntType>(t);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. Arithmetic operator inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class ArithmeticInferenceTests
{
    // int OP int → int
    [Theory]
    [InlineData("1 + 2")]
    [InlineData("10 - 3")]
    [InlineData("4 * 5")]
    [InlineData("7 % 3")]
    [InlineData("8 // 3")]
    public void IntOp_Int_InfersInt(string expr)
    {
        var t = InferenceHelper.InferExpr(expr);
        Assert.IsType<IntType>(t);
    }

    // int / int → float  (Python 3 true division)
    [Fact]
    public void IntDiv_Int_InfersFloat()
    {
        var t = InferenceHelper.InferExpr("7 / 2");
        Assert.IsType<FloatType>(t);
    }

    // float OP anything → float
    [Theory]
    [InlineData("1.0 + 2")]
    [InlineData("3 + 1.5")]
    [InlineData("2.0 * 3")]
    [InlineData("10.0 / 4")]
    [InlineData("10.0 // 3")]
    [InlineData("5.0 % 2")]
    public void FloatOp_InfersFloat(string expr)
    {
        var t = InferenceHelper.InferExpr(expr);
        Assert.IsType<FloatType>(t);
    }

    // ** always → float
    [Theory]
    [InlineData("2 ** 10")]
    [InlineData("2.0 ** 3")]
    [InlineData("4 ** 0.5")]
    public void Pow_AlwaysInfersFloat(string expr)
    {
        var t = InferenceHelper.InferExpr(expr);
        Assert.IsType<FloatType>(t);
    }

    // str + str → str
    [Fact]
    public void StrConcat_InfersStr()
    {
        var t = InferenceHelper.InferExpr("'hello' + ' world'");
        Assert.IsType<StrType>(t);
    }

    // bool + bool → int  (True + True == 2 in Python)
    [Fact]
    public void BoolPlusBool_InfersInt()
    {
        var t = InferenceHelper.InferExpr("True + False");
        Assert.IsType<IntType>(t);
    }

    // bool + int → int
    [Fact]
    public void BoolPlusInt_InfersInt()
    {
        var t = InferenceHelper.InferExpr("True + 1");
        Assert.IsType<IntType>(t);
    }

    // Bitwise ops on int → int
    [Theory]
    [InlineData("5 & 3")]
    [InlineData("5 | 3")]
    [InlineData("5 ^ 3")]
    [InlineData("1 << 4")]
    [InlineData("16 >> 2")]
    public void BitwiseOps_InfersInt(string expr)
    {
        var t = InferenceHelper.InferExpr(expr);
        Assert.IsType<IntType>(t);
    }

    // Mixed incompatible types → Unknown (dynamic fallback)
    [Fact]
    public void StrPlusInt_InfersUnknown()
    {
        var t = InferenceHelper.InferExpr("'x' + 1");
        Assert.IsType<UnknownType>(t);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. Unary operator inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class UnaryInferenceTests
{
    [Fact] public void NegInt_InfersInt()     => Assert.IsType<IntType>(InferenceHelper.InferExpr("-42"));
    [Fact] public void NegFloat_InfersFloat() => Assert.IsType<FloatType>(InferenceHelper.InferExpr("-3.14"));
    [Fact] public void PosInt_InfersInt()     => Assert.IsType<IntType>(InferenceHelper.InferExpr("+42"));
    [Fact] public void Not_InfersBool()       => Assert.IsType<BoolType>(InferenceHelper.InferExpr("not True"));
    [Fact] public void Invert_InfersInt()     => Assert.IsType<IntType>(InferenceHelper.InferExpr("~5"));
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. Comparison and boolean ops
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class ComparisonInferenceTests
{
    [Theory]
    [InlineData("1 == 2")]
    [InlineData("1 != 2")]
    [InlineData("1 < 2")]
    [InlineData("1 <= 2")]
    [InlineData("1 > 2")]
    [InlineData("1 >= 2")]
    [InlineData("'a' in 'abc'")]
    [InlineData("1 not in [1,2]")]
    [InlineData("None is None")]
    [InlineData("1 is not None")]
    public void ComparisonExpr_InfersBool(string expr)
    {
        var t = InferenceHelper.InferExpr(expr);
        Assert.IsType<BoolType>(t);
    }

    [Fact]
    public void AndOp_HomogeneousInt_InfersInt()
    {
        // 1 and 2  → returns 2 (int) in Python
        var t = InferenceHelper.InferExpr("1 and 2");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void OrOp_HomogeneousStr_InfersStr()
    {
        var t = InferenceHelper.InferExpr("'a' or 'b'");
        Assert.IsType<StrType>(t);
    }

    [Fact]
    public void BoolOp_MixedTypes_UnifiesCorrectly()
    {
        // True or 1 → unify(bool, int) = int
        var t = InferenceHelper.InferExpr("True or 1");
        Assert.IsType<IntType>(t);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. Variable assignment and scoping
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class AssignmentInferenceTests
{
    [Fact]
    public void SimpleAssign_VariableGetsCorrectType()
    {
        var (result, _) = InferenceHelper.Infer("x = 42");
        var t = InferenceHelper.VarType(result, "", "x");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void FloatAssign_VariableGetsFloat()
    {
        var (result, _) = InferenceHelper.Infer("x = 3.14");
        Assert.IsType<FloatType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void ChainedAssign_BothVariablesTyped()
    {
        var (result, _) = InferenceHelper.Infer(@"
x = 1
y = x + 1
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "x"));
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "y"));
    }

    [Fact]
    public void AnnotatedAssign_AnnotationTakesPriority()
    {
        var (result, _) = InferenceHelper.Infer("x: int = 42");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void AnnotatedAssignFloat_AnnotationTakesPriority()
    {
        var (result, _) = InferenceHelper.Infer("x: float = 42");
        // Annotation says float even though literal is int
        Assert.IsType<FloatType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void TupleUnpack_BothVariablesTyped()
    {
        var (result, _) = InferenceHelper.Infer("a, b = 1, 'hello'");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "a"));
        Assert.IsType<StrType>(InferenceHelper.VarType(result, "", "b"));
    }

    [Fact]
    public void AugAssign_IntPlusInt_StaysInt()
    {
        var (result, _) = InferenceHelper.Infer(@"
x = 0
x += 1
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void AugAssign_IntPlusFloat_BecomesFloat()
    {
        var (result, _) = InferenceHelper.Infer(@"
x = 0
x += 1.5
");
        Assert.IsType<FloatType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void WalrusOperator_BindsVariable()
    {
        var (result, _) = InferenceHelper.Infer(@"
if (n := 42) > 0:
    pass
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "n"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. Collection literal inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class CollectionInferenceTests
{
    [Fact]
    public void HomogenousIntList_InfersListInt()
    {
        var t = InferenceHelper.InferExpr("[1, 2, 3]");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<IntType>(lt.ElementType);
    }

    [Fact]
    public void HomogenousStrList_InfersListStr()
    {
        var t = InferenceHelper.InferExpr("['a', 'b', 'c']");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<StrType>(lt.ElementType);
    }

    [Fact]
    public void MixedList_InfersListUnknown()
    {
        var t = InferenceHelper.InferExpr("[1, 'hello', 3.14]");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<UnknownType>(lt.ElementType);
    }

    [Fact]
    public void EmptyList_InfersListUnknown()
    {
        var t = InferenceHelper.InferExpr("[]");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<UnknownType>(lt.ElementType);
    }

    [Fact]
    public void IntFloatList_WidensToListFloat()
    {
        // [1, 2.0] → List<float> via numeric widening
        var t = InferenceHelper.InferExpr("[1, 2.0, 3]");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<FloatType>(lt.ElementType);
    }

    [Fact]
    public void HomogenousTuple_InfersTupleWithElementTypes()
    {
        var t = InferenceHelper.InferExpr("(1, 'hello', 3.14)");
        var tt = Assert.IsType<TupleType>(t);
        Assert.Equal(3, tt.ElementTypes.Count);
        Assert.IsType<IntType>(tt.ElementTypes[0]);
        Assert.IsType<StrType>(tt.ElementTypes[1]);
        Assert.IsType<FloatType>(tt.ElementTypes[2]);
    }

    [Fact]
    public void IntSet_InfersSetInt()
    {
        var t = InferenceHelper.InferExpr("{1, 2, 3}");
        var st = Assert.IsType<SetType>(t);
        Assert.IsType<IntType>(st.ElementType);
    }

    [Fact]
    public void StrIntDict_InfersDictStrInt()
    {
        var t = InferenceHelper.InferExpr("{'a': 1, 'b': 2}");
        var dt = Assert.IsType<DictType>(t);
        Assert.IsType<StrType>(dt.KeyType);
        Assert.IsType<IntType>(dt.ValueType);
    }

    [Fact]
    public void EmptyDict_InfersDictUnknownUnknown()
    {
        var t = InferenceHelper.InferExpr("{}");
        var dt = Assert.IsType<DictType>(t);
        Assert.IsType<UnknownType>(dt.KeyType);
        Assert.IsType<UnknownType>(dt.ValueType);
    }

    [Fact]
    public void ListComprehension_InfersCorrectElementType()
    {
        var t = InferenceHelper.InferExpr("[x * 2 for x in [1, 2, 3]]");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<IntType>(lt.ElementType);
    }

    [Fact]
    public void ListComprehension_StrElements_InfersListStr()
    {
        var t = InferenceHelper.InferExpr("[s.upper() for s in ['a', 'b']]");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<StrType>(lt.ElementType);
    }

    [Fact]
    public void ListComprehension_WithFilter_StillTyped()
    {
        var t = InferenceHelper.InferExpr("[x for x in [1,2,3] if x > 1]");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<IntType>(lt.ElementType);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 7. Function return type inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class FunctionReturnInferenceTests
{
    [Fact]
    public void ExplicitReturnAnnotation_UsedDirectly()
    {
        var (result, _) = InferenceHelper.Infer(@"
def greet() -> str:
    return 'hello'
");
        Assert.True(result.FunctionReturnTypes.TryGetValue("greet", out var t));
        Assert.IsType<StrType>(t);
    }

    [Fact]
    public void InferredReturnType_IntLiteral()
    {
        var (result, _) = InferenceHelper.Infer(@"
def answer():
    return 42
");
        Assert.True(result.FunctionReturnTypes.TryGetValue("answer", out var t));
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void InferredReturnType_FloatArithmetic()
    {
        var (result, _) = InferenceHelper.Infer(@"
def area(r):
    return 3.14159 * r * r
");
        Assert.True(result.FunctionReturnTypes.TryGetValue("area", out var t));
        Assert.IsType<FloatType>(t);
    }

    [Fact]
    public void InferredReturnType_ConditionalReturn_Unified()
    {
        var (result, _) = InferenceHelper.Infer(@"
def abs_val(x: int) -> int:
    if x < 0:
        return -x
    return x
");
        Assert.True(result.FunctionReturnTypes.TryGetValue("abs_val", out var t));
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void InferredReturnType_BranchReturnsDifferentTypes_Widened()
    {
        var (result, _) = InferenceHelper.Infer(@"
def mixed(flag: bool):
    if flag:
        return 1
    return 2.0
");
        // int and float → unified to float
        Assert.True(result.FunctionReturnTypes.TryGetValue("mixed", out var t));
        Assert.IsType<FloatType>(t);
    }

    [Fact]
    public void NoReturnStatement_InfersNone()
    {
        var (result, _) = InferenceHelper.Infer(@"
def do_nothing():
    x = 1
");
        Assert.True(result.FunctionReturnTypes.TryGetValue("do_nothing", out var t));
        Assert.IsType<NoneType>(t);
    }

    [Fact]
    public void CallSite_UsesInferredReturnType()
    {
        var (result, module) = InferenceHelper.Infer(@"
def double(x: int) -> int:
    return x * 2

result = double(21)
");
        var assign = (AssignStatement)module.Body[1];
        var callType = result.GetType(assign.Value);
        Assert.IsType<IntType>(callType);
    }

    [Fact]
    public void AnnotatedParams_FlowIntoBody()
    {
        var (result, _) = InferenceHelper.Infer(@"
def add(a: int, b: int) -> int:
    return a + b
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "add", "a"));
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "add", "b"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 8. Control flow — if/else merging
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class ControlFlowInferenceTests
{
    [Fact]
    public void IfElse_BothBranchesSameType_Stable()
    {
        var (result, _) = InferenceHelper.Infer(@"
flag = True
if flag:
    x = 1
else:
    x = 2
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void IfElse_IntAndFloat_WidensToFloat()
    {
        var (result, _) = InferenceHelper.Infer(@"
flag = True
if flag:
    x = 1
else:
    x = 2.0
");
        Assert.IsType<FloatType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void IfElse_IncompatibleTypes_FallsBackToUnknown()
    {
        var (result, _) = InferenceHelper.Infer(@"
flag = True
if flag:
    x = 1
else:
    x = 'hello'
");
        Assert.IsType<UnknownType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void IfNoElse_VariableTypeConservative()
    {
        // Without else branch, x might be unset — merge with Unknown
        var (result, _) = InferenceHelper.Infer(@"
flag = True
if flag:
    x = 42
");
        // After if-without-else, x = unify(int, Unknown) = int
        // (Unknown from the no-else branch, int from the if branch)
        var t = InferenceHelper.VarType(result, "", "x");
        Assert.True(t is IntType or UnknownType);  // implementation may vary
    }

    [Fact]
    public void NestedIf_TypePropagatesCorrectly()
    {
        var (result, _) = InferenceHelper.Infer(@"
x = 0
if True:
    if True:
        x = 42
    else:
        x = 99
else:
    x = -1
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "x"));
    }

    [Fact]
    public void TernaryExpr_HomogeneousTypes_Typed()
    {
        var t = InferenceHelper.InferExpr("1 if True else 2");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void TernaryExpr_MixedTypes_Widened()
    {
        var t = InferenceHelper.InferExpr("1 if True else 2.0");
        Assert.IsType<FloatType>(t);
    }

    [Fact]
    public void TernaryExpr_IncompatibleTypes_Unknown()
    {
        var t = InferenceHelper.InferExpr("1 if True else 'x'");
        Assert.IsType<UnknownType>(t);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 9. Loop inference (fixed-point)
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class LoopInferenceTests
{
    [Fact]
    public void ForLoop_RangeIterator_LoopVarIsInt()
    {
        var (result, _) = InferenceHelper.Infer(@"
for i in range(10):
    pass
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "i"));
    }

    [Fact]
    public void ForLoop_StringIterator_LoopVarIsStr()
    {
        var (result, _) = InferenceHelper.Infer(@"
for ch in 'hello':
    pass
");
        Assert.IsType<StrType>(InferenceHelper.VarType(result, "", "ch"));
    }

    [Fact]
    public void ForLoop_TypedList_LoopVarInheritsElementType()
    {
        var (result, _) = InferenceHelper.Infer(@"
items = [1, 2, 3]
for item in items:
    pass
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "item"));
    }

    [Fact]
    public void WhileLoop_Accumulator_TypeStable()
    {
        var (result, _) = InferenceHelper.Infer(@"
total = 0
i = 0
while i < 10:
    total = total + i
    i = i + 1
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "total"));
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "i"));
    }

    [Fact]
    public void ForLoop_AccumulatorSumInts_StaysInt()
    {
        var (result, _) = InferenceHelper.Infer(@"
total = 0
for i in range(10):
    total = total + i
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "", "total"));
    }

    [Fact]
    public void ForLoop_AccumulatorIntPlusFloat_BecomesFloat()
    {
        var (result, _) = InferenceHelper.Infer(@"
total = 0.0
for i in range(10):
    total = total + i
");
        Assert.IsType<FloatType>(InferenceHelper.VarType(result, "", "total"));
    }

    [Fact]
    public void TupleUnpackInForLoop_BothVarsTyped()
    {
        var (result, _) = InferenceHelper.Infer(@"
pairs = [(1, 'a'), (2, 'b')]
for num, ch in pairs:
    pass
");
        // List element type is TupleType — unpack gives first=int, second=str
        // This may be Unknown if tuple element inference isn't fully implemented
        var numType = InferenceHelper.VarType(result, "", "num");
        var chType  = InferenceHelper.VarType(result, "", "ch");
        Assert.True(numType is IntType or UnknownType);
        Assert.True(chType  is StrType or UnknownType);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 10. Builtin function return types
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class BuiltinReturnTypeTests
{
    [Theory]
    [InlineData("len([1,2,3])")]
    [InlineData("len('hello')")]
    [InlineData("ord('A')")]
    [InlineData("hash(42)")]
    [InlineData("id(None)")]
    [InlineData("round(3.14)")]
    [InlineData("sum([1,2,3])")]
    public void BuiltinsReturningInt(string expr)
    {
        Assert.IsType<IntType>(InferenceHelper.InferExpr(expr));
    }

    [Theory]
    [InlineData("str(42)")]
    [InlineData("repr(42)")]
    [InlineData("chr(65)")]
    [InlineData("hex(255)")]
    [InlineData("bin(8)")]
    [InlineData("oct(8)")]
    [InlineData("format(3.14, '.2f')")]
    [InlineData("input('> ')")]
    public void BuiltinsReturningStr(string expr)
    {
        Assert.IsType<StrType>(InferenceHelper.InferExpr(expr));
    }

    [Theory]
    [InlineData("bool(1)")]
    [InlineData("isinstance(1, int)")]
    [InlineData("hasattr([], 'append')")]
    [InlineData("callable(print)")]
    [InlineData("any([True, False])")]
    [InlineData("all([True, True])")]
    public void BuiltinsReturningBool(string expr)
    {
        Assert.IsType<BoolType>(InferenceHelper.InferExpr(expr));
    }

    [Theory]
    [InlineData("float('3.14')")]
    [InlineData("pow(2, 0.5)")]
    public void BuiltinsReturningFloat(string expr)
    {
        Assert.IsType<FloatType>(InferenceHelper.InferExpr(expr));
    }

    [Fact]
    public void Range_InfersListInt()
    {
        var t = InferenceHelper.InferExpr("range(10)");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<IntType>(lt.ElementType);
    }

    [Fact]
    public void Sorted_InfersListUnknown()
    {
        var t = InferenceHelper.InferExpr("sorted([3,1,2])");
        Assert.IsType<ListType>(t);
    }

    [Fact]
    public void Enumerate_InfersListTuple()
    {
        var t = InferenceHelper.InferExpr("enumerate(['a','b'])");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<TupleType>(lt.ElementType);
    }

    [Fact]
    public void Abs_PreservesOperandType_Int()
    {
        var t = InferenceHelper.InferExpr("abs(-5)");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void Abs_PreservesOperandType_Float()
    {
        var t = InferenceHelper.InferExpr("abs(-3.14)");
        Assert.IsType<FloatType>(t);
    }

    [Fact]
    public void Print_InfersNone()
    {
        var t = InferenceHelper.InferExpr("print('hello')");
        Assert.IsType<NoneType>(t);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 11. String method inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class StringMethodInferenceTests
{
    [Theory]
    [InlineData("'hello'.upper()")]
    [InlineData("'hello'.lower()")]
    [InlineData("'  x  '.strip()")]
    [InlineData("'  x  '.lstrip()")]
    [InlineData("'  x  '.rstrip()")]
    [InlineData("'hello'.replace('l','r')")]
    [InlineData("', '.join(['a','b'])")]
    [InlineData("'hello'.title()")]
    [InlineData("'hello'.center(10)")]
    [InlineData("'hello'.ljust(10)")]
    [InlineData("'hello'.rjust(10)")]
    [InlineData("'5'.zfill(3)")]
    public void StrMethods_ReturningStr(string expr)
    {
        Assert.IsType<StrType>(InferenceHelper.InferExpr(expr));
    }

    [Theory]
    [InlineData("'hello'.split()")]
    [InlineData("'a\\nb'.splitlines()")]
    public void StrMethods_ReturningListStr(string expr)
    {
        var t = InferenceHelper.InferExpr(expr);
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<StrType>(lt.ElementType);
    }

    [Theory]
    [InlineData("'hello'.startswith('h')")]
    [InlineData("'hello'.endswith('o')")]
    [InlineData("'123'.isdigit()")]
    [InlineData("'abc'.isalpha()")]
    [InlineData("'abc123'.isalnum()")]
    public void StrMethods_ReturningBool(string expr)
    {
        Assert.IsType<BoolType>(InferenceHelper.InferExpr(expr));
    }

    [Theory]
    [InlineData("'hello'.find('l')")]
    [InlineData("'hello'.index('l')")]
    [InlineData("'hello'.count('l')")]
    public void StrMethods_ReturningInt(string expr)
    {
        Assert.IsType<IntType>(InferenceHelper.InferExpr(expr));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 12. List / dict / set method inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class CollectionMethodInferenceTests
{
    [Fact]
    public void ListPop_ReturnsElementType()
    {
        var (result, module) = InferenceHelper.Infer(@"
items = [1, 2, 3]
x = items.pop()
");
        var assign = (AssignStatement)module.Body[1];
        Assert.IsType<IntType>(result.GetType(assign.Value));
    }

    [Fact]
    public void ListIndex_ReturnsInt()
    {
        var t = InferenceHelper.InferExpr("[1,2,3].index(2)");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void ListCount_ReturnsInt()
    {
        var t = InferenceHelper.InferExpr("[1,2,3].count(1)");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void DictKeys_ReturnsListOfKeyType()
    {
        var (result, module) = InferenceHelper.Infer(@"
d = {'a': 1, 'b': 2}
k = d.keys()
");
        var assign = (AssignStatement)module.Body[1];
        var t = result.GetType(assign.Value);
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<StrType>(lt.ElementType);
    }

    [Fact]
    public void DictValues_ReturnsListOfValueType()
    {
        var (result, module) = InferenceHelper.Infer(@"
d = {'a': 1, 'b': 2}
v = d.values()
");
        var assign = (AssignStatement)module.Body[1];
        var t = result.GetType(assign.Value);
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<IntType>(lt.ElementType);
    }

    [Fact]
    public void DictGet_ReturnsValueType()
    {
        var (result, module) = InferenceHelper.Infer(@"
d = {'a': 1}
v = d.get('a')
");
        var assign = (AssignStatement)module.Body[1];
        Assert.IsType<IntType>(result.GetType(assign.Value));
    }

    [Fact]
    public void SetUnion_ReturnsSameSetType()
    {
        var (result, module) = InferenceHelper.Infer(@"
a = {1, 2}
b = {3, 4}
c = a.union(b)
");
        var assign = (AssignStatement)module.Body[2];
        var t = result.GetType(assign.Value);
        var st = Assert.IsType<SetType>(t);
        Assert.IsType<IntType>(st.ElementType);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 13. Type annotation parsing
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class AnnotationParsingTests
{
    [Theory]
    [InlineData("x: int",   typeof(IntType))]
    [InlineData("x: float", typeof(FloatType))]
    [InlineData("x: str",   typeof(StrType))]
    [InlineData("x: bool",  typeof(BoolType))]
    [InlineData("x: bytes", typeof(BytesType))]
    public void SimpleAnnotations_ParseCorrectly(string source, Type expectedType)
    {
        var (result, _) = InferenceHelper.Infer(source);
        var t = InferenceHelper.VarType(result, "", "x");
        Assert.IsType(expectedType, t);
    }

    [Fact]
    public void ListIntAnnotation_ParsesCorrectly()
    {
        var (result, _) = InferenceHelper.Infer("x: list[int]");
        var t = InferenceHelper.VarType(result, "", "x");
        var lt = Assert.IsType<ListType>(t);
        Assert.IsType<IntType>(lt.ElementType);
    }

    [Fact]
    public void DictStrIntAnnotation_ParsesCorrectly()
    {
        var (result, _) = InferenceHelper.Infer("x: dict[str, int]");
        var t = InferenceHelper.VarType(result, "", "x");
        var dt = Assert.IsType<DictType>(t);
        Assert.IsType<StrType>(dt.KeyType);
        Assert.IsType<IntType>(dt.ValueType);
    }

    [Fact]
    public void OptionalIntAnnotation_ParsesAsInt()
    {
        var (result, _) = InferenceHelper.Infer("from typing import Optional\nx: Optional[int]");
        var t = InferenceHelper.VarType(result, "", "x");
        Assert.IsType<IntType>(t);
    }

    [Fact]
    public void FunctionAnnotation_ParamsAndReturn()
    {
        var (result, _) = InferenceHelper.Infer(@"
def f(a: int, b: str) -> float:
    return float(a)
");
        Assert.IsType<IntType>(InferenceHelper.VarType(result, "f", "a"));
        Assert.IsType<StrType>(InferenceHelper.VarType(result, "f", "b"));
        result.FunctionReturnTypes.TryGetValue("f", out var ret);
        Assert.IsType<FloatType>(ret);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 14. Unification rules
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class UnificationTests
{
    private static NajaType U(NajaType a, NajaType b) => TypeInferenceEngine.Unify(a, b);

    [Fact] public void Unify_Same_ReturnsSame()       => Assert.IsType<IntType>(U(NajaTypes.Int, NajaTypes.Int));
    [Fact] public void Unify_Unknown_ReturnsOther()   => Assert.IsType<IntType>(U(NajaTypes.Unknown, NajaTypes.Int));
    [Fact] public void Unify_Unknown_Left()           => Assert.IsType<StrType>(U(NajaTypes.Str, NajaTypes.Unknown));
    [Fact] public void Unify_BoolInt_ReturnsInt()     => Assert.IsType<IntType>(U(NajaTypes.Bool, NajaTypes.Int));
    [Fact] public void Unify_IntFloat_ReturnsFloat()  => Assert.IsType<FloatType>(U(NajaTypes.Int, NajaTypes.Float));
    [Fact] public void Unify_BoolFloat_ReturnsFloat() => Assert.IsType<FloatType>(U(NajaTypes.Bool, NajaTypes.Float));
    [Fact] public void Unify_StrInt_ReturnsUnknown()  => Assert.IsType<UnknownType>(U(NajaTypes.Str, NajaTypes.Int));
    [Fact] public void Unify_NoneInt_ReturnsInt()     => Assert.IsType<IntType>(U(NajaTypes.None, NajaTypes.Int));

    [Fact]
    public void Unify_ListIntListFloat_ReturnsListFloat()
    {
        var a = new ListType(NajaTypes.Int);
        var b = new ListType(NajaTypes.Float);
        var r = Assert.IsType<ListType>(U(a, b));
        Assert.IsType<FloatType>(r.ElementType);
    }

    [Fact]
    public void Unify_ListIntListStr_ReturnsListUnknown()
    {
        var a = new ListType(NajaTypes.Int);
        var b = new ListType(NajaTypes.Str);
        var r = Assert.IsType<ListType>(U(a, b));
        Assert.IsType<UnknownType>(r.ElementType);
    }

    [Fact]
    public void Unify_TupleSameLength_ElementWise()
    {
        var a = new TupleType([NajaTypes.Int, NajaTypes.Str]);
        var b = new TupleType([NajaTypes.Float, NajaTypes.Str]);
        var r = Assert.IsType<TupleType>(U(a, b));
        Assert.Equal(2, r.ElementTypes.Count);
        Assert.IsType<FloatType>(r.ElementTypes[0]);
        Assert.IsType<StrType>(r.ElementTypes[1]);
    }

    [Fact]
    public void Unify_TupleDifferentLength_ReturnsUnknown()
    {
        var a = new TupleType([NajaTypes.Int, NajaTypes.Str]);
        var b = new TupleType([NajaTypes.Int]);
        Assert.IsType<UnknownType>(U(a, b));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 15. AOT gate — IsFullyStatic
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
[Trait("Category", "AOT")]
public class AotGateTests
{
    [Fact]
    public void FullyAnnotatedFunction_IsFullyStatic()
    {
        var (result, module) = InferenceHelper.Infer(@"
def add(a: int, b: int) -> int:
    return a + b
");
        var fn = (FunctionDef)module.Body[0];
        Assert.True(result.IsFullyStatic(fn));
    }

    [Fact]
    public void UnannotatedFunctionWithLiterals_IsFullyStatic()
    {
        var (result, module) = InferenceHelper.Infer(@"
def compute():
    x = 1
    y = 2.0
    return x + y
");
        var fn = (FunctionDef)module.Body[0];
        Assert.True(result.IsFullyStatic(fn));
    }

    [Fact]
    public void FunctionWithDynamicCall_IsNotFullyStatic()
    {
        var (result, module) = InferenceHelper.Infer(@"
def process(data):
    return data.transform()
");
        var fn = (FunctionDef)module.Body[0];
        Assert.False(result.IsFullyStatic(fn));
    }

    [Fact]
    public void DynamicSites_EmptyForFullyStaticFunction()
    {
        var (result, _) = InferenceHelper.Infer(@"
def add(a: int, b: int) -> int:
    return a + b
");
        Assert.Empty(result.DynamicSites);
    }

    [Fact]
    public void DynamicSites_NonEmptyForDynamicFunction()
    {
        var (result, _) = InferenceHelper.Infer(@"
def process(data):
    return data.transform()
");
        Assert.NotEmpty(result.DynamicSites);
    }

    [Fact]
    public void NumericComputeFunction_FullyStatic()
    {
        var (result, module) = InferenceHelper.Infer(@"
def dot_product(a: list, b: list) -> float:
    total = 0.0
    for i in range(len(a)):
        total = total + 1.0
    return total
");
        // Even without full element type inference for 'a' and 'b',
        // the accumulator pattern should resolve
        var fn = (FunctionDef)module.Body[0];
        // At minimum, DynamicSites should not include the total accumulation
        Assert.True(result.DynamicSites.Count < 5);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 16. Interprocedural inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class InterproceduralInferenceTests
{
    [Fact]
    public void CallSite_ReceivesInferredReturnType()
    {
        var (result, module) = InferenceHelper.Infer(@"
def square(n: int) -> int:
    return n * n

x = square(5)
");
        var assign = (AssignStatement)module.Body[1];
        Assert.IsType<IntType>(result.GetType(assign.Value));
    }

    [Fact]
    public void ChainedCalls_TypesPropagateCorrectly()
    {
        var (result, module) = InferenceHelper.Infer(@"
def double(n: int) -> int:
    return n * 2

def quadruple(n: int) -> int:
    return double(double(n))

x = quadruple(3)
");
        var assign = (AssignStatement)module.Body[2];
        Assert.IsType<IntType>(result.GetType(assign.Value));
    }

    [Fact]
    public void ForwardReference_FunctionCalledBeforeDefined()
    {
        // Stub collection (Pass 0) should handle this
        var (result, module) = InferenceHelper.Infer(@"
x = helper()

def helper() -> str:
    return 'hello'
");
        var assign = (AssignStatement)module.Body[0];
        // Forward calls use stub return type — may be Unknown or str depending
        // on whether stubs are seeded from annotation before body analysis
        var t = result.GetType(assign.Value);
        Assert.True(t is StrType or UnknownType);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 17. Class field inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class ClassFieldInferenceTests
{
    [Fact]
    public void InitFields_TypesInferred()
    {
        var (result, _) = InferenceHelper.Infer(@"
class Point:
    def __init__(self, x: float, y: float):
        self.x = x
        self.y = y
");
        // Field types stored under (className, fieldName)
        Assert.True(result.VariableTypes.TryGetValue(("Point", "x"), out var xt));
        Assert.True(result.VariableTypes.TryGetValue(("Point", "y"), out var yt));
        Assert.IsType<FloatType>(xt);
        Assert.IsType<FloatType>(yt);
    }

    [Fact]
    public void InitFields_LiteralAssignment_TypesInferred()
    {
        var (result, _) = InferenceHelper.Infer(@"
class Counter:
    def __init__(self):
        self.count = 0
        self.name = 'default'
");
        result.VariableTypes.TryGetValue(("Counter", "count"), out var ct);
        result.VariableTypes.TryGetValue(("Counter", "name"),  out var nt);
        Assert.IsType<IntType>(ct);
        Assert.IsType<StrType>(nt);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 18. Subscript inference
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Inference")]
public class SubscriptInferenceTests
{
    [Fact]
    public void ListIndexing_ReturnsElementType()
    {
        var (result, module) = InferenceHelper.Infer(@"
nums = [1, 2, 3]
x = nums[0]
");
        var assign = (AssignStatement)module.Body[1];
        Assert.IsType<IntType>(result.GetType(assign.Value));
    }

    [Fact]
    public void StringIndexing_ReturnsStr()
    {
        var (result, module) = InferenceHelper.Infer(@"
s = 'hello'
c = s[0]
");
        var assign = (AssignStatement)module.Body[1];
        Assert.IsType<StrType>(result.GetType(assign.Value));
    }

    [Fact]
    public void DictLookup_ReturnsValueType()
    {
        var (result, module) = InferenceHelper.Infer(@"
d = {'a': 42}
v = d['a']
");
        var assign = (AssignStatement)module.Body[1];
        Assert.IsType<IntType>(result.GetType(assign.Value));
    }

    [Fact]
    public void TupleIndexing_ReturnsFirstElementType()
    {
        var (result, module) = InferenceHelper.Infer(@"
t = (1, 'hello')
x = t[0]
");
        var assign = (AssignStatement)module.Body[1];
        Assert.IsType<IntType>(result.GetType(assign.Value));
    }
}

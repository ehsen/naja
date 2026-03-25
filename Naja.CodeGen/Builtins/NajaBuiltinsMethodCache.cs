using System.Reflection;
using System.Reflection.Emit;
using Naja.CodeGen.Builtins;

namespace Naja.CodeGen;

/// <summary>
/// Cached method lookups for all NajaBuiltins static methods used in IL generation.
/// Consolidates reflection-based method resolution into a single point, enabling
/// easier migration to dedicated builtin modules later (Phase 5 continuation).
/// 
/// Usage: Replace `typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MethodName))`
/// with `NajaBuiltinsMethodCache.MethodName_Method`
/// </summary>
internal static class NajaBuiltinsMethodCache
{
    // ── Dynamic operators ─────────────────────────────────────────────────────
    public static readonly MethodInfo DynamicAdd_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.DynamicAdd))!;
    
    public static readonly MethodInfo DynamicSub_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.DynamicSub))!;
    
    public static readonly MethodInfo DynamicMul_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.DynamicMul))!;
    
    public static readonly MethodInfo DynamicMod_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.DynamicMod))!;

    public static readonly MethodInfo DynamicMatMul_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.DynamicMatMul))!;

    // ── Type conversions ──────────────────────────────────────────────────────
    public static readonly MethodInfo ToFloat_Method = 
        typeof(TypeConversion).GetMethod(nameof(TypeConversion.ToFloat))!;
    
    public static readonly MethodInfo ToInt_Method = 
        typeof(TypeConversion).GetMethod(nameof(TypeConversion.ToInt))!;
    
    public static readonly MethodInfo ToBool_Method = 
        typeof(TypeConversion).GetMethod(nameof(TypeConversion.ToBool))!;
    
    public static readonly MethodInfo ToString_Method = 
        typeof(TypeConversion).GetMethod(nameof(TypeConversion.ToStr))!;

    // ── Arithmetic operations ─────────────────────────────────────────────────
    public static readonly MethodInfo PyFloorDiv_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.PyFloorDiv))!;
    
    public static readonly MethodInfo PyFloorDivF_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.PyFloorDivF))!;
    
    public static readonly MethodInfo PyMod_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.PyMod))!;
    
    public static readonly MethodInfo PyModF_Method = 
        typeof(DynamicOperators).GetMethod(nameof(DynamicOperators.PyModF))!;

    public static readonly MethodInfo Abs_Method = 
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.Abs))!;

    public static readonly MethodInfo ParseBigInt_Method =
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.ParseBigInt))!;

    public static readonly MethodInfo NegateBigInt_Method =
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.NegateBigInt))!;

    public static readonly MethodInfo Max_Method = 
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.Max))!;

    public static readonly MethodInfo Min_Method = 
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.Min))!;

    public static readonly MethodInfo Sum_Method = 
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.Sum))!;

    public static readonly MethodInfo Round_Method = 
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.Round))!;

    public static readonly MethodInfo Pow_Method = 
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.Pow))!;

    public static readonly MethodInfo DivMod_Method = 
        typeof(MathFunctions).GetMethod(nameof(MathFunctions.DivMod))!;

    public static readonly MethodInfo Sorted_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Sorted))!;

    public static readonly MethodInfo Reversed_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Reversed))!;

    public static readonly MethodInfo Enumerate_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Enumerate))!;

    public static readonly MethodInfo Zip_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Zip))!;

    public static readonly MethodInfo Map_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Map))!;

    public static readonly MethodInfo Filter_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Filter))!;

    public static readonly MethodInfo Any_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Any))!;

    public static readonly MethodInfo All_Method = 
        typeof(Collections).GetMethod(nameof(Collections.All))!;

    public static readonly MethodInfo MakeList_Method = 
        typeof(Collections).GetMethod(nameof(Collections.MakeList))!;

    public static readonly MethodInfo MakeDict_Method = 
        typeof(Collections).GetMethod(nameof(Collections.MakeDict))!;

    public static readonly MethodInfo MakeSet_Method = 
        typeof(Collections).GetMethod(nameof(Collections.MakeSet))!;

    public static readonly MethodInfo MakeFrozenSet_Method = 
        typeof(Collections).GetMethod(nameof(Collections.MakeFrozenSet))!;

    public static readonly MethodInfo MakeTuple_Method = 
        typeof(Collections).GetMethod(nameof(Collections.MakeTuple))!;

    // ── Comparison operations ─────────────────────────────────────────────────
    public static readonly MethodInfo DynamicEq_Method = 
        typeof(ComparisonOperators).GetMethod(nameof(ComparisonOperators.DynamicEq))!;

    public static readonly MethodInfo DynamicNotEq_Method = 
        typeof(ComparisonOperators).GetMethod(nameof(ComparisonOperators.DynamicNotEq))!;

    public static readonly MethodInfo DynamicLt_Method = 
        typeof(ComparisonOperators).GetMethod(nameof(ComparisonOperators.DynamicLt))!;

    public static readonly MethodInfo DynamicLtEq_Method = 
        typeof(ComparisonOperators).GetMethod(nameof(ComparisonOperators.DynamicLtEq))!;

    public static readonly MethodInfo DynamicGt_Method = 
        typeof(ComparisonOperators).GetMethod(nameof(ComparisonOperators.DynamicGt))!;

    public static readonly MethodInfo DynamicGtEq_Method = 
        typeof(ComparisonOperators).GetMethod(nameof(ComparisonOperators.DynamicGtEq))!;

    // ── Container operations ──────────────────────────────────────────────────
    public static readonly MethodInfo Contains_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.Contains))!;

    public static readonly MethodInfo SetItem_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.SetItem))!;

    // ── Attribute operations ─────────────────────────────────────────────────
    public static readonly MethodInfo SetAttr_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.SetAttr))!;

    public static readonly MethodInfo HasAttr_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.HasAttr))!;

    public static readonly MethodInfo GetAttr_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.GetAttr))!;

    public static readonly MethodInfo GetStaticAttr_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.GetStaticAttr))!;

    // ── Type operations ─────────────────────────────────────────────────────
    public static readonly MethodInfo TypeOf_Method = 
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.TypeOf))!;

    public static readonly MethodInfo IsInstance_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.IsInstance))!;

    public static readonly MethodInfo IsSubclass_Method =
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.IsSubclass))!;

    public static readonly MethodInfo Callable_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.Callable))!;

    // ── I/O operations ──────────────────────────────────────────────────────
    public static readonly MethodInfo Print_Method = 
        typeof(IOFunctions).GetMethod(nameof(IOFunctions.Print))!;

    public static readonly MethodInfo Input_Method = 
        typeof(IOFunctions).GetMethod(nameof(IOFunctions.Input))!;

    // ── Collection operations ────────────────────────────────────────────────
    public static readonly MethodInfo Len_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Len))!;

    public static readonly MethodInfo Range_Method = 
        typeof(Collections).GetMethod(nameof(Collections.Range))!;

    public static readonly MethodInfo Chr_Method = 
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.Chr))!;

    public static readonly MethodInfo Ord_Method = 
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.Ord))!;

    public static readonly MethodInfo Hex_Method = 
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.Hex))!;

    public static readonly MethodInfo Bin_Method = 
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.Bin))!;

    public static readonly MethodInfo Oct_Method = 
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.Oct))!;

    public static readonly MethodInfo Open_Method = 
        typeof(IOFunctions).GetMethod(nameof(IOFunctions.Open))!;

    public static readonly MethodInfo Id_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.Id))!;

    public static readonly MethodInfo Hash_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.Hash))!;

    public static readonly MethodInfo Vars_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.Vars))!;

    public static readonly MethodInfo Dir_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.Dir))!;

    public static readonly MethodInfo Globals_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Globals))!;

    public static readonly MethodInfo Locals_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Locals))!;

    public static readonly MethodInfo Iter_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.Iter))!;

    public static readonly MethodInfo NextVararg_Method = 
        typeof(Iterators).GetMethod(nameof(Iterators.NextVararg))!;

    public static readonly MethodInfo GetForLoopEnumerator_Method =
        typeof(Iterators).GetMethod(nameof(Iterators.GetForLoopEnumerator))!;

    // ── Exception handling ───────────────────────────────────────────────────
    public static readonly MethodInfo Assert_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.Assert))!;

    public static readonly MethodInfo SetExceptionCause_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.SetExceptionCause))!;

    public static readonly MethodInfo SetExceptionContext_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.SetExceptionContext))!;

    public static readonly MethodInfo EnsureException_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.EnsureException))!;

    public static readonly MethodInfo ContextExitWithException_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.ContextExitWithException))!;

    public static readonly MethodInfo ContextExit_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.ContextExit))!;

    // ── Collection construction ──────────────────────────────────────────────
    public static readonly MethodInfo CreateFunctionWithDefaults_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.CreateFunctionWithDefaults))!;

    public static readonly MethodInfo IteratorMoveNext_Method = 
        typeof(Iterators).GetMethod(nameof(Iterators.IteratorMoveNext))!;

    // ── String operations ────────────────────────────────────────────────────
    public static readonly MethodInfo Format_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.Format))!;

    public static readonly MethodInfo StrUpper_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrUpper))!;

    public static readonly MethodInfo StrLower_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrLower))!;

    public static readonly MethodInfo StrStrip_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrStrip))!;

    public static readonly MethodInfo StrLStrip_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrLStrip))!;

    // ── Collection operations ────────────────────────────────────────────────
    public static readonly MethodInfo GetItem_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.GetItem))!;

    public static readonly MethodInfo GetUnpackSlice_Method = 
        typeof(Collections).GetMethod(nameof(Collections.GetUnpackSlice))!;

    public static readonly MethodInfo UnpackIterable_Method = 
        typeof(Collections).GetMethod(nameof(Collections.UnpackIterable))!;

    // ── Exception & Context operations ───────────────────────────────────────
    public static readonly MethodInfo ToStr_Method = 
        typeof(TypeConversion).GetMethod(nameof(TypeConversion.ToStr))!;

    public static readonly MethodInfo Repr_Method = 
        typeof(TypeConversion).GetMethod(nameof(TypeConversion.Repr))!;

    public static readonly MethodInfo ContextEnter_Method = 
        typeof(ExceptionHelpers).GetMethod(nameof(ExceptionHelpers.ContextEnter))!;

    // ── Dynamic call operations ──────────────────────────────────────────────
    public static readonly MethodInfo CallCallable_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.CallCallable))!;

    public static readonly MethodInfo DynamicCall_Method = 
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.DynamicCall))!;

    public static readonly MethodInfo CreateDotNet_Method = 
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.CreateDotNet))!;

    // ── String method references ─────────────────────────────────────────────
    public static readonly MethodInfo StrRStrip_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrRStrip))!;

    public static readonly MethodInfo StrStartsWith_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrStartsWith))!;

    public static readonly MethodInfo StrEndsWith_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrEndsWith))!;

    public static readonly MethodInfo StrIsDigit_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrIsDigit))!;

    public static readonly MethodInfo StrIsAlpha_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrIsAlpha))!;

    public static readonly MethodInfo StrIsAlNum_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrIsAlNum))!;

    public static readonly MethodInfo StrFind_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrFind))!;

    public static readonly MethodInfo StrIndex_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrIndex))!;

    public static readonly MethodInfo StrReplace_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrReplace))!;

    public static readonly MethodInfo StrCenter_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrCenter))!;

    public static readonly MethodInfo StrLJust_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrLJust))!;

    public static readonly MethodInfo StrRJust_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrRJust))!;

    public static readonly MethodInfo StrZFill_Method =
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrZFill))!;

    public static readonly MethodInfo StrCount_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrCount))!;

    public static readonly MethodInfo StrJoin_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrJoin))!;

    public static readonly MethodInfo StrSplit_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrSplit))!;

    public static readonly MethodInfo StrSplitLines_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrSplitLines))!;

    public static readonly MethodInfo StrTitle_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrTitle))!;

    public static readonly MethodInfo StrEncode_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrEncode))!;

    public static readonly MethodInfo StrFormat_Method = 
        typeof(StringFunctions).GetMethod(nameof(StringFunctions.StrFormat))!;

    // ── List method references ───────────────────────────────────────────────
    public static readonly MethodInfo ListAppend_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListAppend))!;

    public static readonly MethodInfo ListExtend_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListExtend))!;

    public static readonly MethodInfo ListInsert_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListInsert))!;

    public static readonly MethodInfo ListPop_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListPop))!;

    public static readonly MethodInfo ListRemove_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListRemove))!;

    public static readonly MethodInfo ListReverse_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListReverse))!;

    public static readonly MethodInfo ListSort_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListSort))!;

    public static readonly MethodInfo ListIndex_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListIndex))!;

    public static readonly MethodInfo ListCount_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListCount))!;

    public static readonly MethodInfo ListCopy_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListCopy))!;

    public static readonly MethodInfo ListClear_Method = 
        typeof(Collections).GetMethod(nameof(Collections.ListClear))!;

    // ── Dict method references ───────────────────────────────────────────────
    public static readonly MethodInfo DictKeys_Method = 
        typeof(Collections).GetMethod(nameof(Collections.DictKeys))!;

    public static readonly MethodInfo DictValues_Method = 
        typeof(Collections).GetMethod(nameof(Collections.DictValues))!;

    public static readonly MethodInfo DictItems_Method = 
        typeof(Collections).GetMethod(nameof(Collections.DictItems))!;

    public static readonly MethodInfo DictGet_Method = 
        typeof(Collections).GetMethod(nameof(Collections.DictGet))!;

    public static readonly MethodInfo DictPop_Method = 
        typeof(Collections).GetMethod(nameof(Collections.DictPop))!;

    public static readonly MethodInfo DictUpdate_Method = 
        typeof(Collections).GetMethod(nameof(Collections.DictUpdate))!;

    public static readonly MethodInfo DictClear_Method = 
        typeof(Collections).GetMethod(nameof(Collections.DictClear))!;

    public static readonly MethodInfo DictCopy_Method = 
        typeof(Collections).GetMethod(nameof(Collections.DictCopy))!;

    // ── Event handler operations ─────────────────────────────────────────────
    public static readonly MethodInfo AddEventHandler_Method =
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.AddEventHandler))!;

    public static readonly MethodInfo RemoveEventHandler_Method =
        typeof(ReflectionHelpers).GetMethod(nameof(ReflectionHelpers.RemoveEventHandler))!;

    // ── Exception variable deletion sentinel ────────────────────────────────
    public static readonly FieldInfo DeletedSentinel_Field =
        typeof(ExceptionHelpers).GetField(nameof(ExceptionHelpers.DeletedSentinel))!;

    // ── Type & Reflection operations ──────────────────────────────────────────
    public static readonly MethodInfo ResolveTypeByName_Method = 
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.ResolveTypeByName))!;

    // ── Generator operations ──────────────────────────────────────────────────
    public static readonly ConstructorInfo NajaGenerator_Ctor =
        typeof(NajaGenerator).GetConstructor(
            new Type[] { typeof(Action<NajaGenerator, object[]>), typeof(object[]) })!;

    public static readonly MethodInfo NajaGenerator_Yield_Method =
        typeof(NajaGenerator).GetMethod(nameof(NajaGenerator.Yield))!;

    public static readonly MethodInfo NajaGenerator_Send_Method =
        typeof(NajaGenerator).GetMethod(nameof(NajaGenerator.Send))!;

    public static readonly ConstructorInfo NajaGeneratorReturn_Ctor =
        typeof(NajaGeneratorReturn).GetConstructor(new Type[] { typeof(object) })!;

    // ── Scripting builtins (compile / eval / exec) ───────────────────────────
    public static readonly MethodInfo Compile_Method =
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.Compile))!;

    public static readonly MethodInfo Eval_Method =
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.Eval))!;

    public static readonly MethodInfo Exec_Method =
        typeof(TypeSystem).GetMethod(nameof(TypeSystem.Exec))!;

    // ── Python descriptor wrappers ────────────────────────────────────────────
    public static readonly MethodInfo MakeStaticMethod_Method =
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeStaticMethod))!;

    public static readonly MethodInfo MakeClassMethod_Method =
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeClassMethod))!;
}

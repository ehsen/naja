using System.Reflection;
using System.Reflection.Emit;

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
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicAdd))!;
    
    public static readonly MethodInfo DynamicSub_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicSub))!;
    
    public static readonly MethodInfo DynamicMul_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicMul))!;
    
    public static readonly MethodInfo DynamicMod_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicMod))!;

    // ── Type conversions ──────────────────────────────────────────────────────
    public static readonly MethodInfo ToFloat_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToFloat))!;
    
    public static readonly MethodInfo ToInt_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToInt))!;
    
    public static readonly MethodInfo ToBool_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToBool))!;
    
    public static readonly MethodInfo ToString_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToString))!;

    // ── Arithmetic operations ─────────────────────────────────────────────────
    public static readonly MethodInfo PyFloorDiv_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.PyFloorDiv))!;
    
    public static readonly MethodInfo PyFloorDivF_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.PyFloorDivF))!;
    
    public static readonly MethodInfo PyMod_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.PyMod))!;
    
    public static readonly MethodInfo PyModF_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.PyModF))!;

    public static readonly MethodInfo Abs_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Abs))!;

    public static readonly MethodInfo Max_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Max))!;

    public static readonly MethodInfo Min_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Min))!;

    public static readonly MethodInfo Sum_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Sum))!;

    public static readonly MethodInfo Round_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Round))!;

    public static readonly MethodInfo Pow_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Pow))!;

    public static readonly MethodInfo DivMod_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DivMod))!;

    public static readonly MethodInfo Sorted_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Sorted))!;

    public static readonly MethodInfo Reversed_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Reversed))!;

    public static readonly MethodInfo Enumerate_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Enumerate))!;

    public static readonly MethodInfo Zip_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Zip))!;

    public static readonly MethodInfo Map_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Map))!;

    public static readonly MethodInfo Filter_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Filter))!;

    public static readonly MethodInfo Any_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Any))!;

    public static readonly MethodInfo All_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.All))!;

    public static readonly MethodInfo MakeList_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeList))!;

    public static readonly MethodInfo MakeDict_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeDict))!;

    public static readonly MethodInfo MakeSet_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeSet))!;

    public static readonly MethodInfo MakeFrozenSet_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeFrozenSet))!;

    public static readonly MethodInfo MakeTuple_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.MakeTuple))!;

    // ── Comparison operations ─────────────────────────────────────────────────
    public static readonly MethodInfo DynamicEq_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicEq))!;
    
    public static readonly MethodInfo DynamicNotEq_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicNotEq))!;
    
    public static readonly MethodInfo DynamicLt_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicLt))!;
    
    public static readonly MethodInfo DynamicLtEq_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicLtEq))!;
    
    public static readonly MethodInfo DynamicGt_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicGt))!;
    
    public static readonly MethodInfo DynamicGtEq_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicGtEq))!;

    // ── Container operations ──────────────────────────────────────────────────
    public static readonly MethodInfo Contains_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Contains))!;

    public static readonly MethodInfo SetItem_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.SetItem))!;

    // ── Attribute operations ─────────────────────────────────────────────────
    public static readonly MethodInfo SetAttr_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.SetAttr))!;

    public static readonly MethodInfo HasAttr_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.HasAttr))!;

    public static readonly MethodInfo GetAttr_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetAttr))!;

    public static readonly MethodInfo GetStaticAttr_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetStaticAttr))!;

    // ── Type operations ─────────────────────────────────────────────────────
    public static readonly MethodInfo TypeOf_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.TypeOf))!;

    public static readonly MethodInfo IsInstance_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.IsInstance))!;

    public static readonly MethodInfo Callable_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Callable))!;

    // ── I/O operations ──────────────────────────────────────────────────────
    public static readonly MethodInfo Print_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Print))!;

    public static readonly MethodInfo Input_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Input))!;

    // ── Collection operations ────────────────────────────────────────────────
    public static readonly MethodInfo Len_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Len))!;

    public static readonly MethodInfo Range_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Range))!;

    public static readonly MethodInfo Chr_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Chr))!;

    public static readonly MethodInfo Ord_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Ord))!;

    public static readonly MethodInfo Hex_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Hex))!;

    public static readonly MethodInfo Bin_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Bin))!;

    public static readonly MethodInfo Oct_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Oct))!;

    public static readonly MethodInfo Open_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Open))!;

    public static readonly MethodInfo Id_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Id))!;

    public static readonly MethodInfo Hash_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Hash))!;

    public static readonly MethodInfo Vars_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Vars))!;

    public static readonly MethodInfo Dir_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Dir))!;

    public static readonly MethodInfo Iter_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Iter))!;

    public static readonly MethodInfo NextVararg_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.NextVararg))!;

    // ── Exception handling ───────────────────────────────────────────────────
    public static readonly MethodInfo Assert_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Assert))!;

    public static readonly MethodInfo SetExceptionCause_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.SetExceptionCause))!;

    public static readonly MethodInfo EnsureException_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.EnsureException))!;

    public static readonly MethodInfo ContextExitWithException_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ContextExitWithException))!;

    public static readonly MethodInfo ContextExit_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ContextExit))!;

    // ── Collection construction ──────────────────────────────────────────────
    public static readonly MethodInfo CreateFunctionWithDefaults_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.CreateFunctionWithDefaults))!;

    public static readonly MethodInfo IteratorMoveNext_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.IteratorMoveNext))!;

    // ── String operations ────────────────────────────────────────────────────
    public static readonly MethodInfo Format_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Format))!;

    public static readonly MethodInfo StrUpper_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrUpper))!;

    public static readonly MethodInfo StrLower_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrLower))!;

    public static readonly MethodInfo StrStrip_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrStrip))!;

    public static readonly MethodInfo StrLStrip_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrLStrip))!;

    // ── Collection operations ────────────────────────────────────────────────
    public static readonly MethodInfo GetItem_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetItem))!;

    public static readonly MethodInfo GetUnpackSlice_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.GetUnpackSlice))!;

    public static readonly MethodInfo UnpackIterable_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.UnpackIterable))!;

    // ── Exception & Context operations ───────────────────────────────────────
    public static readonly MethodInfo ToStr_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ToStr))!;

    public static readonly MethodInfo Repr_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.Repr))!;

    public static readonly MethodInfo ContextEnter_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ContextEnter))!;

    // ── Dynamic call operations ──────────────────────────────────────────────
    public static readonly MethodInfo CallCallable_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.CallCallable))!;

    public static readonly MethodInfo DynamicCall_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DynamicCall))!;

    public static readonly MethodInfo CreateDotNet_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.CreateDotNet))!;

    // ── String method references ─────────────────────────────────────────────
    public static readonly MethodInfo StrRStrip_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrRStrip))!;

    public static readonly MethodInfo StrStartsWith_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrStartsWith))!;

    public static readonly MethodInfo StrEndsWith_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrEndsWith))!;

    public static readonly MethodInfo StrIsDigit_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrIsDigit))!;

    public static readonly MethodInfo StrIsAlpha_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrIsAlpha))!;

    public static readonly MethodInfo StrIsAlNum_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrIsAlNum))!;

    public static readonly MethodInfo StrFind_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrFind))!;

    public static readonly MethodInfo StrIndex_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrIndex))!;

    public static readonly MethodInfo StrReplace_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrReplace))!;

    public static readonly MethodInfo StrCenter_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrCenter))!;

    public static readonly MethodInfo StrLJust_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrLJust))!;

    public static readonly MethodInfo StrRJust_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrRJust))!;

    public static readonly MethodInfo StrZFill_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrZFill))!;

    public static readonly MethodInfo StrCount_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrCount))!;

    public static readonly MethodInfo StrJoin_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrJoin))!;

    public static readonly MethodInfo StrSplit_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrSplit))!;

    public static readonly MethodInfo StrSplitLines_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrSplitLines))!;

    public static readonly MethodInfo StrTitle_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.StrTitle))!;

    // ── List method references ───────────────────────────────────────────────
    public static readonly MethodInfo ListAppend_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListAppend))!;

    public static readonly MethodInfo ListExtend_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListExtend))!;

    public static readonly MethodInfo ListInsert_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListInsert))!;

    public static readonly MethodInfo ListPop_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListPop))!;

    public static readonly MethodInfo ListRemove_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListRemove))!;

    public static readonly MethodInfo ListReverse_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListReverse))!;

    public static readonly MethodInfo ListSort_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListSort))!;

    public static readonly MethodInfo ListIndex_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListIndex))!;

    public static readonly MethodInfo ListCount_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListCount))!;

    public static readonly MethodInfo ListCopy_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListCopy))!;

    public static readonly MethodInfo ListClear_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ListClear))!;

    // ── Dict method references ───────────────────────────────────────────────
    public static readonly MethodInfo DictKeys_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictKeys))!;

    public static readonly MethodInfo DictValues_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictValues))!;

    public static readonly MethodInfo DictItems_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictItems))!;

    public static readonly MethodInfo DictGet_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictGet))!;

    public static readonly MethodInfo DictPop_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictPop))!;

    public static readonly MethodInfo DictUpdate_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictUpdate))!;

    public static readonly MethodInfo DictClear_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictClear))!;

    public static readonly MethodInfo DictCopy_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.DictCopy))!;

    // ── Type & Reflection operations ──────────────────────────────────────────
    public static readonly MethodInfo ResolveTypeByName_Method = 
        typeof(NajaBuiltins).GetMethod(nameof(NajaBuiltins.ResolveTypeByName))!;
}

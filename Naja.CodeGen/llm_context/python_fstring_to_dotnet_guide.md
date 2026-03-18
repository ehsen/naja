# Python 3.14 F-String Format Spec → .NET Compiler Translation Guide

## Problem Statement

When compiling Python f-strings to C#, `EmitFString(FStringExpr)` must not blindly emit `string.Format("{0:" + csFmt + "}", value)` for all non-empty format specs. Python format codes like `b` (binary), `o` (octal), `%` (percent), fill/align characters, and several flags have **no valid equivalent** in .NET composite format strings. Passing them to `string.Format` produces incorrect output silently — no exception, just wrong data.

**Root cause:** Missing dispatch gate — all non-empty specs fall through to `string.Format` instead of routing Python-semantic specs to the runtime helper that already implements Python-style formatting.

---

## Architecture: Three-Way Dispatch

Every formatted value in an f-string must be routed through one of three paths:

```
EmitFString(FStringExpr)
        │
        ▼
  format_spec empty?
   ┌─────┴──────┐
  YES           NO
   │             │
   ▼             ▼
Convert.ToString   Classify format spec
    (value)        (parse type char + flags)
                      │
          ┌───────────┼────────────┐
          ▼           ▼            ▼
   .NET-native   Python-semantic  Dynamic/nested
   d,f,e,g,n,   b,o,fill/align,  spec is an
   x,X           %,+ flag, etc.  f-string expr
          │           │            │
          ▼           ▼            ▼
  string.Format   PyRuntime     PyRuntime
  ("{0:csFmt}",   .Format(val,  .Format(val,
   val)           "pySpec")     specExpr)
```

---

## Format Spec Classification Table

| Python spec | Example | .NET path | Emit | Notes |
|---|---|---|---|---|
| `d` | `{n:d}` | `string.Format` | `"{0:D}"` | Safe if no fill/align flags |
| `f` | `{x:.2f}` | `string.Format` | `"{0:F2}"` | Safe if no fill/align flags |
| `e` | `{x:.3e}` | `string.Format` | `"{0:E3}"` | Output differs: .NET uses `E+003`, Python uses `e+03` |
| `g` | `{x:.4g}` | `string.Format` | `"{0:G4}"` | Close but edge cases differ |
| `x` | `{n:x}` | `string.Format` | `"{0:x}"` | Safe |
| `X` | `{n:X}` | `string.Format` | `"{0:X}"` | Safe |
| `n` | `{n:n}` | `string.Format` | `"{0:N}"` | Culture-sensitive in both |
| `b` | `{n:08b}` | **`PyRuntime.Format`** | `PyRuntime.Format(n, "08b")` | .NET has no binary specifier |
| `o` | `{n:o}` | **`PyRuntime.Format`** | `PyRuntime.Format(n, "o")` | .NET has no octal specifier |
| `s` | `{s:s}` | `Convert.ToString` | `Convert.ToString(val)` | Explicit string type |
| `%` | `{x:.1%}` | **`PyRuntime.Format`** | `PyRuntime.Format(x, ".1%")` | Python: `42.5%`, .NET: `42.50 %` (space + locale) |
| fill+align | `{s:>10}` | **`PyRuntime.Format`** | `PyRuntime.Format(s, ">10")` | Fill char behaviour differs from `PadLeft`/`PadRight` |
| `+` flag | `{n:+d}` | **`PyRuntime.Format`** | `PyRuntime.Format(n, "+d")` | .NET `+#` does not work the same way |
| `#` flag | `{n:#b}` | **`PyRuntime.Format`** | `PyRuntime.Format(n, "#b")` | Python prefixes `0b`/`0o`/`0x` |
| `_` grouping | `{n:_}` | **`PyRuntime.Format`** | `PyRuntime.Format(n, "_")` | .NET has no `_` thousands separator |
| nested spec | `{n:{w}.{p}f}` | **`PyRuntime.Format`** | `PyRuntime.Format(n, specExpr)` | Dynamic — must always be runtime |

---

## Classifier Implementation (Compiler-Side C#)

Add this classifier to your `EmitFString` layer. It returns `true` only for specs that are safe to pass to `string.Format`:

```csharp
static bool IsNetNativeSpec(string pySpec, out string csSpec)
{
    csSpec = null;
    if (string.IsNullOrEmpty(pySpec)) return false;

    // Last character is the Python type code
    var typeChar = pySpec[^1];

    // Always route to runtime helper
    if ("bos%".Contains(typeChar))    return false; // binary, octal, string, percent
    if (pySpec.Contains('_'))         return false; // _ grouping separator
    if (pySpec.Contains('+'))         return false; // explicit positive sign
    if (pySpec.Contains('#'))         return false; // alternate form (0b, 0o, 0x prefix)
    if (HasFillAlign(pySpec))         return false; // fill/align characters present

    // Safe subset: d, f, e, g, n, x, X with optional width.precision only
    csSpec = TranslateSimpleSpec(pySpec, typeChar);
    return csSpec != null;
}

static bool HasFillAlign(string spec)
{
    // Python alignment characters: <  >  ^  =
    return spec.IndexOfAny(['<', '>', '^', '=']) >= 0;
}

static string TranslateSimpleSpec(string spec, char typeChar)
{
    // Strip the type char, leaving optional [[0]width][.precision]
    var body = spec[..^1];
    return typeChar switch
    {
        'd' => "D" + body,          // {n:05d} → {0:D5}  (zero-pad works in both)
        'f' => ParseFP(body, 'F'),  // {x:.2f} → {0:F2}
        'e' => ParseFP(body, 'E'),  // close but not identical — see notes above
        'g' => ParseFP(body, 'G'),
        'n' => "N" + body,
        'x' => "x" + body,
        'X' => "X" + body,
        _   => null                 // unknown — punt to runtime
    };
}

static string ParseFP(string body, char netType)
{
    // body examples: ".2"  "10.2"  "10"  ""
    // .NET composite format only takes precision, not width, in the format token
    var dotIdx = body.IndexOf('.');
    if (dotIdx >= 0)
        return netType + body[(dotIdx + 1)..]; // extract precision digits only
    return netType.ToString();
}
```

---

## Updated `EmitFString` Dispatch Logic

```csharp
void EmitFString(FStringExpr expr)
{
    foreach (var part in expr.Parts)
    {
        if (part is LiteralPart lit)
        {
            Emit(QuoteString(lit.Value));
        }
        else if (part is FormattedValue fv)
        {
            var valueExpr = EmitExpression(fv.Value);

            if (string.IsNullOrEmpty(fv.FormatSpec))
            {
                // Path 1: No spec — just stringify
                Emit($"Convert.ToString({valueExpr})");
            }
            else if (IsNetNativeSpec(fv.FormatSpec, out var csSpec))
            {
                // Path 2: .NET-native spec — safe to use string.Format
                Emit($"string.Format(\"{{0:{csSpec}}}\", {valueExpr})");
            }
            else
            {
                // Path 3: Python-semantic or dynamic spec — delegate to runtime helper
                // This is what was missing for b, o, %, fill/align, +, #, _, nested specs
                var specLiteral = QuoteString(fv.FormatSpec);
                Emit($"PyRuntime.Format({valueExpr}, {specLiteral})");
            }
        }
    }
}
```

---

## `PyRuntime.Format` Implementation

Your runtime helper must implement the full Python format mini-language:

```
[[fill]align][sign][#][0][width][grouping_option][.precision][type]
```

```csharp
public static string Format(object value, string spec)
{
    var p = ParsePyFormatSpec(spec);

    // Step 1: Produce the raw formatted digits/string
    string raw = p.Type switch
    {
        'b'  => Convert.ToString(Convert.ToInt64(value), 2),   // base-2
        'o'  => Convert.ToString(Convert.ToInt64(value), 8),   // base-8
        'x'  => Convert.ToInt64(value).ToString("x"),
        'X'  => Convert.ToInt64(value).ToString("X"),
        'd'  => Convert.ToInt64(value).ToString(),
        'f'  => FormatFixed(value, p.Precision ?? 6),
        'e'  => FormatSci(value, p.Precision ?? 6),    // must emit e+01 not E+001
        'g'  => FormatGeneral(value, p.Precision ?? 6),
        '%'  => FormatPercent(value, p.Precision ?? 6), // multiply ×100, append %
        's' or '\0' => value?.ToString() ?? "None",
        _    => throw new PythonFormatError($"Unknown format code '{p.Type}'")
    };

    // Step 2: Apply alternate form prefix (# flag)
    if (p.AltForm)
    {
        raw = p.Type switch
        {
            'b' => "0b" + raw,
            'o' => "0o" + raw,
            'x' => "0x" + raw,
            'X' => "0X" + raw,
            _   => raw
        };
    }

    // Step 3: Apply sign
    double numericValue = Convert.ToDouble(value);
    if (p.Sign == '+' && numericValue >= 0)      raw = "+" + raw;
    else if (p.Sign == ' ' && numericValue >= 0) raw = " " + raw;

    // Step 4: Apply zero-padding (before fill/align, after sign/prefix)
    if (p.ZeroPad && p.Width > raw.Length)
        raw = raw.PadLeft(p.Width, '0');

    // Step 5: Apply fill/align
    if (p.Width > raw.Length)
    {
        raw = p.Align switch
        {
            '<' => raw.PadRight(p.Width, p.Fill),
            '>' => raw.PadLeft(p.Width, p.Fill),
            '^' => raw.PadLeft((p.Width + raw.Length) / 2, p.Fill)
                      .PadRight(p.Width, p.Fill),
            '=' => PadAfterSign(raw, p.Width, p.Fill), // sign-aware padding
            _   => raw.PadLeft(p.Width, p.Fill)         // default: right-align for numbers
        };
    }

    return raw;
}
```

### `ParsePyFormatSpec` — Parsed Spec Structure

```csharp
record PyFormatSpec(
    char   Fill,       // fill character (default: ' ')
    char   Align,      // < > ^ = (default: depends on type)
    char   Sign,       // + - ' ' (default: '-')
    bool   AltForm,    // # flag
    bool   ZeroPad,    // 0 flag
    int?   Width,      // minimum field width
    char   Grouping,   // _ or , (default: none)
    int?   Precision,  // digits after decimal / significant digits
    char   Type        // b o x X d f e g n s % (default: '\0')
);
```

### `FormatSci` — Must Match Python's `e` Notation

.NET emits `E+003` (3-digit exponent), Python emits `e+03` (2-digit, lowercase). You must normalise:

```csharp
static string FormatSci(object value, int precision)
{
    // Use G format then post-process, or build manually
    double d = Convert.ToDouble(value);
    string raw = d.ToString("e" + precision); // e.g. "4.200000e+001"
    // Normalize exponent to 2 digits, lowercase e
    return NormalizeExponent(raw);
}

static string NormalizeExponent(string s)
{
    int eIdx = s.IndexOf('e');
    if (eIdx < 0) return s;
    var mantissa = s[..eIdx];
    var expPart  = s[(eIdx + 1)..]; // e.g. "+001" or "-02"
    var sign     = expPart[0];
    var digits   = expPart[1..].TrimStart('0');
    if (digits.Length == 0) digits = "0";
    if (digits.Length == 1) digits = "0" + digits; // always at least 2 digits
    return $"{mantissa}e{sign}{digits}";
}
```

### `FormatPercent` — Python `%` Semantics

```csharp
static string FormatPercent(object value, int precision)
{
    double d = Convert.ToDouble(value) * 100.0;
    return d.ToString("F" + precision) + "%"; // no space — Python: "42.5%", not "42.5 %"
}
```

---

## The `08b` Failure Case — Step by Step

This is the exact scenario that triggered the original bug:

```python
# Python source
result = f"{42:08b}"
# Expected: "00101010"
```

**Old (broken) emitted C#:**
```csharp
// WRONG — string.Format ignores 'b', formats as integer
string result = string.Format("{0:08b}", 42); // → "42"  ✗
```

**New (correct) emitted C#:**
```csharp
// CORRECT — classifier detects 'b' is Python-semantic, routes to runtime helper
string result = PyRuntime.Format(42, "08b");
// Inside PyRuntime.Format:
//   raw    = Convert.ToString(42, 2)  → "101010"
//   ZeroPad to width 8               → "00101010"  ✓
```

---

## Decision Checklist for Adding New Format Codes

When adding support for a new Python format code or flag, ask these questions in order:

1. **Does .NET's `string.Format` composite format support this code natively?**
   - If NO → always route to `PyRuntime.Format`.

2. **Does the Python output exactly match .NET output for all inputs?**
   - `e`/`E` → NO (exponent digit count differs) → route to `PyRuntime.Format`
   - `g`/`G` → mostly, but trailing zero behaviour differs → consider routing to runtime
   - If any deviation → route to `PyRuntime.Format`

3. **Are any flags present (`+`, `#`, `_`, fill/align)?**
   - If YES → route to `PyRuntime.Format` unconditionally

4. **Is the format spec dynamically computed (nested f-string expression)?**
   - If YES → route to `PyRuntime.Format` unconditionally

5. **Only if all above are NO** → emit `string.Format("{0:csSpec}", value)`

---

## Summary of Changes Required

| Location | Change |
|---|---|
| `EmitFString` | Add three-way dispatch: empty spec / .NET-native / Python-semantic |
| `IsNetNativeSpec` | New classifier — whitelist `d`, `f`, `e`, `g`, `n`, `x`, `X` with no flags |
| `HasFillAlign` | New helper — detect `<`, `>`, `^`, `=` in spec string |
| `TranslateSimpleSpec` | Translate safe Python specs to .NET composite format codes |
| `ParseFP` | Extract precision from Python `width.precision` body |
| `PyRuntime.Format` | Ensure `b`, `o`, `%`, `#`, `+`, fill/align, `_`, nested specs all handled |
| `FormatSci` | Normalize exponent to 2-digit lowercase (`e+02` not `E+003`) |
| `FormatPercent` | Multiply ×100, append `%` with no space |

The core fix is a single gate condition in `EmitFString`. Python-semantic specs were incorrectly falling through to `string.Format`. Routing them to `PyRuntime.Format` (which already implements Python semantics) is sufficient to pass the `08b` assertion and all similar cases.

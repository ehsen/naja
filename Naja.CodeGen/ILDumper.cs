using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace Naja.CodeGen;

/// <summary>
/// Utility to dump IL from compiled methods for debugging.
///
/// The opcode table is built from the canonical System.Reflection.Emit.OpCodes
/// fields (never hand-typed), and operand bytes are consumed per
/// OpCode.OperandType so the instruction stream stays in sync. Metadata tokens
/// (methods/fields/types/strings) are resolved through the owning module when
/// possible.
/// </summary>
public static class ILDumper
{
    // value (ushort) → OpCode. Single-byte opcodes: 0x00–0xFE.
    // Two-byte opcodes: 0xFE00 | second byte.
    private static readonly Dictionary<int, OpCode> OpCodeTable = BuildOpCodeTable();

    private static Dictionary<int, OpCode> BuildOpCodeTable()
    {
        var table = new Dictionary<int, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode op)
                table[op.Value] = op;
        }
        return table;
    }

    /// <summary>
    /// Dump the IL for a specific method as a string with offsets, opcode
    /// names and resolved operands.
    /// </summary>
    public static string DumpMethodIL(MethodInfo method)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Method: {method.DeclaringType?.FullName}.{method.Name}");
        sb.AppendLine($"Signature: {method.ReturnType.Name} {method.Name}({string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
        sb.AppendLine();

        var body = method.GetMethodBody();
        if (body == null)
        {
            sb.AppendLine("(No method body)");
            return sb.ToString();
        }

        var il = body.GetILAsByteArray();
        if (il == null || il.Length == 0)
        {
            sb.AppendLine("(Empty method body)");
            return sb.ToString();
        }

        sb.AppendLine($"IL ({il.Length} bytes):");
        sb.AppendLine();

        var module = method.Module;
        int offset = 0;
        while (offset < il.Length)
        {
            int instrStart = offset;
            byte first = il[offset++];

            OpCode op;
            if (first == 0xFE && offset < il.Length)
            {
                byte second = il[offset++];
                op = OpCodeTable.TryGetValue((0xFE00 | second), out var two)
                    ? two : new OpCode();
                op = op.Equals(default) ? OpCodes.Nop : op; // unknown 0xFE — nop placeholder
            }
            else
            {
                op = OpCodeTable.TryGetValue(first, out var one)
                    ? one : OpCodes.Nop;
            }

            string operand = ReadOperand(op, il, ref offset, module);
            sb.AppendLine($"  IL_{instrStart:X4}: {op.Name,-14} {operand}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Consume and format the operand of <paramref name="op"/> per its
    /// OperandType, advancing <paramref name="offset"/> past it.
    /// </summary>
    private static string ReadOperand(OpCode op, byte[] il, ref int offset, Module module)
    {
        switch (op.OperandType)
        {
            case OperandType.InlineNone:
                return "";

            case OperandType.ShortInlineBrTarget:
            {
                if (offset >= il.Length) return "<truncated>";
                int delta = (sbyte)il[offset++];
                return $"IL_{offset + delta:X4}";
            }

            case OperandType.InlineBrTarget:
            {
                if (offset + 4 > il.Length) return "<truncated>";
                int delta = BitConverter.ToInt32(il, offset);
                offset += 4;
                return $"IL_{offset + delta:X4}";
            }

            case OperandType.ShortInlineI:
            {
                if (offset >= il.Length) return "<truncated>";
                return il[offset++].ToString();
            }

            case OperandType.InlineI:
            {
                if (offset + 4 > il.Length) return "<truncated>";
                var v = BitConverter.ToInt32(il, offset);
                offset += 4;
                return v.ToString();
            }

            case OperandType.InlineI8:
            {
                if (offset + 8 > il.Length) return "<truncated>";
                var v = BitConverter.ToInt64(il, offset);
                offset += 8;
                return v.ToString();
            }

            case OperandType.ShortInlineR:
            {
                if (offset + 4 > il.Length) return "<truncated>";
                var v = BitConverter.ToSingle(il, offset);
                offset += 4;
                return v.ToString("R");
            }

            case OperandType.InlineR:
            {
                if (offset + 8 > il.Length) return "<truncated>";
                var v = BitConverter.ToDouble(il, offset);
                offset += 8;
                return v.ToString("R");
            }

            case OperandType.ShortInlineVar:
            {
                if (offset >= il.Length) return "<truncated>";
                return $"V{il[offset++]}";
            }

            case OperandType.InlineVar:
            {
                if (offset + 2 > il.Length) return "<truncated>";
                var v = BitConverter.ToUInt16(il, offset);
                offset += 2;
                return $"V{v}";
            }

            case OperandType.InlineString:
            {
                if (offset + 4 > il.Length) return "<truncated>";
                var token = BitConverter.ToInt32(il, offset);
                offset += 4;
                try
                {
                    var s = module.ResolveString(token);
                    return $"\"{s?.Replace("\n", "\\n")}\"";
                }
                catch { return $"tok:0x{token:X8}"; }
            }

            case OperandType.InlineMethod:
            {
                if (offset + 4 > il.Length) return "<truncated>";
                var token = BitConverter.ToInt32(il, offset);
                offset += 4;
                try
                {
                    var m = module.ResolveMethod(token);
                    return $"{m?.DeclaringType?.Name}.{m?.Name}";
                }
                catch { return $"tok:0x{token:X8}"; }
            }

            case OperandType.InlineField:
            {
                if (offset + 4 > il.Length) return "<truncated>";
                var token = BitConverter.ToInt32(il, offset);
                offset += 4;
                try
                {
                    var f = module.ResolveField(token);
                    return $"{f?.DeclaringType?.Name}::{f?.Name}";
                }
                catch { return $"tok:0x{token:X8}"; }
            }

            case OperandType.InlineType:
            case OperandType.InlineTok:
            case OperandType.InlineSig:
            {
                if (offset + 4 > il.Length) return "<truncated>";
                var token = BitConverter.ToInt32(il, offset);
                offset += 4;
                try
                {
                    var t = module.ResolveType(token);
                    return t.FullName ?? t.Name;
                }
                catch { return $"tok:0x{token:X8}"; }
            }

            case OperandType.InlineSwitch:
            {
                if (offset + 4 > il.Length) return "<truncated>";
                int n = BitConverter.ToInt32(il, offset);
                offset += 4;
                var targets = new string[n];
                for (int i = 0; i < n; i++)
                {
                    if (offset + 4 > il.Length) { targets[i] = "<truncated>"; break; }
                    int delta = BitConverter.ToInt32(il, offset);
                    offset += 4;
                    targets[i] = $"IL_{offset + delta:X4}";
                }
                return $"({string.Join(", ", targets)})";
            }

            default:
                // Unknown operand type — consume nothing rather than desync.
                return "";
        }
    }
}
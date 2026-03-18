using System;
using System.Reflection;
using System.Text;

namespace Naja.CodeGen;

/// <summary>
/// Utility to dump IL from compiled methods for debugging.
/// </summary>
public static class ILDumper
{
    /// <summary>
    /// Dump the IL for a specific method as a string with line numbers and stack analysis.
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

        // Simple IL disassembly — just show hex + opcodes
        int offset = 0;
        while (offset < il.Length)
        {
            byte opByte = il[offset];
            string opName = GetOpCodeName(opByte);
            string operandStr = "";

            // Try to interpret operands for common opcodes
            if (opByte == 0xFE && offset + 1 < il.Length)
            {
                // Two-byte opcode
                byte opByte2 = il[offset + 1];
                opName = GetOpCodeName(opByte, opByte2);
                offset += 2;

                operandStr = GetOperandString(opName, il, ref offset);
            }
            else if (opByte == 0x1F || opByte == 0x20 || opByte == 0x21 || opByte == 0x23 || opByte == 0x25)
            {
                // Single-byte opcodes with 4-byte operands (ldc.i4, ldarg, etc.)
                offset++;
                if (offset + 4 <= il.Length)
                {
                    int operand = BitConverter.ToInt32(il, offset);
                    operandStr = operand.ToString();
                    offset += 4;
                }
            }
            else if (opByte == 0x0E || opByte == 0x0F || opByte == 0x10)
            {
                // ldloc, stloc, ldarg with byte operand
                offset++;
                if (offset < il.Length)
                {
                    byte operand = il[offset];
                    operandStr = operand.ToString();
                    offset++;
                }
            }
            else
            {
                offset++;
            }

            sb.AppendLine($"  {offset - (operandStr.Length > 0 ? 4 : 1):X4}: {opName,-15} {operandStr}");
        }

        return sb.ToString();
    }

    private static string GetOpCodeName(byte opByte)
    {
        return opByte switch
        {
            0x00 => "nop",
            0x01 => "ldc.i4.0",
            0x02 => "ldc.i4.1",
            0x03 => "ldc.i4.2",
            0x04 => "ldc.i4.3",
            0x05 => "ldc.i4.4",
            0x06 => "ldc.i4.5",
            0x07 => "ldc.i4.6",
            0x08 => "ldc.i4.7",
            0x09 => "ldc.i4.8",
            0x0A => "ldc.i4.s",
            0x0B => "ldc.i4",
            0x0C => "ldc.i8",
            0x0D => "ldc.r4",
            0x0E => "ldc.r8",
            0x0F => "ldarg.0",
            0x10 => "ldarg.1",
            0x11 => "ldarg.2",
            0x12 => "ldarg.3",
            0x13 => "ldarg",
            0x14 => "ldarg.s",
            0x15 => "ldloc.0",
            0x16 => "ldloc.1",
            0x17 => "ldloc.2",
            0x18 => "ldloc.3",
            0x19 => "ldloc",
            0x1A => "ldloc.s",
            0x1B => "stloc.0",
            0x1C => "stloc.1",
            0x1D => "stloc.2",
            0x1E => "stloc.3",
            0x1F => "stloc",
            0x20 => "stloc.s",
            0x21 => "ldnull",
            0x22 => "ldc.i4.m1",
            0x23 => "ldc.i4.s",
            0x25 => "dup",
            0x26 => "pop",
            0x27 => "jmp",
            0x28 => "call",
            0x29 => "calli",
            0x2A => "ret",
            0x2B => "brfalse",
            0x2C => "brtrue",
            0x2D => "beq",
            0x2E => "bge",
            0x2F => "bgt",
            0x30 => "ble",
            0x31 => "blt",
            0x32 => "bne.un",
            0x33 => "bge.un",
            0x34 => "bgt.un",
            0x35 => "ble.un",
            0x36 => "blt.un",
            0x37 => "switch",
            0x38 => "ldind.i1",
            0x39 => "ldind.u1",
            0x3A => "ldind.i2",
            0x3B => "ldind.u2",
            0x3C => "ldind.i4",
            0x3D => "ldind.u4",
            0x3E => "ldind.i8",
            0x3F => "ldind.i",
            0x40 => "ldind.r4",
            0x41 => "ldind.r8",
            0x42 => "ldind.ref",
            0x43 => "stind.ref",
            0x44 => "stind.i1",
            0x45 => "stind.i2",
            0x46 => "stind.i4",
            0x47 => "stind.i8",
            0x48 => "stind.r4",
            0x49 => "stind.r8",
            0x4A => "add",
            0x4B => "sub",
            0x4C => "mul",
            0x4D => "div",
            0x4E => "div.un",
            0x4F => "rem",
            0x50 => "rem.un",
            0x51 => "and",
            0x52 => "or",
            0x53 => "xor",
            0x54 => "shl",
            0x55 => "shr",
            0x56 => "shr.un",
            0x57 => "neg",
            0x58 => "not",
            0x59 => "conv.i1",
            0x5A => "conv.i2",
            0x5B => "conv.i4",
            0x5C => "conv.i8",
            0x5D => "conv.r4",
            0x5E => "conv.r8",
            0x5F => "conv.u4",
            0x60 => "conv.u8",
            0x61 => "callvirt",
            0x62 => "cpobj",
            0x63 => "ldobj",
            0x64 => "ldstr",
            0x65 => "newobj",
            0x66 => "castclass",
            0x67 => "isinst",
            0x68 => "conv.r.un",
            0x69 => "unbox",
            0x6A => "throw",
            0x6B => "ldfld",
            0x6C => "ldflda",
            0x6D => "stfld",
            0x6E => "ldsfld",
            0x6F => "ldsflda",
            0x70 => "stsfld",
            0x71 => "stobj",
            0x72 => "conv.ovf.i1.un",
            0x73 => "conv.ovf.i2.un",
            0x74 => "conv.ovf.i4.un",
            0x75 => "conv.ovf.i8.un",
            0x76 => "conv.ovf.u1.un",
            0x77 => "conv.ovf.u2.un",
            0x78 => "conv.ovf.u4.un",
            0x79 => "conv.ovf.u8.un",
            0x7A => "conv.ovf.i.un",
            0x7B => "conv.ovf.u.un",
            0x7C => "box",
            0x7D => "newarr",
            0x7E => "ldlen",
            0x7F => "ldelema",
            0x80 => "ldelem.i1",
            0x81 => "ldelem.u1",
            0x82 => "ldelem.i2",
            0x83 => "ldelem.u2",
            0x84 => "ldelem.i4",
            0x85 => "ldelem.u4",
            0x86 => "ldelem.i8",
            0x87 => "ldelem.i",
            0x88 => "ldelem.r4",
            0x89 => "ldelem.r8",
            0x8A => "ldelem.ref",
            0x8B => "stelem.i",
            0x8C => "stelem.i1",
            0x8D => "stelem.i2",
            0x8E => "stelem.i4",
            0x8F => "stelem.i8",
            0x90 => "stelem.r4",
            0x91 => "stelem.r8",
            0x92 => "stelem.ref",
            0x93 => "ldelem",
            0x94 => "stelem",
            0x95 => "unbox.any",
            0x96 => "conv.ovf.i1",
            0x97 => "conv.ovf.u1",
            0x98 => "conv.ovf.i2",
            0x99 => "conv.ovf.u2",
            0x9A => "conv.ovf.i4",
            0x9B => "conv.ovf.u4",
            0x9C => "conv.ovf.i8",
            0x9D => "conv.ovf.u8",
            0x9E => "conv.ovf.i",
            0x9F => "conv.ovf.u",
            0xA0 => "brfalse.s",
            0xA1 => "brtrue.s",
            0xA2 => "beq.s",
            0xA3 => "bge.s",
            0xA4 => "bgt.s",
            0xA5 => "ble.s",
            0xA6 => "blt.s",
            0xA7 => "bne.un.s",
            0xA8 => "bge.un.s",
            0xA9 => "bgt.un.s",
            0xAA => "ble.un.s",
            0xAB => "blt.un.s",
            0xAC => "leave",
            0xAD => "leave.s",
            0xAE => "stind.i",
            0xAF => "conv.u",
            0xB0 => "arglist",
            0xB1 => "ceq",
            0xB2 => "cgt",
            0xB3 => "cgt.un",
            0xB4 => "clt",
            0xB5 => "clt.un",
            0xB6 => "ldftn",
            0xB7 => "ldvirtftn",
            0xB8 => "ldarg",
            0xB9 => "ldarga",
            0xBA => "starg",
            0xBB => "ldloc",
            0xBC => "ldloca",
            0xBD => "stloc",
            0xBE => "localloc",
            0xBF => "endfilter",
            0xC0 => "unaligned",
            0xC1 => "volatile",
            0xC2 => "tail",
            0xC3 => "initobj",
            0xC4 => "constrained",
            0xC5 => "cpblk",
            0xC6 => "initblk",
            0xC7 => "no",
            0xC8 => "rethrow",
            0xC9 => "sizeof",
            0xCA => "refanytype",
            0xCB => "readonly",
            _ => $"0x{opByte:X2}"
        };
    }

    private static string GetOpCodeName(byte opByte1, byte opByte2)
    {
        return opByte2 switch
        {
            0x00 => "arglist",
            0x01 => "ceq",
            0x02 => "cgt",
            0x03 => "cgt.un",
            0x04 => "clt",
            0x05 => "clt.un",
            0x06 => "ldftn",
            0x07 => "ldvirtftn",
            0x09 => "ldarg",
            0x0A => "ldarga",
            0x0B => "starg",
            0x0C => "ldloc",
            0x0D => "ldloca",
            0x0E => "stloc",
            0x0F => "localloc",
            0x11 => "endfilter",
            0x12 => "unaligned",
            0x13 => "volatile",
            0x14 => "tail",
            0x15 => "initobj",
            0x16 => "constrained",
            0x17 => "cpblk",
            0x18 => "initblk",
            0x19 => "no",
            0x1A => "rethrow",
            0x1C => "sizeof",
            0x1D => "refanytype",
            0x1E => "readonly",
            _ => $"0xFE{opByte2:X2}"
        };
    }

    private static string GetOperandString(string opName, byte[] il, ref int offset)
    {
        // For now, just return empty — we'd need more sophisticated IL parsing
        return "";
    }
}

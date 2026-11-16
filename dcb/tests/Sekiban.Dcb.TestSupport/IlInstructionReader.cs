using System.Reflection;
using System.Reflection.Emit;
namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     Decodes a method body into real instructions, so a scan matches opcodes only at instruction boundaries. A
///     byte-by-byte scan also "finds" opcode values inside operands (for example the hit index a coverage tool inserts
///     before a getter's <c>ldfld</c>), which makes the result depend on unrelated code elsewhere in the assembly.
/// </summary>
public static class IlInstructionReader
{
    private static readonly OpCode[] OneByte = new OpCode[0x100];
    private static readonly OpCode[] TwoByte = new OpCode[0x100];

    static IlInstructionReader()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode op)
            {
                continue;
            }

            var value = (ushort)op.Value;
            if (op.Size == 1)
            {
                OneByte[value] = op;
            }
            else
            {
                TwoByte[value & 0xFF] = op;
            }
        }
    }

    /// <summary>Yields each instruction's opcode and, for a 4-byte inline operand, that operand (token or offset).</summary>
    public static IEnumerable<(OpCode OpCode, int Operand)> Read(byte[] il)
    {
        var i = 0;
        while (i < il.Length)
        {
            var op = il[i] == 0xFE && i + 1 < il.Length ? TwoByte[il[i + 1]] : OneByte[il[i]];
            i += op.Size;
            var operandSize = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4
            };
            var operand = operandSize == 4 && i + 4 <= il.Length ? BitConverter.ToInt32(il, i) : 0;
            yield return (op, operand);
            i += operandSize;
        }
    }
}

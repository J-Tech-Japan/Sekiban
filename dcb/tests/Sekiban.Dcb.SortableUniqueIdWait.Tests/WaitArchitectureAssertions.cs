using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Sekiban.Dcb.Common;
using Xunit;

namespace Sekiban.Dcb.SortableUniqueIdWait.Tests;

internal sealed record WaitArchitectureRoute(
    string Name,
    Type ExecutorType,
    Type QueryContractDefinition,
    string WaitMethodName,
    SortableUniqueIdWaitSurface Surface);

/// <summary>
///     Structural proof for the production wait-route inventory. Each route must call its named wait boundary exactly
///     once, that boundary must call the one shared policy exactly once, and neither method may contain a second wait
///     loop or direct delay/clock implementation. This is deliberately IL-based so a private field-only assertion cannot
///     remain green after a route is bypassed or its wait is copied back into an entrypoint.
/// </summary>
internal static class WaitArchitectureAssertions
{
    private static readonly MethodInfo SharedWaitMethod =
        typeof(SortableUniqueIdWaitPolicy).GetMethod(
            "WaitAsync",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new InvalidOperationException("The shared wait policy must expose its internal WaitAsync method.");

    private static readonly OpCode[] OneByteOpCodes = BuildOpCodeMap(singleByte: true);
    private static readonly OpCode[] TwoByteOpCodes = BuildOpCodeMap(singleByte: false);

    internal static void AssertAll(params WaitArchitectureRoute[] routes)
    {
        Assert.NotEmpty(routes);
        Assert.Equal(
            routes.Length,
            routes.Select(route => route.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(routes, AssertRoute);
    }

    private static void AssertRoute(WaitArchitectureRoute route)
    {
        var entrypoint = FindQueryEntrypoint(route);
        var waitMethod = FindWaitMethod(route);
        var entrypointImplementation = GetAsyncImplementation(entrypoint);
        var waitImplementation = GetAsyncImplementation(waitMethod);

        var entrypointInstructions = ReadInstructions(entrypointImplementation);
        var entrypointCalls = GetCallSites(entrypointImplementation, entrypointInstructions);
        var waitCalls = entrypointCalls
            .Where(call => SameMethod(call.Target, waitMethod))
            .ToArray();

        Assert.Single(
            waitCalls);
        Assert.DoesNotContain(
            entrypointCalls,
            call => SameMethod(call.Target, SharedWaitMethod));
        Assert.True(
            HasEnumConstantNearCall(entrypointInstructions, waitCalls[0].Instruction.Offset, route.Surface),
            $"Route '{route.Name}' must pass its explicitly inventoried wait surface to {waitMethod.Name}.");
        AssertNoDuplicateWaitImplementation(route.Name, entrypointImplementation, entrypointInstructions);

        var waitInstructions = ReadInstructions(waitImplementation);
        var policyCalls = GetCallSites(waitImplementation, waitInstructions)
            .Where(call => SameMethod(call.Target, SharedWaitMethod))
            .ToArray();

        Assert.Single(
            policyCalls);
        AssertNoDuplicateWaitImplementation(route.Name + " wait boundary", waitImplementation, waitInstructions);
    }

    private static MethodInfo FindQueryEntrypoint(WaitArchitectureRoute route)
    {
        var candidates = route.ExecutorType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method =>
                method.Name == "QueryAsync" &&
                method.IsGenericMethodDefinition &&
                method.GetParameters() is [{ ParameterType: { IsGenericType: true } }])
            .Where(method => method.GetParameters()[0].ParameterType.GetGenericTypeDefinition() ==
                             route.QueryContractDefinition)
            .ToArray();

        Assert.Single(
            candidates);
        return candidates[0];
    }

    private static MethodInfo FindWaitMethod(WaitArchitectureRoute route)
    {
        var candidates = route.ExecutorType
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(method => method.Name == route.WaitMethodName)
            .ToArray();

        Assert.Single(
            candidates);
        return candidates[0];
    }

    private static MethodInfo GetAsyncImplementation(MethodInfo method)
    {
        var stateMachineAttribute = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (stateMachineAttribute is null)
        {
            return method;
        }

        var moveNext = stateMachineAttribute.StateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(moveNext);
        return moveNext!;
    }

    private static void AssertNoDuplicateWaitImplementation(
        string routeName,
        MethodInfo method,
        IReadOnlyList<Instruction> instructions)
    {
        var backwards = instructions
            .Where(instruction => IsBranch(instruction.OpCode))
            .SelectMany(instruction => GetBranchTargets(instruction).Select(target => (instruction, target)))
            .Where(pair => pair.target <= pair.instruction.Offset)
            .ToArray();
        Assert.Empty(
            backwards);

        var forbiddenCalls = GetCallSites(method, instructions)
            .Where(call => IsDirectWaitImplementation(call.Target))
            .Select(call => call.Target?.ToString() ?? "unresolved call")
            .ToArray();
        Assert.Empty(
            forbiddenCalls);
    }

    private static bool IsDirectWaitImplementation(MethodBase? target)
    {
        if (target is null)
        {
            return false;
        }

        if (target.DeclaringType == typeof(Task) && target.Name == nameof(Task.Delay))
        {
            return true;
        }

        if (target.DeclaringType == typeof(Thread) && target.Name.StartsWith("Sleep", StringComparison.Ordinal))
        {
            return true;
        }

        if (target.DeclaringType == typeof(Stopwatch) && target.Name.StartsWith("Start", StringComparison.Ordinal))
        {
            return true;
        }

        if (target.DeclaringType == typeof(TimeProvider) &&
            (target.Name == nameof(TimeProvider.GetTimestamp) || target.Name == nameof(TimeProvider.GetElapsedTime)))
        {
            return true;
        }

        return target.DeclaringType == typeof(SortableUniqueIdWaitHelper);
    }

    private static bool HasEnumConstantNearCall(
        IReadOnlyList<Instruction> instructions,
        int callOffset,
        SortableUniqueIdWaitSurface expectedSurface)
    {
        var callIndex = instructions.Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Offset == callOffset)
            .Select(pair => pair.index)
            .Single();
        var firstIndex = Math.Max(0, callIndex - 10);
        return instructions
            .Skip(firstIndex)
            .Take(callIndex - firstIndex)
            .Any(instruction => TryGetInt32Constant(instruction, out var value) &&
                                value == (int)expectedSurface);
    }

    private static IReadOnlyList<CallSite> GetCallSites(
        MethodInfo owner,
        IReadOnlyList<Instruction> instructions) =>
        instructions
            .Where(instruction => instruction.OpCode == OpCodes.Call ||
                                  instruction.OpCode == OpCodes.Callvirt)
            .Select(instruction => new CallSite(instruction, ResolveCall(owner, instruction)))
            .ToArray();

    private static MethodBase? ResolveCall(MethodInfo owner, Instruction instruction)
    {
        if (instruction.Operand is not int token)
        {
            return null;
        }

        try
        {
            var typeArguments = owner.DeclaringType?.IsGenericType == true
                ? owner.DeclaringType.GetGenericArguments()
                : null;
            var methodArguments = owner.IsGenericMethod ? owner.GetGenericArguments() : null;
            return owner.Module.ResolveMethod(token, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static bool SameMethod(MethodBase? actual, MethodBase expected) =>
        actual is not null &&
        actual.Module == expected.Module &&
        actual.MetadataToken == expected.MetadataToken;

    private static IReadOnlyList<Instruction> ReadInstructions(MethodInfo method)
    {
        var body = method.GetMethodBody();
        Assert.NotNull(body);
        var bytes = body!.GetILAsByteArray();
        Assert.NotNull(bytes);

        var instructions = new List<Instruction>();
        var offset = 0;
        while (offset < bytes!.Length)
        {
            var instructionOffset = offset;
            var opcodeByte = bytes[offset++];
            var opcode = opcodeByte == 0xFE
                ? TwoByteOpCodes[bytes[offset++]]
                : OneByteOpCodes[opcodeByte];
            var operand = ReadOperand(opcode, bytes, ref offset);
            instructions.Add(new Instruction(instructionOffset, opcode, operand));
        }

        return instructions;
    }

    private static object? ReadOperand(OpCode opcode, byte[] bytes, ref int offset) => opcode.OperandType switch
    {
        OperandType.InlineNone => null,
        OperandType.ShortInlineI => (sbyte)bytes[offset++],
        OperandType.InlineI => ReadInt32(bytes, ref offset),
        OperandType.InlineI8 => ReadInt64(bytes, ref offset),
        OperandType.ShortInlineR => ReadSingle(bytes, ref offset),
        OperandType.InlineR => ReadDouble(bytes, ref offset),
        OperandType.ShortInlineBrTarget => offset + (sbyte)bytes[offset++],
        OperandType.InlineBrTarget => ReadBranchTarget(bytes, ref offset),
        OperandType.InlineSwitch => ReadSwitchTargets(bytes, ref offset),
        OperandType.InlineString or
        OperandType.InlineSig or
        OperandType.InlineField or
        OperandType.InlineMethod or
        OperandType.InlineType or
        OperandType.InlineTok => ReadInt32(bytes, ref offset),
        OperandType.ShortInlineVar => bytes[offset++],
        OperandType.InlineVar => ReadUInt16(bytes, ref offset),
        _ => throw new NotSupportedException($"Unsupported IL operand type {opcode.OperandType}.")
    };

    private static int ReadBranchTarget(byte[] bytes, ref int offset)
    {
        var delta = ReadInt32(bytes, ref offset);
        return offset + delta;
    }

    private static int[] ReadSwitchTargets(byte[] bytes, ref int offset)
    {
        var count = ReadInt32(bytes, ref offset);
        var baseOffset = offset + (count * sizeof(int));
        var targets = new int[count];
        for (var index = 0; index < count; index++)
        {
            targets[index] = baseOffset + ReadInt32(bytes, ref offset);
        }

        return targets;
    }

    private static int ReadInt32(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToInt32(bytes, offset);
        offset += sizeof(int);
        return value;
    }

    private static long ReadInt64(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToInt64(bytes, offset);
        offset += sizeof(long);
        return value;
    }

    private static float ReadSingle(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToSingle(bytes, offset);
        offset += sizeof(float);
        return value;
    }

    private static double ReadDouble(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToDouble(bytes, offset);
        offset += sizeof(double);
        return value;
    }

    private static ushort ReadUInt16(byte[] bytes, ref int offset)
    {
        var value = BitConverter.ToUInt16(bytes, offset);
        offset += sizeof(ushort);
        return value;
    }

    private static bool IsBranch(OpCode opcode) =>
        opcode.OperandType == OperandType.ShortInlineBrTarget ||
        opcode.OperandType == OperandType.InlineBrTarget ||
        opcode.OperandType == OperandType.InlineSwitch;

    private static IEnumerable<int> GetBranchTargets(Instruction instruction)
    {
        if (instruction.Operand is int singleTarget && instruction.OpCode.OperandType != OperandType.InlineSwitch)
        {
            yield return singleTarget;
        }
        else if (instruction.Operand is int[] targets)
        {
            foreach (var switchTarget in targets)
            {
                yield return switchTarget;
            }
        }
    }

    private static bool TryGetInt32Constant(Instruction instruction, out int value)
    {
        value = instruction.OpCode.Value switch
        {
            short valueCode when valueCode == OpCodes.Ldc_I4_M1.Value => -1,
            short valueCode when valueCode == OpCodes.Ldc_I4_0.Value => 0,
            short valueCode when valueCode == OpCodes.Ldc_I4_1.Value => 1,
            short valueCode when valueCode == OpCodes.Ldc_I4_2.Value => 2,
            short valueCode when valueCode == OpCodes.Ldc_I4_3.Value => 3,
            short valueCode when valueCode == OpCodes.Ldc_I4_4.Value => 4,
            short valueCode when valueCode == OpCodes.Ldc_I4_5.Value => 5,
            short valueCode when valueCode == OpCodes.Ldc_I4_6.Value => 6,
            short valueCode when valueCode == OpCodes.Ldc_I4_7.Value => 7,
            short valueCode when valueCode == OpCodes.Ldc_I4_8.Value => 8,
            _ => int.MinValue
        };

        if (value != int.MinValue)
        {
            return true;
        }

        if (instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is int inlineValue)
        {
            value = inlineValue;
            return true;
        }

        if (instruction.OpCode == OpCodes.Ldc_I4_S && instruction.Operand is sbyte shortValue)
        {
            value = shortValue;
            return true;
        }

        value = 0;
        return false;
    }

    private static OpCode[] BuildOpCodeMap(bool singleByte)
    {
        var opcodes = new OpCode[0x100];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opcode)
            {
                continue;
            }

            var value = (ushort)opcode.Value;
            if (singleByte && value < 0x100)
            {
                opcodes[value] = opcode;
            }
            else if (!singleByte && (value & 0xFF00) == 0xFE00)
            {
                opcodes[value & 0xFF] = opcode;
            }
        }

        return opcodes;
    }

    private sealed record Instruction(int Offset, OpCode OpCode, object? Operand);

    private sealed record CallSite(Instruction Instruction, MethodBase? Target);
}

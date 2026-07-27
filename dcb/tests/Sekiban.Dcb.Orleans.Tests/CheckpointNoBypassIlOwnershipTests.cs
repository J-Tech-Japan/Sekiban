using System.Reflection;
using System.Reflection.Emit;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Storage;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G20 structural ownership proof by IL analysis over the PRODUCTION assembly inventory (not a source regex over
///     two named files). Every direct legacy MUTATION of the checkpoint store
///     (<see cref="IMultiProjectionStateStore" />.<c>UpsertAsync</c> / <c>UpsertFromStreamAsync</c> / <c>DeleteAsync</c> /
///     <c>DeleteAllAsync</c>) is a <c>call</c>/<c>callvirt</c> in some method body. This walks the IL of EVERY method of
///     EVERY type (including nested async state machines and compiler-generated closures) in the product assemblies and
///     proves the ONLY types that emit such a call are the two owning coordinators — a raw store field, a delegate
///     (<c>ldftn</c> is also a method reference this scan sees), or a helper anywhere else in the inventory that bypassed
///     the capability-aware path would trip this at build time, not by luck of a runtime path being hit. (A purely
///     reflective <c>MethodInfo.Invoke</c> bypass is not statically decidable in any language; that residue is covered by
///     the fail-closed CAS contract and the no-silent-success provider tests.)
/// </summary>
public class CheckpointNoBypassIlOwnershipTests
{
    // The mutation surface of the checkpoint store. Reads (GetLatest*, ListAll, OpenStateDataReadStream) are irrelevant.
    private static readonly HashSet<string> LegacyMutations = new(StringComparer.Ordinal)
    {
        nameof(IMultiProjectionStateStore.UpsertAsync),
        nameof(IMultiProjectionStateStore.UpsertFromStreamAsync),
        nameof(IMultiProjectionStateStore.DeleteAsync),
        nameof(IMultiProjectionStateStore.DeleteAllAsync)
    };

    // The two owning types permitted to call a legacy mutation, each strictly as a documented LEGACY-FALLBACK branch
    // (that the companion CheckpointNoBypassStructuralTests proves is marked). Any OTHER caller is a bypass.
    private static readonly HashSet<string> AllowedOwners = new(StringComparer.Ordinal)
    {
        "Sekiban.Dcb.Orleans.Grains.MultiProjectionGrain",
        "Sekiban.Dcb.MultiProjections.MultiProjectionStateBuilder"
    };

    private static Assembly[] ProductAssemblies() => new[]
    {
        typeof(MultiProjectionStateBuilder).Assembly,                        // Sekiban.Dcb.Core
        typeof(Sekiban.Dcb.Orleans.Grains.IMultiProjectionGrain).Assembly    // Sekiban.Dcb.Orleans.Core
    };

    private sealed record CallSite(string Owner, string Method, string TargetMethod);

    private static List<CallSite> ScanLegacyMutationCallSites()
    {
        var sites = new List<CallSite>();
        foreach (var asm in ProductAssemblies())
        {
            foreach (var type in asm.GetTypes())
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly;
                var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
                foreach (var method in methods)
                {
                    MethodBody? body;
                    try { body = method.GetMethodBody(); }
                    catch { continue; }
                    if (body is null) { continue; }
                    byte[] il;
                    try { il = body.GetILAsByteArray() ?? Array.Empty<byte>(); }
                    catch { continue; }
                    ScanMethod(asm.Modules.First(), type, method, il, sites);
                }
            }
        }
        return sites;
    }

    private static void ScanMethod(Module module, Type type, MethodBase method, byte[] il, List<CallSite> sites)
    {
        var typeArgs = SafeGenericArgs(type);
        var methodArgs = method is MethodInfo mi && mi.IsGenericMethodDefinition ? mi.GetGenericArguments() : null;
        var i = 0;
        while (i < il.Length)
        {
            var (opValue, operandType, next) = DecodeOpcode(il, i);
            if ((opValue == OpCodes.Call.Value || opValue == OpCodes.Callvirt.Value || opValue == OpCodes.Ldftn.Value
                    || opValue == OpCodes.Ldvirtftn.Value)
                && operandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, i + OpcodeLength(opValue));
                MethodBase? target = null;
                try { target = module.ResolveMethod(token, typeArgs, methodArgs); }
                catch { /* MethodSpec/edge tokens we cannot resolve are not our targets */ }
                if (target is not null && IsLegacyCheckpointMutation(target))
                {
                    sites.Add(new CallSite(OutermostName(type), Describe(method), target.Name));
                }
            }
            i = next;
        }
    }

    private static bool IsLegacyCheckpointMutation(MethodBase target)
    {
        if (!LegacyMutations.Contains(target.Name))
        {
            return false;
        }
        var declaring = target.DeclaringType;
        // The declaring type is either the interface itself (callvirt on IMultiProjectionStateStore) or a concrete store
        // implementing it (a direct call on a typed field). Both are legacy mutations of the checkpoint store.
        return declaring is not null && typeof(IMultiProjectionStateStore).IsAssignableFrom(declaring);
    }

    [Fact]
    public void OnlyTheTwoOwningCoordinators_EverEmitALegacyCheckpointMutation_AcrossTheWholeProductInventory()
    {
        var sites = ScanLegacyMutationCallSites();
        var offenders = sites.Where(s => !AllowedOwners.Contains(s.Owner)).ToList();
        Assert.True(offenders.Count == 0,
            "A legacy checkpoint mutation is emitted OUTSIDE the capability-aware coordinators — route it through the CAS "
            + "coordinator or move it into a marked non-capable fallback in an owning type:\n"
            + string.Join('\n', offenders.Select(o => $"  {o.Owner}::{o.Method} -> {o.TargetMethod}")));
    }

    [Fact]
    public void TheIlScan_IsNonVacuous_ItActuallyFindsTheOwners_LegacyFallbacks()
    {
        // If the scan found nothing, the walker/token resolution has rotted and the ownership test would pass vacuously.
        var sites = ScanLegacyMutationCallSites();
        Assert.True(sites.Count > 0, "the IL scan found no legacy checkpoint mutation at all — the walker has rotted");
        Assert.All(sites, s => Assert.Contains(s.Owner, AllowedOwners));
    }

    // ---- minimal IL decoder ----

    private static readonly Dictionary<short, OperandType> OperandTypes = BuildOperandTable();
    private static Dictionary<short, OperandType> BuildOperandTable()
    {
        var map = new Dictionary<short, OperandType>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode op)
            {
                map[op.Value] = op.OperandType;
            }
        }
        return map;
    }

    // Returns (opcode value, operand type, index of the next instruction).
    private static (short OpValue, OperandType Operand, int Next) DecodeOpcode(byte[] il, int i)
    {
        short opValue = il[i];
        if (il[i] == 0xFE && i + 1 < il.Length)
        {
            opValue = (short)(0xFE00 | il[i + 1]);
        }
        var operand = OperandTypes.TryGetValue(opValue, out var ot) ? ot : OperandType.InlineNone;
        var start = i + OpcodeLength(opValue);
        var next = start + OperandSize(operand, il, start);
        return (opValue, operand, next);
    }

    private static int OpcodeLength(short opValue) => (opValue & 0xFF00) == 0xFE00 ? 2 : 1;

    private static int OperandSize(OperandType operand, byte[] il, int operandStart) => operand switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
            or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
            or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, operandStart),
        _ => 4
    };

    private static Type[]? SafeGenericArgs(Type type)
    {
        try { return type.IsGenericType || type.IsGenericTypeDefinition ? type.GetGenericArguments() : null; }
        catch { return null; }
    }

    private static string OutermostName(Type type)
    {
        var t = type;
        while (t.DeclaringType is not null)
        {
            t = t.DeclaringType;
        }
        return t.FullName ?? t.Name;
    }

    private static string Describe(MethodBase method) => $"{method.DeclaringType?.Name}.{method.Name}";
}

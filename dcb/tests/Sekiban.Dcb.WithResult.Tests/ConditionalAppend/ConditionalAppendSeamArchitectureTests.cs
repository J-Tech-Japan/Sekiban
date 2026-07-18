using System.Reflection;
using System.Runtime.CompilerServices;
using Dcb.Domain;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     Structural guard for the SEK-G16 post-commit response-loss TEST seam (<c>AfterConditionalCommitHook</c>). It proves
///     the seam cannot silently become a public/production-reachable runtime mutation surface: it is a NON-public INSTANCE
///     member (never static/global, so parallel instances cannot leak state), absent from the public event-store
///     interfaces, and left null by normal construction (production composition never assigns it).
/// </summary>
public class ConditionalAppendSeamArchitectureTests
{
    private const string HookName = "AfterConditionalCommitHook";

    public static IEnumerable<object[]> SeamStoreTypes => new[]
    {
        new object[] { typeof(SqliteEventStore) },
        new object[] { typeof(CosmosDbEventStore) }
    };

    [Theory]
    [MemberData(nameof(SeamStoreTypes))]
    public void Hook_IsNonPublic_Instance_NotStatic(Type storeType)
    {
        var nonPublic = storeType.GetProperty(HookName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(nonPublic);                                                    // exists as an internal instance member
        Assert.Null(storeType.GetProperty(HookName, BindingFlags.Instance | BindingFlags.Public)); // not public
        Assert.Null(storeType.GetProperty(HookName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)); // never static/global
    }

    [Fact]
    public void Hook_IsAbsentFromPublicEventStoreInterfaces()
    {
        foreach (var contract in new[] { typeof(IEventStore), typeof(IConditionalEventStore), typeof(IHotEventStore) })
        {
            Assert.Null(contract.GetProperty(HookName));
        }
    }

    [Fact]
    public void Hook_StateIsPerInstance_NoStaticLeak()
    {
        var domain = DomainType.GetDomainTypes();
        var a = new SqliteEventStore(Path.Combine(Path.GetTempPath(), $"seam-a-{Guid.NewGuid():N}.db"), domain.EventTypes);
        var b = new SqliteEventStore(Path.Combine(Path.GetTempPath(), $"seam-b-{Guid.NewGuid():N}.db"), domain.EventTypes);

        Assert.Null(a.AfterConditionalCommitHook);   // default: unset
        Assert.Null(b.AfterConditionalCommitHook);
        a.AfterConditionalCommitHook = () => Task.CompletedTask;
        Assert.NotNull(a.AfterConditionalCommitHook);
        Assert.Null(b.AfterConditionalCommitHook);   // setting one instance never leaks to another
    }

    [Fact]
    public void ProductionComposition_LeavesHookUnset_Sqlite()
    {
        var domain = DomainType.GetDomainTypes();
        // A store built the way production builds it carries no hook.
        var store = new SqliteEventStore(
            Path.Combine(Path.GetTempPath(), $"seam-prod-{Guid.NewGuid():N}.db"),
            domain.EventTypes, null, null, new DefaultServiceIdProvider());
        Assert.Null(store.AfterConditionalCommitHook);
    }

    [Fact]
    public void ProductionComposition_LeavesHookUnset_Cosmos()
    {
        var domain = DomainType.GetDomainTypes();
        var options = new CosmosDbEventStoreOptions { EventsContainerName = "events", TagsContainerName = "tags" };
        var context = new CosmosDbContext(new InMemoryCosmosClient(), "db", null, options);
        var store = new CosmosDbEventStore(
            context, domain.EventTypes, new DefaultServiceIdProvider(), new DefaultCosmosContainerResolver(options));
        Assert.Null(store.AfterConditionalCommitHook);
    }

    [Fact]
    public void InternalsVisibleTo_Allowlist_IsExactlyTheIntendedAssemblies()
    {
        // The provider assemblies grant internals only to the intended test assembly; Core grants ONLY the four provider
        // assemblies (for the internal post-commit-response-loss signal) — never a test assembly.
        AssertIvtAllowlist(typeof(SqliteEventStore).Assembly, "Sekiban.Dcb.WithResult.Tests");
        AssertIvtAllowlist(typeof(CosmosDbEventStore).Assembly, "Sekiban.Dcb.WithResult.Tests");
        AssertIvtAllowlist(
            typeof(IConditionalEventStore).Assembly,
            "Sekiban.Dcb.Postgres", "Sekiban.Dcb.Sqlite", "Sekiban.Dcb.CosmosDb", "Sekiban.Dcb.DynamoDB");
    }

    [Theory]
    [MemberData(nameof(SeamStoreTypes))]
    public void Hook_IsNotConstructorInjected_NorAnOptionOrDiSurface(Type storeType)
    {
        // Not a constructor parameter (so DI/composition cannot supply it).
        foreach (var ctor in storeType.GetConstructors())
        {
            Assert.DoesNotContain(ctor.GetParameters(), p =>
                p.Name == "afterConditionalCommitHook" || p.ParameterType == typeof(Func<Task>));
        }

        // Not a public settable option/DI surface: no PUBLIC Func<Task> property anywhere on the store.
        Assert.DoesNotContain(
            storeType.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            p => p.PropertyType == typeof(Func<Task>));
    }

    [Fact]
    public void OptionsTypes_DoNotExposeTheHook()
    {
        Assert.DoesNotContain(
            typeof(CosmosDbEventStoreOptions).GetProperties(), p => p.PropertyType == typeof(Func<Task>));
        Assert.DoesNotContain(
            typeof(SqliteEventStoreOptions).GetProperties(), p => p.PropertyType == typeof(Func<Task>));
    }

    private const string SetterKind = "setter";
    private const string StoreKind = "stfld";
    private const string ReflectionKind = "reflection";

    // The provider assemblies (Postgres/SQLite/Cosmos) declare the AfterConditionalCommitHook seam; Core declares the
    // executor allocator probes and the coordinator's verification-budget override.
    private const string ProviderHookSeam = "AfterConditionalCommitHook";
    private static readonly string[] CoreSeams =
    {
        "ConditionalEventIdFactory", "ConditionalSortableIdFactory", "VerificationBudgetOverride"
    };

    [Fact]
    public void ProductionAssemblies_ContainNoSeamWrite_ByAnyPath()
    {
        // Structural IL guard covering EVERY write surface: setter calls, direct backing-field stores (stfld/stsfld) —
        // matched by the EXACT resolved backing FieldInfo identity, never a name — outside the narrowly-allowed default
        // init / own setter, and reflection assignment (PropertyInfo/FieldInfo.SetValue) ANYWHERE in the assembly
        // (production uses zero reflection SetValue). Target resolution fails closed if a seam backing field is unresolved.
        // SQLite/Cosmos declare the AfterConditionalCommitHook seam; Core declares the executor allocator probes + the
        // coordinator's verification-budget override. DynamoDB has NO settable test seam (its post-commit ambiguity is
        // wrapped in the store's catch, not a hook property), so it is not scanned for one.
        Assert.Empty(FindSeamWrites(typeof(SqliteEventStore).Assembly, ProviderHookSeam));
        Assert.Empty(FindSeamWrites(typeof(CosmosDbEventStore).Assembly, ProviderHookSeam));
        Assert.Empty(FindSeamWrites(typeof(IConditionalEventStore).Assembly, CoreSeams));
    }

    [Fact]
    public void SeamTargetResolution_FailsClosed_WhenABackingFieldCannotBeResolved()
    {
        // A configured seam that does not exist in the scanned assembly must THROW (fail closed), never silently pass by
        // falling back to name matching.
        Assert.Throws<InvalidOperationException>(
            () => FindSeamWrites(typeof(DynamoDbEventStore).Assembly, "AfterConditionalCommitHook"));
    }

    // ── Independent, mode-specific non-vacuous controls. Each proves one scanner branch (per-kind assertion, not an
    //    aggregate) and fails if that branch is removed. The control "seam" is a manual property whose INTERNAL backing
    //    field is directly writable by external fixtures, so the stfld branch can be exercised against the EXACT resolved
    //    target field identity. ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Control_SetterCall_IsDetected()
    {
        var hits = FindSeamWrites(typeof(SeamWriteControls).Assembly, SeamWriteControls.SeamName);
        Assert.Contains(hits, h => h.StartsWith(SetterKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamWriteControls.ExternalSetterCaller), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_DirectBackingFieldStore_ToExactTargetField_IsDetected()
    {
        var hits = FindSeamWrites(typeof(SeamWriteControls).Assembly, SeamWriteControls.SeamName);
        Assert.Contains(hits, h => h.StartsWith(StoreKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamWriteControls.ExternalFieldStorer), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_UnrelatedSameNamedField_IsNotReported()
    {
        // Negative control: a DIFFERENT type has a field with the IDENTICAL name to the control seam's backing field. Because
        // matching is by exact FieldInfo identity (not name), its external store must NOT be reported.
        var hits = FindSeamWrites(typeof(SeamWriteControls).Assembly, SeamWriteControls.SeamName);
        Assert.DoesNotContain(hits, h => h.Contains(nameof(SeamWriteControls.UnrelatedSameNamedField), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_ReflectionSetValueFromNonDeclaringType_IsDetected()
    {
        var hits = FindSeamWrites(typeof(SeamWriteControls).Assembly, SeamWriteControls.SeamName);
        Assert.Contains(hits, h => h.StartsWith(ReflectionKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamWriteControls.ExternalReflectionSetter), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Control fixtures for the seam-write scanner. The control seam is a MANUAL property with an internal backing
    ///     field so an external fixture can store it directly (the scanner matches by that field's exact identity).
    /// </summary>
    private static class SeamWriteControls
    {
        public const string SeamName = "ControlSeam";

        internal sealed class ControlSeamHolder
        {
            internal Func<Task>? ControlSeamBackingField;
            internal Func<Task>? ControlSeam
            {
                get => ControlSeamBackingField;
                set => ControlSeamBackingField = value;
            }
        }

        // (1) An external type calls the seam setter.
        internal sealed class ExternalSetterCaller
        {
            public void Mutate(ControlSeamHolder target) => target.ControlSeam = () => Task.CompletedTask;
        }

        // (2) An external type stores directly to the EXACT target backing field.
        internal sealed class ExternalFieldStorer
        {
            public void Store(ControlSeamHolder target) => target.ControlSeamBackingField = () => Task.CompletedTask;
        }

        // (3) A different external type obtains a PropertyInfo and calls SetValue.
        internal sealed class ExternalReflectionSetter
        {
            public void Assign(ControlSeamHolder target) =>
                typeof(ControlSeamHolder).GetProperty(SeamName, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(target, (Func<Task>)(() => Task.CompletedTask));
        }

        // Negative: an unrelated type whose field has the IDENTICAL NAME but a different identity — must NOT be reported.
        internal sealed class UnrelatedSameNamedField
        {
            internal Func<Task>? ControlSeamBackingField;
            public void Store(UnrelatedSameNamedField target) => target.ControlSeamBackingField = () => Task.CompletedTask;
        }
    }

    private static List<string> FindSeamWrites(Assembly assembly, params string[] propNames)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        // Resolve each configured seam to the EXACT identity of its backing field (via its getter's ldfld/ldsfld) and its
        // setter method. Fail closed if a seam property or its backing field cannot be resolved — never fall back to names.
        var targetFields = new HashSet<(Module Module, int Token)>();
        var setterIds = new HashSet<(Module Module, int Token)>();
        var setterOwnerOfField = new Dictionary<(Module, int), (Module, int)>(); // backing field -> its own setter
        foreach (var propName in propNames)
        {
            var props = types
                .Select(t => t.GetProperty(propName, all))
                .Where(p => p is not null)
                .Cast<PropertyInfo>()
                .ToList();
            if (props.Count == 0)
            {
                throw new InvalidOperationException($"Seam property '{propName}' not found in {assembly.GetName().Name}.");
            }

            foreach (var prop in props)
            {
                var backing = ResolveBackingField(prop)
                    ?? throw new InvalidOperationException(
                        $"Backing field for seam '{propName}' in {prop.DeclaringType?.FullName} could not be resolved.");
                var fieldId = (backing.Module, backing.MetadataToken);
                targetFields.Add(fieldId);
                if (prop.SetMethod is { } setter)
                {
                    var setterId = (setter.Module, setter.MetadataToken);
                    setterIds.Add(setterId);
                    setterOwnerOfField[fieldId] = setterId;
                }
            }
        }

        var hits = new List<string>();
        foreach (var type in types)
        {
            var members = type.GetMethods(all | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(type.GetConstructors(all | BindingFlags.DeclaredOnly));
            foreach (var method in members)
            {
                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch { il = null; }
                if (il is null)
                {
                    continue;
                }

                var typeArgs = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
                var methodArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : Type.EmptyTypes;
                var methodId = (method.Module, method.MetadataToken);

                for (var i = 0; i + 5 <= il.Length; i++)
                {
                    var op = il[i];
                    var token = BitConverter.ToInt32(il, i + 1);
                    if (op is 0x28 or 0x6F) // call / callvirt
                    {
                        MethodBase? callee;
                        try { callee = method.Module.ResolveMethod(token, typeArgs, methodArgs); }
                        catch { continue; }
                        if (callee is null)
                        {
                            continue;
                        }
                        if (setterIds.Contains((callee.Module, callee.MetadataToken)))
                        {
                            hits.Add($"{SetterKind}: {type.FullName}.{method.Name} -> {callee.Name}");
                        }
                        else if (callee.Name == "SetValue" && callee.DeclaringType?.Namespace == "System.Reflection")
                        {
                            // Conservative: ANY reflection assignment anywhere in the assembly (production has none).
                            hits.Add($"{ReflectionKind}: {type.FullName}.{method.Name} -> {callee.DeclaringType!.Name}.SetValue");
                        }
                    }
                    else if (op is 0x7D or 0x80) // stfld / stsfld
                    {
                        FieldInfo? field;
                        try { field = method.Module.ResolveField(token, typeArgs, methodArgs); }
                        catch { continue; }
                        if (field is null)
                        {
                            continue;
                        }

                        var fieldId = (field.Module, field.MetadataToken);
                        if (!targetFields.Contains(fieldId))
                        {
                            continue; // exact identity — a same-named unrelated field is NOT a target
                        }

                        // Legitimate ONLY in the target field's OWN property setter or the field's declaring-type ctor.
                        var isDeclaringCtor = method is ConstructorInfo && field.DeclaringType == type;
                        var isOwnSetter = setterOwnerOfField.TryGetValue(fieldId, out var owner) && owner == methodId;
                        if (!isDeclaringCtor && !isOwnSetter)
                        {
                            hits.Add($"{StoreKind}: {type.FullName}.{method.Name} -> {field.DeclaringType?.Name}.{field.Name}");
                        }
                    }
                }
            }
        }

        return hits;
    }

    // Resolves the backing field a property actually reads, by scanning its getter's IL for the first ldfld/ldsfld.
    private static FieldInfo? ResolveBackingField(PropertyInfo prop)
    {
        var getter = prop.GetMethod;
        byte[]? il;
        try { il = getter?.GetMethodBody()?.GetILAsByteArray(); }
        catch { il = null; }
        if (getter is null || il is null)
        {
            return null;
        }

        var typeArgs = prop.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : Type.EmptyTypes;
        for (var i = 0; i + 5 <= il.Length; i++)
        {
            if (il[i] is 0x7B or 0x7E) // ldfld / ldsfld
            {
                try { return getter.Module.ResolveField(BitConverter.ToInt32(il, i + 1), typeArgs, Type.EmptyTypes); }
                catch { return null; }
            }
        }

        return null;
    }

    private static void AssertIvtAllowlist(Assembly assembly, params string[] expected)
    {
        var actual = assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0].Trim())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), actual);
    }
}

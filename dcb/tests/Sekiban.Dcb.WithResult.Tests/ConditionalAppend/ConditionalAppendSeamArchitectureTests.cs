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

    private static readonly string[] AllSeams =
    {
        "AfterConditionalCommitHook", "ConditionalEventIdFactory", "ConditionalSortableIdFactory", "VerificationBudgetOverride"
    };

    [Fact]
    public void ProductionAssemblies_ContainNoSeamWrite_ByAnyPath()
    {
        // Structural IL guard covering EVERY write surface: setter calls (call/callvirt set_X), direct backing-field
        // stores (stfld/stsfld) outside the narrowly-allowed default init / own setter, and reflection assignment
        // (PropertyInfo/FieldInfo.SetValue) ANYWHERE in the assembly (production uses zero reflection SetValue, so this is
        // a clean conservative rejection — no allowlist needed).
        Assert.Empty(FindSeamWrites(typeof(SqliteEventStore).Assembly, "AfterConditionalCommitHook"));
        Assert.Empty(FindSeamWrites(typeof(CosmosDbEventStore).Assembly, "AfterConditionalCommitHook"));
        Assert.Empty(FindSeamWrites(typeof(DynamoDbEventStore).Assembly, "AfterConditionalCommitHook"));
        Assert.Empty(FindSeamWrites(typeof(IConditionalEventStore).Assembly, AllSeams));
    }

    // ── Three INDEPENDENT, mode-specific non-vacuous controls: each proves one scanner branch, and each fails if that
    //    branch is removed (a per-kind assertion, not an aggregate). The control fixtures below produce exactly one
    //    write pattern each, from a NON-declaring external type. ───────────────────────────────────────────────────────

    [Fact]
    public void Control_SetterCall_IsDetected()
    {
        var hits = FindSeamWrites(typeof(SeamWriteControls).Assembly, AllSeams);
        Assert.Contains(hits, h => h.StartsWith(SetterKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamWriteControls.ExternalSetterCaller), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_DirectBackingFieldStore_IsDetected()
    {
        var hits = FindSeamWrites(typeof(SeamWriteControls).Assembly, AllSeams);
        Assert.Contains(hits, h => h.StartsWith(StoreKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamWriteControls.ExternalFieldStorer), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_ReflectionSetValueFromNonDeclaringType_IsDetected()
    {
        var hits = FindSeamWrites(typeof(SeamWriteControls).Assembly, AllSeams);
        Assert.Contains(hits, h => h.StartsWith(ReflectionKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamWriteControls.ExternalReflectionSetter), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Control fixtures for the seam-write scanner. Each is a NON-seam-declaring external type that performs exactly
    ///     one forbidden write pattern, so the scanner must attribute a hit of the corresponding kind to it.
    /// </summary>
    private static class SeamWriteControls
    {
        // (1) An external type calls a seam setter.
        internal sealed class ExternalSetterCaller
        {
            public Func<Task>? AfterConditionalCommitHook { get; set; }
            public void Mutate(ExternalSetterCaller target) => target.AfterConditionalCommitHook = () => Task.CompletedTask;
        }

        // (2) An external/direct store to a seam-named field (models a converted-to-manual backing field assigned outside
        //     any setter/ctor — a stfld the scanner must catch by the normalized field name).
        internal sealed class ExternalFieldStorer
        {
            public Func<Task>? AfterConditionalCommitHook;
            public void Store(ExternalFieldStorer target) => target.AfterConditionalCommitHook = () => Task.CompletedTask;
        }

        // (3) A different external type obtains a PropertyInfo and calls SetValue.
        internal sealed class ExternalReflectionSetter
        {
            public void Assign(object target) =>
                typeof(ExternalSetterCaller)
                    .GetProperty(nameof(ExternalSetterCaller.AfterConditionalCommitHook))!
                    .SetValue(target, (Func<Task>)(() => Task.CompletedTask));
        }
    }

    private static List<string> FindSeamWrites(Assembly assembly, params string[] propNames)
    {
        var setterNames = new HashSet<string>(propNames.Select(n => "set_" + n), StringComparer.Ordinal);
        var seamNameSet = new HashSet<string>(propNames, StringComparer.Ordinal);

        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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
                        if (setterNames.Contains(callee.Name))
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

                        // Normalize an auto-property backing field (`<Prop>k__BackingField`) to its property name; a plain
                        // field keeps its own name. Match either against the seam names.
                        var normalized = NormalizeFieldName(field.Name);
                        if (!seamNameSet.Contains(normalized))
                        {
                            continue;
                        }

                        // Legitimate ONLY in the property's own auto-generated setter or the declaring type's ctor.
                        var isDeclaringCtor = method is ConstructorInfo && field.DeclaringType == type;
                        if (!isDeclaringCtor && !string.Equals(method.Name, "set_" + normalized, StringComparison.Ordinal))
                        {
                            hits.Add($"{StoreKind}: {type.FullName}.{method.Name} -> {field.Name}");
                        }
                    }
                }
            }
        }

        return hits;
    }

    private static string NormalizeFieldName(string fieldName) =>
        fieldName.StartsWith('<') && fieldName.EndsWith(">k__BackingField", StringComparison.Ordinal)
            ? fieldName[1..fieldName.IndexOf('>')]
            : fieldName;

    private static void AssertIvtAllowlist(Assembly assembly, params string[] expected)
    {
        var actual = assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0].Trim())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), actual);
    }
}

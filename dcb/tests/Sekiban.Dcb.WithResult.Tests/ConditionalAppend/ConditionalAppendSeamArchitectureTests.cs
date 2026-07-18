using System.Reflection;
using System.Runtime.CompilerServices;
using Dcb.Domain;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     Structural guard for the SEK-G16 test-only seams (the post-commit response-loss <c>AfterConditionalCommitHook</c> on
///     each provider store, plus the executor allocator probes and the coordinator verification-budget override in Core). It
///     proves a seam cannot silently become a public/production-reachable runtime mutation surface, and — crucially — that
///     the guarantee is applied to EVERY production seam, driven from the SINGLE exhaustive <see cref="SeamInventory" />.
///     The Postgres provider store cannot be referenced from this assembly, so its identical guards live in
///     <c>Sekiban.Dcb.Postgres.Tests.ConditionalAppend.PostgresSeamArchitectureTests</c>; the shared inventory ties the two
///     together and the snapshot assertion below pins the whole surface (Postgres included) so nothing escapes.
/// </summary>
public class ConditionalAppendSeamArchitectureTests
{
    private const string HookName = "AfterConditionalCommitHook";

    // Provider store types this assembly CAN reference (Postgres is guarded in its own provider test project).
    public static IEnumerable<object[]> SeamStoreTypes => new[]
    {
        new object[] { typeof(SqliteEventStore) },
        new object[] { typeof(CosmosDbEventStore) }
    };

    // Assemblies referenceable here that DECLARE at least one seam (used to drive the exact seam-target scan).
    private static readonly (string Name, Assembly Assembly)[] SeamDeclaringAssembliesHere =
    {
        (SeamInventory.CoreAssembly, typeof(IConditionalEventStore).Assembly),
        (SeamInventory.SqliteAssembly, typeof(SqliteEventStore).Assembly),
        (SeamInventory.CosmosAssembly, typeof(CosmosDbEventStore).Assembly)
    };

    // Assemblies referenceable here that must be reflection-scanned (includes DynamoDB, which declares NO settable seam).
    private static readonly (string Name, Assembly Assembly)[] ReflectionScannedAssembliesHere =
    {
        (SeamInventory.CoreAssembly, typeof(IConditionalEventStore).Assembly),
        (SeamInventory.SqliteAssembly, typeof(SqliteEventStore).Assembly),
        (SeamInventory.CosmosAssembly, typeof(CosmosDbEventStore).Assembly),
        ("Sekiban.Dcb.DynamoDB", typeof(DynamoDbEventStore).Assembly)
    };

    // ── The single exhaustive inventory is pinned here: count + every (assembly, type, property) tuple. Adding or removing
    //    a production seam MUST update SeamInventory, which changes this snapshot — omission becomes a failing assertion. ──

    [Fact]
    public void SeamInventory_Snapshot_IsExactlyTheExpectedProductionSurface()
    {
        var actual = SeamInventory.Entries
            .Select(e => $"{e.AssemblyName}|{e.DeclaringTypeFullName}|{e.PropertyName}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "Sekiban.Dcb.Core|Sekiban.Dcb.Actors.CoreGeneralSekibanExecutor|ConditionalEventIdFactory",
            "Sekiban.Dcb.Core|Sekiban.Dcb.Actors.CoreGeneralSekibanExecutor|ConditionalSortableIdFactory",
            "Sekiban.Dcb.Core|Sekiban.Dcb.Storage.ConditionalAppendCoordinator|VerificationBudgetOverride",
            "Sekiban.Dcb.CosmosDb|Sekiban.Dcb.CosmosDb.CosmosDbEventStore|AfterConditionalCommitHook",
            "Sekiban.Dcb.Postgres|Sekiban.Dcb.Postgres.PostgresEventStore|AfterConditionalCommitHook",
            "Sekiban.Dcb.Sqlite|Sekiban.Dcb.Sqlite.SqliteEventStore|AfterConditionalCommitHook"
        }.OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(6, SeamInventory.Entries.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SeamInventory_ProviderHookEntries_ResolveToRealSettableProperties_Here()
    {
        // Every inventory entry for an assembly referenceable here must resolve to a real, settable, non-public instance
        // property whose backing field the scanner can locate. Postgres is verified in its own provider test project.
        foreach (var (name, asm) in SeamDeclaringAssembliesHere)
        {
            foreach (var entry in SeamInventory.Entries.Where(e => e.AssemblyName == name))
            {
                var type = asm.GetType(entry.DeclaringTypeFullName);
                Assert.NotNull(type);
                var prop = type!.GetProperty(entry.PropertyName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.NotNull(prop);
                Assert.True(prop!.CanWrite, $"{entry.DeclaringTypeFullName}.{entry.PropertyName} must be settable (a seam).");
                Assert.NotNull(SeamWriteScanner.ResolveBackingField(prop));
            }
        }
    }

    [Fact]
    public void ProviderStores_ExposeNoUnlistedFuncTaskSeam()
    {
        // Reverse guard: any internal instance Func<Task> property on a provider store MUST be in the inventory, so a
        // silently-added second hook cannot escape the structural scan.
        foreach (var storeType in new[] { typeof(SqliteEventStore), typeof(CosmosDbEventStore) })
        {
            var hookShaped = storeType
                .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(Func<Task>))
                .Select(p => p.Name);
            var listed = SeamInventory.Entries
                .Where(e => e.DeclaringTypeFullName == storeType.FullName)
                .Select(e => e.PropertyName)
                .ToHashSet();
            Assert.All(hookShaped, n => Assert.Contains(n, listed));
        }
    }

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
        // assemblies (for the internal post-commit-response-loss signal) — never a test assembly. Postgres's own IVT is
        // asserted in Sekiban.Dcb.Postgres.Tests (that test assembly is where the Postgres seam is reachable).
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

    [Fact]
    public void ProductionAssemblies_ContainNoSeamTargetWrite_ByAnyPath()
    {
        // Exact-identity seam-target scan: for each seam-declaring assembly, drive the scan from the inventory's own list of
        // property names for that assembly. Setter calls and direct backing-field stores (matched by EXACT resolved
        // FieldInfo identity, never a name) outside the field's own setter / declaring ctor are reported. Fails closed if a
        // configured seam or backing field cannot be resolved. (Postgres's identical scan runs in its provider test project.)
        foreach (var (name, asm) in SeamDeclaringAssembliesHere)
        {
            var props = SeamInventory.PropertyNamesIn(name);
            Assert.NotEmpty(props); // a seam-declaring assembly must contribute at least one name — no vacuous scan
            Assert.Empty(SeamWriteScanner.FindSeamTargetWrites(asm, props));
        }
    }

    [Fact]
    public void ProductionAssemblies_ContainNoReflectionAssignment_IncludingDynamo()
    {
        // Reflection assignment ban is DECOUPLED from seam resolution and runs unconditionally over every referenceable
        // production assembly, including DynamoDB (which declares no settable seam but must still be reflection-clean).
        // Postgres's reflection scan runs in its provider test project.
        foreach (var (_, asm) in ReflectionScannedAssembliesHere)
        {
            Assert.Empty(SeamWriteScanner.FindReflectionAssignments(asm));
        }
    }

    [Fact]
    public void SeamTargetResolution_FailsClosed_WhenABackingFieldCannotBeResolved()
    {
        // A configured seam that does not exist in the scanned assembly must THROW (fail closed), never silently pass by
        // falling back to name matching. DynamoDB declares no AfterConditionalCommitHook.
        Assert.Throws<InvalidOperationException>(
            () => SeamWriteScanner.FindSeamTargetWrites(typeof(DynamoDbEventStore).Assembly, HookName));
    }

    // ── Independent, mode-specific non-vacuous controls. Each proves one scanner branch (per-kind assertion, not an
    //    aggregate) and fails if that branch is removed. The control "seam" is a manual property whose INTERNAL backing
    //    field is directly writable by external fixtures, so the stfld branch can be exercised against the EXACT resolved
    //    target field identity. ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Control_SetterCall_IsDetected()
    {
        var hits = SeamWriteScanner.FindSeamTargetWrites(typeof(SeamWriteControls).Assembly, SeamWriteControls.SeamName);
        Assert.Contains(hits, h => h.StartsWith(SeamWriteScanner.SetterKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamWriteControls.ExternalSetterCaller), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_DirectBackingFieldStore_ToExactTargetField_IsDetected()
    {
        var hits = SeamWriteScanner.FindSeamTargetWrites(typeof(SeamWriteControls).Assembly, SeamWriteControls.SeamName);
        Assert.Contains(hits, h => h.StartsWith(SeamWriteScanner.StoreKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamWriteControls.ExternalFieldStorer), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_UnrelatedSameNamedField_IsNotReported()
    {
        // Negative control: a DIFFERENT type has a field with the IDENTICAL name to the control seam's backing field. Because
        // matching is by exact FieldInfo identity (not name), its external store must NOT be reported.
        var hits = SeamWriteScanner.FindSeamTargetWrites(typeof(SeamWriteControls).Assembly, SeamWriteControls.SeamName);
        Assert.DoesNotContain(hits, h => h.Contains(nameof(SeamWriteControls.UnrelatedSameNamedField), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_ReflectionSetValueFromNonDeclaringType_IsDetected()
    {
        // The decoupled reflection scan is non-vacuous: an external SetValue is reported without any seam configuration.
        var hits = SeamWriteScanner.FindReflectionAssignments(typeof(SeamWriteControls).Assembly);
        Assert.Contains(hits, h => h.StartsWith(SeamWriteScanner.ReflectionKind + ":", StringComparison.Ordinal)
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

    private static void AssertIvtAllowlist(Assembly assembly, params string[] expected)
    {
        var actual = assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0].Trim())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), actual);
    }
}

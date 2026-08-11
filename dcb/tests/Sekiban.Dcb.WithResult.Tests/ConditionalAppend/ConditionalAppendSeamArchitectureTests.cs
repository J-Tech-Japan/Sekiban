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
///     Structural guard for the SEK-G16 test-only seams. It proves a seam cannot silently become a public/production
///     runtime mutation surface AND — the exhaustiveness requirement — that <see cref="SeamInventory" /> is a TRUE
///     reverse-discovered inventory, not a hand-pinned count: every seam-shaped member that actually exists in the
///     authoritative production assemblies is discovered by <see cref="ProductionSeamRule" /> and proven to be listed
///     (missing AND extra both fail), across all four current seam shapes (<c>Func&lt;Task&gt;</c>, <c>Func&lt;Guid&gt;</c>,
///     <c>Func&lt;string&gt;</c>, <c>TimeSpan?</c>). The Postgres provider store cannot be referenced from this assembly,
///     so its identical reverse-discovery / exact-field / reflection / IVT guards live in
///     <c>Sekiban.Dcb.Postgres.Tests.ConditionalAppend.PostgresSeamArchitectureTests</c>; together the two cover the full
///     five-assembly set (Core, Postgres, SQLite, Cosmos, DynamoDB).
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

    // Authoritative assemblies resolved to the ones referenceable HERE; Postgres is deliberately null (delegated).
    private static Assembly? ResolveAuthoritativeAssemblyHere(string name) => name switch
    {
        SeamInventory.CoreAssembly => typeof(IConditionalEventStore).Assembly,
        SeamInventory.SqliteAssembly => typeof(SqliteEventStore).Assembly,
        SeamInventory.CosmosAssembly => typeof(CosmosDbEventStore).Assembly,
        "Sekiban.Dcb.DynamoDB" => typeof(DynamoDbEventStore).Assembly,
        SeamInventory.PostgresAssembly => null, // not referenced here — covered by Sekiban.Dcb.Postgres.Tests
        _ => throw new InvalidOperationException($"Unknown authoritative assembly '{name}'.")
    };

    // Assemblies whose seam surface / reflection cleanliness is owned by THIS test project.
    private static readonly string[] OwnedHere =
        { SeamInventory.CoreAssembly, SeamInventory.SqliteAssembly, SeamInventory.CosmosAssembly, "Sekiban.Dcb.DynamoDB" };

    // ── Exhaustiveness: reverse discovery == inventory, and the reflection-scan coverage == the authoritative list. ──────

    [Fact]
    public void SeamInventory_Snapshot_IsExactlyTheExpectedProductionSurface()
    {
        // Human-readable pin of the whole five-assembly surface (documentation); reverse discovery below proves it matches
        // reality. Adding/removing a production seam must update SeamInventory, which changes this snapshot too.
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
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProductionSeams_ReverseDiscovered_EqualInventory_ForOwnedAssemblies()
    {
        // TRUE reverse discovery: for each assembly this project owns, discover every seam-shaped non-public settable
        // member that actually exists and assert the discovered (assembly,type,property) set equals the inventory subset
        // EXACTLY. A missing entry (listed but not present) or an extra (present but unlisted — a new Core probe, a new
        // provider hook, a differently-shaped seam, or a seam on any other type) both fail here.
        foreach (var name in OwnedHere)
        {
            AssertReverseDiscoveryEqualsInventory(ResolveAuthoritativeAssemblyHere(name)!);
        }
    }

    [Fact]
    public void NoHiddenFieldSeam_InOwnedProductionAssemblies()
    {
        // Anti-evasion: no writable non-public seam-shaped field (other than compiler-generated auto-property backing
        // fields) may exist — a hand-rolled field seam would sidestep property-based reverse discovery.
        foreach (var name in OwnedHere)
        {
            Assert.Empty(ProductionSeamRule.DiscoverNonBackingSeamFields(ResolveAuthoritativeAssemblyHere(name)!));
        }
    }

    [Fact]
    public void ReflectionScan_Coverage_UnionMatchesAuthoritativeList()
    {
        // The scan is DRIVEN by SeamInventory.ReflectionScannedAssemblies (so the authoritative list cannot be declared-
        // but-unused). Every entry is either scanned here (and proven reflection-clean) or explicitly delegated; the union
        // of scanned+delegated must equal the authoritative list, and this project must own exactly OwnedHere with only
        // Postgres delegated. Dropping an assembly from the list, or from a scan path, changes these sets and fails.
        var authoritative = SeamInventory.ReflectionScannedAssemblies.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "Sekiban.Dcb.Core", "Sekiban.Dcb.CosmosDb", "Sekiban.Dcb.DynamoDB", "Sekiban.Dcb.Postgres", "Sekiban.Dcb.Sqlite" },
            authoritative);

        var scanned = new List<string>();
        var delegated = new List<string>();
        foreach (var name in SeamInventory.ReflectionScannedAssemblies)
        {
            var asm = ResolveAuthoritativeAssemblyHere(name);
            if (asm is null)
            {
                delegated.Add(name);
                continue;
            }
            Assert.Empty(SeamWriteScanner.FindReflectionAssignments(asm));
            scanned.Add(name);
        }

        Assert.Equal(authoritative, scanned.Concat(delegated).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal(OwnedHere.OrderBy(s => s, StringComparer.Ordinal).ToArray(), scanned.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { SeamInventory.PostgresAssembly }, delegated.ToArray());
    }

    // ── Non-mutability / non-surface guards (per referenceable provider store). ──────────────────────────────────────────

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
        // The provider assemblies grant internals only to the intended test assembly; Core grants the four provider
        // assemblies (for the internal post-commit-response-loss signal), Sekiban.Dcb.Orleans.Core (SEK-G18: the Orleans
        // host reads the internal projection rebuild-required signal), and the two Orleans facades (SEK-G31: retained
        // constructors share the process generator/coordinator without adding public API) — never a test assembly.
        // Postgres's own IVT is asserted in Sekiban.Dcb.Postgres.Tests (where the Postgres seam is reachable).
        AssertIvtAllowlist(typeof(SqliteEventStore).Assembly, "Sekiban.Dcb.WithResult.Tests");
        AssertIvtAllowlist(typeof(CosmosDbEventStore).Assembly, "Sekiban.Dcb.WithResult.Tests");
        AssertIvtAllowlist(
            typeof(IConditionalEventStore).Assembly,
            "Sekiban.Dcb.Postgres", "Sekiban.Dcb.Sqlite", "Sekiban.Dcb.CosmosDb", "Sekiban.Dcb.DynamoDB",
            "Sekiban.Dcb.Orleans.Core", "Sekiban.Dcb.Orleans.WithResult", "Sekiban.Dcb.Orleans.WithoutResult");
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
        // Exact-identity seam-target scan: for each owned seam-declaring assembly, drive the scan from the inventory's own
        // list of property names. Setter calls and direct backing-field stores (matched by EXACT resolved FieldInfo
        // identity, never a name) outside the field's own setter / declaring ctor are reported. Fails closed if a
        // configured seam or backing field cannot be resolved. (Postgres's identical scan runs in its provider test project.)
        foreach (var name in OwnedHere)
        {
            var props = SeamInventory.PropertyNamesIn(name);
            if (props.Length == 0)
            {
                continue; // DynamoDB declares no settable seam — nothing to target-scan (still reflection-scanned above)
            }
            Assert.Empty(SeamWriteScanner.FindSeamTargetWrites(ResolveAuthoritativeAssemblyHere(name)!, props));
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

    // ── Independent, mode-specific non-vacuous controls. Each proves one scanner/discovery branch and fails if removed. ──

    [Fact]
    public void Control_SetterCall_IsDetected()
    {
        var hits = SeamWriteScanner.FindSeamTargetWrites(typeof(SeamControls).Assembly, SeamControls.FuncTaskSeamName);
        Assert.Contains(hits, h => h.StartsWith(SeamWriteScanner.SetterKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamControls.ExternalSetterCaller), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_DirectBackingFieldStore_ToExactTargetField_IsDetected()
    {
        var hits = SeamWriteScanner.FindSeamTargetWrites(typeof(SeamControls).Assembly, SeamControls.FuncTaskSeamName);
        Assert.Contains(hits, h => h.StartsWith(SeamWriteScanner.StoreKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamControls.ExternalFieldStorer), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_UnrelatedSameNamedField_IsNotReported()
    {
        // Negative control: a DIFFERENT type has a field with the IDENTICAL name to the control seam's backing field. Because
        // matching is by exact FieldInfo identity (not name), its external store must NOT be reported.
        var hits = SeamWriteScanner.FindSeamTargetWrites(typeof(SeamControls).Assembly, SeamControls.FuncTaskSeamName);
        Assert.DoesNotContain(hits, h => h.Contains(nameof(SeamControls.UnrelatedSameNamedField), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_ReflectionSetValueFromNonDeclaringType_IsDetected()
    {
        // The decoupled reflection scan is non-vacuous: an external SetValue is reported without any seam configuration.
        var hits = SeamWriteScanner.FindReflectionAssignments(typeof(SeamControls).Assembly);
        Assert.Contains(hits, h => h.StartsWith(SeamWriteScanner.ReflectionKind + ":", StringComparison.Ordinal)
            && h.Contains(nameof(SeamControls.ExternalReflectionSetter), StringComparison.Ordinal));
    }

    [Fact]
    public void Control_ReverseDiscovery_FindsEverySeamShape_OnNewDeclaringTypes()
    {
        // Proves reverse discovery is non-vacuous for ALL four shapes and for NEW declaring types (not just the known
        // production stores): each control fixture is a distinct new type carrying one shape.
        var discovered = ProductionSeamRule.DiscoverSeamProperties(typeof(SeamControls).Assembly).ToList();
        Assert.Contains(discovered, e => e.PropertyName == "ControlTaskSeam");   // Func<Task>
        Assert.Contains(discovered, e => e.PropertyName == "ControlGuidSeam");   // Func<Guid>
        Assert.Contains(discovered, e => e.PropertyName == "ControlStringSeam"); // Func<string>
        Assert.Contains(discovered, e => e.PropertyName == "ControlBudgetSeam"); // TimeSpan?
    }

    [Fact]
    public void Control_ReverseEquality_Fails_WhenAnUnlistedNonFuncTaskSeamExists()
    {
        // Mutation control: the controls assembly declares seam-shaped members (including non-Func<Task> shapes) that are
        // NOT in SeamInventory. Reverse-equality against the inventory therefore MUST fail (extras detected). This is the
        // exact assertion the production test runs — proven to catch an unlisted, non-Func<Task>, new-type seam.
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(
            () => AssertReverseDiscoveryEqualsInventory(typeof(SeamControls).Assembly));
    }

    [Fact]
    public void Control_HiddenFieldSeam_IsDiscovered()
    {
        // Anti-evasion control: a hand-rolled writable non-public field seam IS discovered (proving the production
        // "no hidden field seam" assertion is non-vacuous).
        var fields = ProductionSeamRule.DiscoverNonBackingSeamFields(typeof(SeamControls).Assembly).ToList();
        Assert.Contains(fields, f => f.Contains(nameof(SeamControls.HandRolledFieldSeamHolder), StringComparison.Ordinal));
    }

    private static void AssertReverseDiscoveryEqualsInventory(Assembly assembly)
    {
        var asmName = assembly.GetName().Name!;
        var discovered = ProductionSeamRule.DiscoverSeamProperties(assembly)
            .Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var expected = SeamInventory.Entries.Where(e => e.AssemblyName == asmName)
            .Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, discovered);
    }

    private static string Key(SeamEntry e) => $"{e.AssemblyName}|{e.DeclaringTypeFullName}|{e.PropertyName}";

    /// <summary>
    ///     Control fixtures. One distinct new type per seam shape (proving reverse discovery catches non-Func&lt;Task&gt;
    ///     shapes and new declaring types), plus the exact-field / setter / reflection / negative-name controls for the
    ///     IL scanner. All live in the test assembly, so production reverse discovery (over the five authoritative
    ///     assemblies) never sees them.
    /// </summary>
    private static class SeamControls
    {
        public const string FuncTaskSeamName = "ControlTaskSeam";

        // One holder per shape — each is a NEW declaring type with a non-public settable seam-shaped property.
        internal sealed class TaskSeamHolder
        {
            internal Func<Task>? ControlTaskSeamBackingField;
            internal Func<Task>? ControlTaskSeam
            {
                get => ControlTaskSeamBackingField;
                set => ControlTaskSeamBackingField = value;
            }
        }

        internal sealed class GuidSeamHolder
        {
            internal Func<Guid> ControlGuidSeam { get; set; } = Guid.NewGuid;
        }

        internal sealed class StringSeamHolder
        {
            internal Func<string> ControlStringSeam { get; set; } = () => string.Empty;
        }

        internal sealed class BudgetSeamHolder
        {
            internal TimeSpan? ControlBudgetSeam { get; set; }
        }

        // (1) An external type calls the seam setter.
        internal sealed class ExternalSetterCaller
        {
            public void Mutate(TaskSeamHolder target) => target.ControlTaskSeam = () => Task.CompletedTask;
        }

        // (2) An external type stores directly to the EXACT target backing field.
        internal sealed class ExternalFieldStorer
        {
            public void Store(TaskSeamHolder target) => target.ControlTaskSeamBackingField = () => Task.CompletedTask;
        }

        // (3) A different external type obtains a PropertyInfo and calls SetValue.
        internal sealed class ExternalReflectionSetter
        {
            public void Assign(TaskSeamHolder target) =>
                typeof(TaskSeamHolder).GetProperty(FuncTaskSeamName, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(target, (Func<Task>)(() => Task.CompletedTask));
        }

        // Negative: an unrelated type whose field has the IDENTICAL NAME but a different identity — must NOT be reported.
        internal sealed class UnrelatedSameNamedField
        {
            internal Func<Task>? ControlTaskSeamBackingField;
            public void Store(UnrelatedSameNamedField target) => target.ControlTaskSeamBackingField = () => Task.CompletedTask;
        }

        // Anti-evasion positive: a hand-rolled writable non-public field seam (NOT a backing field) — must be discovered.
        internal sealed class HandRolledFieldSeamHolder
        {
            internal Func<Task>? HiddenSeam;
            public void Touch() => HiddenSeam = () => Task.CompletedTask;
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

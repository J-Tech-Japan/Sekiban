using System.Reflection;
using System.Runtime.CompilerServices;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests.ConditionalAppend;

/// <summary>
///     Structural guard for the Postgres <c>AfterConditionalCommitHook</c> test seam. The Postgres provider store cannot be
///     referenced from <c>Sekiban.Dcb.WithResult.Tests</c>, so the identical reverse-discovery / exact-field / API /
///     constructor / reflection / IVT guarantees are applied HERE, driven from the same single <see cref="SeamInventory" />
///     and <see cref="ProductionSeamRule" />. This closes the five-assembly set: WithResult owns Core/SQLite/Cosmos/Dynamo,
///     this project owns Postgres.
/// </summary>
public class PostgresSeamArchitectureTests
{
    private static readonly Type StoreType = typeof(PostgresEventStore);
    private static readonly Assembly PostgresAssembly = StoreType.Assembly;
    private const string HookName = "AfterConditionalCommitHook";
    private const string TagHeadHookName = "TagHeadProtocolHook";
    private const string BeforeTaggedStreamReaderReadHookName = "BeforeTaggedStreamReaderReadHook";
    private const string AfterTaggedStreamReaderReadHookName = "AfterTaggedStreamReaderReadHook";

    // Authoritative assemblies resolved to the ones this project owns for scanning; only Postgres is owned here.
    private static Assembly? ResolveOwnedAssembly(string name) =>
        name == SeamInventory.PostgresAssembly ? PostgresAssembly : null;

    [Fact]
    public void PostgresSeams_ReverseDiscovered_EqualInventory()
    {
        // TRUE reverse discovery over the Postgres assembly: the discovered seam-shaped non-public settable members must
        // equal the inventory's Postgres subset EXACTLY (missing OR extra fails) — not a hand-maintained assertion.
        var discovered = ProductionSeamRule.DiscoverSeamProperties(PostgresAssembly)
            .Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var expected = SeamInventory.Entries.Where(e => e.AssemblyName == SeamInventory.PostgresAssembly)
            .Select(Key).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, discovered);
        Assert.NotEmpty(expected); // non-vacuous: Postgres genuinely contributes a seam
    }

    [Fact]
    public void NoHiddenFieldSeam_InPostgresAssembly()
    {
        // Anti-evasion: no writable non-public seam-shaped field (other than compiler-generated backing fields) exists.
        Assert.Empty(ProductionSeamRule.DiscoverNonBackingSeamFields(PostgresAssembly));
    }

    [Fact]
    public void SeamInventory_ListsThePostgresHooks_ResolvingToRealSettableProperties()
    {
        var entries = SeamInventory.Entries.Where(e => e.AssemblyName == SeamInventory.PostgresAssembly)
            .OrderBy(e => e.PropertyName, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[]
            {
                HookName,
                TagHeadHookName,
                BeforeTaggedStreamReaderReadHookName,
                AfterTaggedStreamReaderReadHookName
            }.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            entries.Select(e => e.PropertyName).ToArray());
        foreach (var entry in entries)
        {
            Assert.Equal("Sekiban.Dcb.Postgres.PostgresEventStore", entry.DeclaringTypeFullName);
            var type = PostgresAssembly.GetType(entry.DeclaringTypeFullName);
            Assert.NotNull(type);
            var prop = type!.GetProperty(entry.PropertyName,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(prop);
            Assert.True(prop!.CanWrite, "The Postgres seam must be settable.");
            Assert.NotNull(SeamWriteScanner.ResolveBackingField(prop)); // positive: exact backing field is resolvable
        }
    }

    [Fact]
    public void Hook_IsNonPublic_Instance_NotStatic()
    {
        foreach (var hookName in new[]
                 {
                     HookName,
                     TagHeadHookName,
                     BeforeTaggedStreamReaderReadHookName,
                     AfterTaggedStreamReaderReadHookName
                 })
        {
            Assert.NotNull(StoreType.GetProperty(hookName, BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.Null(StoreType.GetProperty(hookName, BindingFlags.Instance | BindingFlags.Public));
            Assert.Null(StoreType.GetProperty(hookName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        }
    }

    [Fact]
    public void Hook_IsAbsentFromPublicEventStoreInterfaces()
    {
        foreach (var contract in new[] { typeof(IEventStore), typeof(IConditionalEventStore), typeof(IHotEventStore) })
        {
            foreach (var hookName in new[]
                     {
                         HookName,
                         TagHeadHookName,
                         BeforeTaggedStreamReaderReadHookName,
                         AfterTaggedStreamReaderReadHookName
                     })
            {
                Assert.Null(contract.GetProperty(hookName));
            }
        }
    }

    [Fact]
    public void Hook_IsNotConstructorInjected_NorAPublicDiSurface()
    {
        foreach (var ctor in StoreType.GetConstructors())
        {
            Assert.DoesNotContain(ctor.GetParameters(), p =>
                p.Name == "afterConditionalCommitHook" || p.ParameterType == typeof(Func<Task>));
        }
        Assert.DoesNotContain(
            StoreType.GetProperties(BindingFlags.Instance | BindingFlags.Public),
            p => p.PropertyType == typeof(Func<Task>));
    }

    [Fact]
    public void PostgresAssembly_ContainsNoSeamTargetWrite_ByAnyPath()
    {
        // Exact-identity seam-target scan over the Postgres assembly, driven from the inventory's Postgres property list.
        var props = SeamInventory.PropertyNamesIn(SeamInventory.PostgresAssembly);
        Assert.NotEmpty(props); // non-vacuous: the Postgres seam contributes at least one name
        Assert.Empty(SeamWriteScanner.FindSeamTargetWrites(PostgresAssembly, props));
    }

    [Fact]
    public void ReflectionScan_Coverage_PostgresOwnedAndUnionComplete()
    {
        // Driven by the authoritative list: Postgres is scanned (and proven reflection-clean) here; the remaining four are
        // delegated to Sekiban.Dcb.WithResult.Tests. scanned ∪ delegated must equal the authoritative list exactly.
        var authoritative = SeamInventory.ReflectionScannedAssemblies.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var scanned = new List<string>();
        var delegated = new List<string>();
        foreach (var name in SeamInventory.ReflectionScannedAssemblies)
        {
            var asm = ResolveOwnedAssembly(name);
            if (asm is null)
            {
                delegated.Add(name);
                continue;
            }
            Assert.Empty(SeamWriteScanner.FindReflectionAssignments(asm));
            scanned.Add(name);
        }

        Assert.Equal(authoritative, scanned.Concat(delegated).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { SeamInventory.PostgresAssembly }, scanned.ToArray());
        Assert.Equal(
            new[] { "Sekiban.Dcb.Core", "Sekiban.Dcb.CosmosDb", "Sekiban.Dcb.DynamoDB", "Sekiban.Dcb.Sqlite" },
            delegated.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void InternalsVisibleTo_Allowlist_IsExactlyThisTestAssembly()
    {
        var actual = PostgresAssembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0].Trim())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Sekiban.Dcb.Postgres.Tests" }, actual);
    }

    private static string Key(SeamEntry e) => $"{e.AssemblyName}|{e.DeclaringTypeFullName}|{e.PropertyName}";
}

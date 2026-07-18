using System.Reflection;
using System.Runtime.CompilerServices;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests.ConditionalAppend;

/// <summary>
///     Structural guard for the Postgres <c>AfterConditionalCommitHook</c> test seam. The Postgres provider store cannot be
///     referenced from <c>Sekiban.Dcb.WithResult.Tests</c>, so the identical setter / backing-field / API / constructor /
///     reflection / IVT guarantees that the shared seam guard applies to SQLite, Cosmos and Core are applied HERE to the
///     Postgres assembly — driven from the same single <see cref="SeamInventory" />. Without this file one real production
///     seam would sit outside the structural guarantee.
/// </summary>
public class PostgresSeamArchitectureTests
{
    private static readonly Type StoreType = typeof(PostgresEventStore);
    private const string HookName = "AfterConditionalCommitHook";

    [Fact]
    public void SeamInventory_ListsThePostgresHook_ResolvingToARealSettableProperty()
    {
        var entry = Assert.Single(SeamInventory.Entries.Where(e => e.AssemblyName == SeamInventory.PostgresAssembly));
        Assert.Equal("Sekiban.Dcb.Postgres.PostgresEventStore", entry.DeclaringTypeFullName);
        Assert.Equal(HookName, entry.PropertyName);

        var type = StoreType.Assembly.GetType(entry.DeclaringTypeFullName);
        Assert.NotNull(type);
        var prop = type!.GetProperty(entry.PropertyName,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(prop);
        Assert.True(prop!.CanWrite, "The Postgres seam must be settable.");
        Assert.NotNull(SeamWriteScanner.ResolveBackingField(prop)); // positive: the exact backing field is resolvable
    }

    [Fact]
    public void Hook_IsNonPublic_Instance_NotStatic()
    {
        Assert.NotNull(StoreType.GetProperty(HookName, BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Null(StoreType.GetProperty(HookName, BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(StoreType.GetProperty(HookName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
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
    public void PostgresStore_ExposesNoUnlistedFuncTaskSeam()
    {
        // Reverse guard: every internal instance Func<Task> property must be the inventoried hook — a silently-added
        // second hook cannot escape.
        var hookShaped = StoreType
            .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(p => p.PropertyType == typeof(Func<Task>))
            .Select(p => p.Name);
        var listed = SeamInventory.Entries
            .Where(e => e.DeclaringTypeFullName == StoreType.FullName)
            .Select(e => e.PropertyName)
            .ToHashSet();
        Assert.All(hookShaped, n => Assert.Contains(n, listed));
    }

    [Fact]
    public void PostgresAssembly_ContainsNoSeamTargetWrite_ByAnyPath()
    {
        // Exact-identity seam-target scan over the Postgres assembly, driven from the inventory's Postgres property list.
        var props = SeamInventory.PropertyNamesIn(SeamInventory.PostgresAssembly);
        Assert.NotEmpty(props); // non-vacuous: the Postgres seam contributes at least one name
        Assert.Empty(SeamWriteScanner.FindSeamTargetWrites(StoreType.Assembly, props));
    }

    [Fact]
    public void PostgresAssembly_ContainsNoReflectionAssignment()
    {
        // Decoupled reflection-assignment ban applied to the Postgres assembly (production uses zero reflection SetValue).
        Assert.Empty(SeamWriteScanner.FindReflectionAssignments(StoreType.Assembly));
    }

    [Fact]
    public void InternalsVisibleTo_Allowlist_IsExactlyThisTestAssembly()
    {
        var actual = StoreType.Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0].Trim())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Sekiban.Dcb.Postgres.Tests" }, actual);
    }
}

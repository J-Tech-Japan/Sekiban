using Dcb.Domain;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Sqlite;
using Xunit;
namespace Sekiban.Dcb.WithResult.Tests.Capabilities;

/// <summary>
///     Sqlite is the case that settles the design argument. The same class, with the same name, is durable when it is a
///     file and volatile when it is <c>:memory:</c> — so a descriptor derived from the type name, or an attribute, or
///     the registration method that was called, would be wrong half the time. Only the live instance knows which one it
///     got, which is why the guard asks the live instance.
/// </summary>
public class SqliteDurabilityDescriptorTests
{
    private static SqliteEventStore Store(string databasePath) =>
        new(databasePath, DomainType.GetDomainTypes().EventTypes);

    [Fact]
    public void AFileBackedSqliteStore_IsDurable()
    {
        var descriptor = Store(Path.Combine(Path.GetTempPath(), $"sekiban-dcb-{Guid.NewGuid():N}.db")).DescribeStorage();

        Assert.Equal(StorageDurability.Durable, descriptor.Durability);
        Assert.Equal("Sqlite", descriptor.ProviderName);
    }

    [Theory]
    [InlineData(":memory:")]
    [InlineData("file:test?mode=memory&cache=shared")]
    public void AnInMemorySqliteStore_IsVolatile_DespiteBeingCalledSqlite(string databasePath)
    {
        var descriptor = Store(databasePath).DescribeStorage();

        // A production host pointed at an in-memory Sqlite is exactly the incident again, wearing a durable name.
        Assert.Equal(StorageDurability.Volatile, descriptor.Durability);
        Assert.Equal("Sqlite (in-memory)", descriptor.ProviderName);
    }
}

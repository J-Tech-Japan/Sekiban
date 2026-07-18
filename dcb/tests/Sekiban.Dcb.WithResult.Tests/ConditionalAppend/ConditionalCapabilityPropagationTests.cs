using Dcb.Domain;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 capability propagation across the surfaces a host actually resolves through: the per-service factory, the
///     resolver the Hybrid wrapper and the executor preflight use (<see cref="SekibanDcbCapabilityResolver" />), and the
///     composite intersection rule. A real provider store must report the single-event unique-key kind through each, and
///     the composite must fail closed the moment any participant cannot condition.
/// </summary>
public sealed class ConditionalCapabilityPropagationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sek-g16-prop-{Guid.NewGuid():N}.db");
    private readonly DcbDomainTypes _domain = DomainType.GetDomainTypes();

    private SqliteEventStore NewSqliteStore() => new(_dbPath, _domain.EventTypes);

    private CosmosDbEventStoreFactory NewCosmosFactory()
    {
        var options = new CosmosDbEventStoreOptions { EventsContainerName = "events", TagsContainerName = "tags" };
        var context = new CosmosDbContext(new InMemoryCosmosClient(), "db", null, options);
        return new CosmosDbEventStoreFactory(context, _domain.EventTypes, new DefaultCosmosContainerResolver(options));
    }

    [Fact]
    public void Factory_PropagatesTheKind()
    {
        var factory = NewCosmosFactory();
        Assert.True(((IWriteConditionCapabilityProvider)factory)
            .DescribeWriteConditions().Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    [Fact]
    public void Resolver_OverARealProviderStore_ReportsTheKind()
    {
        // This is the exact resolution the Hybrid wrapper and the executor preflight perform on the hot store; if it
        // reports the kind, a Hybrid wrapping this store forwards conditional appends to it.
        var descriptor = SekibanDcbCapabilityResolver.DescribeWriteConditions(NewSqliteStore(), "hot event store");
        Assert.True(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    [Fact]
    public void Composite_Intersection_SupportsTheKind_WhenEveryParticipantDoes()
    {
        var sqlite = SekibanDcbCapabilityResolver.DescribeWriteConditions(NewSqliteStore(), "a");
        var cosmosFactory = ((IWriteConditionCapabilityProvider)NewCosmosFactory()).DescribeWriteConditions();

        var composite = WriteConditionCapabilityDescriptor.Intersect("composite", new[] { sqlite, cosmosFactory });
        Assert.True(composite.Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    [Fact]
    public void Composite_Intersection_FailsClosed_WhenAnyParticipantCannotCondition()
    {
        var sqlite = SekibanDcbCapabilityResolver.DescribeWriteConditions(NewSqliteStore(), "a");
        var cannotCondition = WriteConditionCapabilityDescriptor.None("legacy");

        var composite = WriteConditionCapabilityDescriptor.Intersect("composite", new[] { sqlite, cannotCondition });
        Assert.False(composite.Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}

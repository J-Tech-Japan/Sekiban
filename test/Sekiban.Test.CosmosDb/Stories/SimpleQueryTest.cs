using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Queries.BasicClientFilters;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Command;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Infrastructure.Cosmos;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class SimpleQueryTest : TestBase<FeatureCheckDependency>
{
    private readonly IQueryExecutor _queryExecutor;
    public SimpleQueryTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new CosmosSekibanServiceProviderGenerator())
    {
        GetService<ICommandExecutor>();
        _queryExecutor = GetService<IQueryExecutor>();
    }

    [Fact]
    public async Task QueryExecuteAggregateListAsync()
    {

        await _queryExecutor.ExecuteAsync(
            new BasicClientQueryParameter(
                null,
                null,
                null,
                null,
                null,
                null,
                null));
    }

    [Fact]
    public async Task QueryExecuteAggregateAsync()
    {
        await _queryExecutor.ExecuteAsync(new ClientEmailExistsQuery.Parameter("foo@example.com"));
    }


    [Fact]
    public async Task QueryExecuteSingleProjectionListAsync()
    {

        await _queryExecutor.ExecuteAsync(new ClientNameHistoryProjectionQuery.Parameter(null, null, null, null, null));
    }
    [Fact]
    public async Task QueryExecuteSingleProjectionAsync()
    {

        await _queryExecutor.ExecuteAsync(new ClientNameHistoryProjectionCountQuery.Parameter(null, null));
    }
    [Fact]
    public async Task QueryExecuteMultipleProjectionListAsync()
    {
        await _queryExecutor.ExecuteAsync(
            new ClientLoyaltyPointQuery.Parameter(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
    }
    [Fact]
    public async Task QueryExecuteMultipleProjectionAsync()
    {
        await _queryExecutor.ExecuteAsync(
            new ClientLoyaltyPointMultiProjectionQuery.Parameter(null, ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.Points));
    }
}

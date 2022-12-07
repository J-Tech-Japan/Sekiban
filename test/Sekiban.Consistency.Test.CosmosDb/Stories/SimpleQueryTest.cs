using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Queries.BasicClientFilters;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.QueryModel;
using System.Threading.Tasks;
using Xunit;
namespace SampleProjectStoryXTest.Stories;

public class SimpleQueryTest : TestBase
{
    private readonly ICommandExecutor _commandExecutor;
    private readonly IQueryExecutor _queryExecutor;
    public SimpleQueryTest(
        SekibanTestFixture sekibanTestFixture,
        bool inMemory = false,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType = ServiceCollectionExtensions.MultiProjectionType.MemoryCache) : base(
        sekibanTestFixture,
        inMemory,
        multiProjectionType)
    {
        _commandExecutor = GetService<ICommandExecutor>();
        _queryExecutor = GetService<IQueryExecutor>();
    }

    [Fact]
    public async Task QueryExecuteAggregateListAsync()
    {

        var result = await _queryExecutor.ExecuteAsync(
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
        var result = await _queryExecutor.ExecuteAsync(
            new ClientEmailExistsQuery.QueryParameter("foo@example.com"));
    }


    [Fact]
    public async Task QueryExecuteSingleProjectionListAsync()
    {

        var result = await _queryExecutor.ExecuteAsync(
            new ClientNameHistoryProjectionQuery.Parameter(null, null, null, null, null));
    }
    [Fact]
    public async Task QueryExecuteSingleProjectionAsync()
    {

        var result = await _queryExecutor.ExecuteAsync(
            new ClientNameHistoryProjectionCountQuery.Parameter(null, null));
    }
    [Fact]
    public async Task QueryExecuteMultipleProjectionListAsync()
    {
        var result = await _queryExecutor.ExecuteAsync(
            new ClientLoyaltyPointQuery.QueryParameter(
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
        var result = await _queryExecutor.ExecuteAsync(
            new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(null, ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.Points));
    }
}

using Customer.Domain.Aggregates.Clients.Queries.BasicClientFilters;
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
    public async Task QueryTestAsync()
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
}

using Sekiban.Infrastructure.Cosmos;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosAggregateSubtypeTest : AggregateSubtypeTest
{

    public CosmosAggregateSubtypeTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new CosmosSekibanServiceProviderGenerator())
    {
    }
}

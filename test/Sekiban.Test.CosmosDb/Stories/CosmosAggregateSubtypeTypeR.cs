using Sekiban.Infrastructure.Cosmos;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosAggregateSubtypeTypeR : AggregateSubtypeTypeR
{

    public CosmosAggregateSubtypeTypeR(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new CosmosSekibanServiceProviderGenerator())
    {
    }
}

using Sekiban.Infrastructure.Cosmos;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosCustomerDbStoryBasic : CustomerDbStoryBasic
{

    public CosmosCustomerDbStoryBasic(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new CosmosSekibanServiceProviderGenerator())
    {
    }
}

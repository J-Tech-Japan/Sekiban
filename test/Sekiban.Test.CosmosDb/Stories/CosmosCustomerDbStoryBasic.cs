using Sekiban.Infrastructure.Cosmos;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosCustomerDbStoryBasic : Abstracts.CustomerDbStoryBasic
{

    public CosmosCustomerDbStoryBasic(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new CosmosSekibanServiceProviderGenerator())
    {
    }
}

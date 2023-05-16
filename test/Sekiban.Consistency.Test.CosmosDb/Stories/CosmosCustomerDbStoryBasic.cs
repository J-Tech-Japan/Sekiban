using Sekiban.Infrastructure.Cosmos;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories.CustomerDbStoryBasic;

public class CosmosCustomerDbStoryBasic : Abstracts.CustomerDbStoryBasic
{

    public CosmosCustomerDbStoryBasic(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new CosmosSekibanServiceProviderGenerator())
    {
    }
}

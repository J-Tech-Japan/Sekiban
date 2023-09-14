using Sekiban.Infrastructure.Cosmos;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosMultipleEventsInOneClassTest : MultipleEventsInOneClassTest
{
    public CosmosMultipleEventsInOneClassTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper output) : base(
        sekibanTestFixture,
        output,
        new CosmosSekibanServiceProviderGenerator())
    {
    }
}

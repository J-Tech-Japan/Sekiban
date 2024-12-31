using Sekiban.Test.Abstructs.Abstructs;
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

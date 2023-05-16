using SampleProjectStoryXTest.Stories.Abstracts;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class CosmosAggregateSubtypeTypeR : AggregateSubtypeTypeR
{

    public CosmosAggregateSubtypeTypeR(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new CosmosSekibanServiceProviderGenerator())
    {
    }
}

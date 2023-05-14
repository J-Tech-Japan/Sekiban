using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories.CustomerDbStoryBasic;

public class CustomerDbStoryBasicCosmos : CustomerDbStoryBasic
{

    public CustomerDbStoryBasicCosmos(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, DependencyHelper.DatabaseType.CosmosDb)
    {
    }
}

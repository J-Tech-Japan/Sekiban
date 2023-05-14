using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories.CustomerDbStoryBasic;

public class CustomerDbStoryBasicDynamo : CustomerDbStoryBasic
{
    public CustomerDbStoryBasicDynamo(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, DependencyHelper.DatabaseType.DynamoDb)
    {
    }
}
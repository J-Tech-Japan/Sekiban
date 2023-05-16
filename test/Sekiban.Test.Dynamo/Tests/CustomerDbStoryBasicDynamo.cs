using SampleProjectStoryXTest.Stories.Abstracts;
using SampleProjectStoryXTest.Stories.CustomerDbStoryBasic;
using Sekiban.Infrastructure.Dynamo;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class CustomerDbStoryBasicDynamo : CustomerDbStoryBasic
{
    public CustomerDbStoryBasicDynamo(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new DynamoSekibanServiceProviderGenerator())
    {
    }
}
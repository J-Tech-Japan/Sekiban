using SampleProjectStoryXTest.Stories.Abstracts;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class DynamoAggregateSubtypeTest : AggregateSubtypeTest
{

    public DynamoAggregateSubtypeTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new DynamoSekibanServiceProviderGenerator())
    {
    }
}

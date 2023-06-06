using Sekiban.Infrastructure.Dynamo;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class DynamoAggregateSubtypeTest : AggregateSubtypeTest
{

    public DynamoAggregateSubtypeTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new DynamoSekibanServiceProviderGenerator())
    {
    }
}

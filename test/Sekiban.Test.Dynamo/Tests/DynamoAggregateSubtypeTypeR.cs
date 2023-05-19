using Sekiban.Infrastructure.Dynamo;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class DynamoAggregateSubtypeTypeR : AggregateSubtypeTypeR
{

    public DynamoAggregateSubtypeTypeR(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new DynamoSekibanServiceProviderGenerator())
    {
    }
}

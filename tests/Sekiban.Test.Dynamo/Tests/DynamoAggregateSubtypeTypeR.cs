using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class DynamoAggregateSubtypeTypeR : AggregateSubtypeTypeR
{

    public DynamoAggregateSubtypeTypeR(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) :
        base(sekibanTestFixture, testOutputHelper, new DynamoSekibanServiceProviderGenerator())
    {
    }
}

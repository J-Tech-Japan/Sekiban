using Sekiban.Infrastructure.Dynamo;
using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class CustomerDbStoryBasicDynamo : CustomerDbStoryBasic
{
    public CustomerDbStoryBasicDynamo(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new DynamoSekibanServiceProviderGenerator())
    {
    }
}

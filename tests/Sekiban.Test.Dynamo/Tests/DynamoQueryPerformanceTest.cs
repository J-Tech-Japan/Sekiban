using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class DynamoQueryPerformanceTest : QueryPerformanceTestBase
{
    public DynamoQueryPerformanceTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new DynamoSekibanServiceProviderGenerator())
    {
    }
}

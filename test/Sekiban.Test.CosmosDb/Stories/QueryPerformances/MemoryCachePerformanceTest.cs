using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories.QueryPerformances;

public class MemoryCachePerformanceTest : QueryPerformanceTestBase
{
    public MemoryCachePerformanceTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper)
    {
    }
}

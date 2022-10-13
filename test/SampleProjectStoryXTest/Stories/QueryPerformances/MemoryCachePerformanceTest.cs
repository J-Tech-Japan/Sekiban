using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.TestHelpers.StoryTests;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories.QueryPerformances;

public class MemoryCachePerformanceTest : QueryPerformanceTestBase
{

    public MemoryCachePerformanceTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
    }
}

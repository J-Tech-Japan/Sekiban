using Sekiban.Core.Dependency;
using Sekiban.Testing.Story;
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

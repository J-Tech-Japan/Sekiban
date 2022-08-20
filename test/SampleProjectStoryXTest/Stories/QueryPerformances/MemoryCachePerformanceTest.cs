using Sekiban.EventSourcing;
using Sekiban.EventSourcing.TestHelpers;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories.QueryPerformances
{
    public class MemoryCachePerformanceTest : QueryPerformanceTestBase
    {

        public MemoryCachePerformanceTest(TestFixture testFixture, ITestOutputHelper testOutputHelper) : base(
            testFixture,
            testOutputHelper,
            ServiceCollectionExtensions.MultipleProjectionType.MemoryCache) { }
    }
}

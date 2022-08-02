using Sekiban.EventSourcing;
using Sekiban.EventSourcing.TestHelpers;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories.QueryPerformances;

public class SimpleQueryPerformanceTest : QueryPerformanceTestBase
{

    public SimpleQueryPerformanceTest(TestFixture testFixture, ITestOutputHelper testOutputHelper) : base(
        testFixture,
        testOutputHelper,
        ServiceCollectionExtensions.MultipleProjectionType.Simple) { }
}

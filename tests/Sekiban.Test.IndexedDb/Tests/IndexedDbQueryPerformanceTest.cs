using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.IndexedDb.Tests;

public class IndexedDbQueryPerformanceTest : QueryPerformanceTestBase
{
    public IndexedDbQueryPerformanceTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new IndexedDbSekibanServiceProviderGenerator())
    {
    }
}

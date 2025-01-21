using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.IndexedDb.Tests;

public class IndexedDbAggregateSubtypeTest : AggregateSubtypeTest
{

    public IndexedDbAggregateSubtypeTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new IndexedDbSekibanServiceProviderGenerator())
    {
    }
}

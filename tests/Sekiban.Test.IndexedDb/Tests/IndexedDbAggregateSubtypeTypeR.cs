using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.IndexedDb.Tests;

public class IndexedDbAggregateSubtypeTypeR : AggregateSubtypeTypeR
{

    public IndexedDbAggregateSubtypeTypeR(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new IndexedDbSekibanServiceProviderGenerator())
    {
    }
}

using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.IndexedDb.Tests;

public class IndexedDbMultipleEventsInOneClassTest : MultipleEventsInOneClassTest
{
    public IndexedDbMultipleEventsInOneClassTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper output) : base(
        sekibanTestFixture,
        output,
        new IndexedDbSekibanServiceProviderGenerator())
    {
    }
}

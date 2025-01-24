using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.IndexedDb.Tests;

public class IndexedDbMultiProjectionSnapshotTests : MultiProjectionSnapshotTests
{

    public IndexedDbMultiProjectionSnapshotTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new IndexedDbSekibanServiceProviderGenerator())
    {
    }
}

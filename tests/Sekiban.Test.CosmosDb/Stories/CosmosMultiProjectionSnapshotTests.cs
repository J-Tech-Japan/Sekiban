using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosMultiProjectionSnapshotTests : MultiProjectionSnapshotTests
{
    public CosmosMultiProjectionSnapshotTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper)
        : base(sekibanTestFixture, testOutputHelper, new CosmosSekibanServiceProviderGenerator())
    {
    }
}

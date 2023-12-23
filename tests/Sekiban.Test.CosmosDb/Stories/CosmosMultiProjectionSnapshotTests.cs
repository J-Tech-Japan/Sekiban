using Sekiban.Infrastructure.Cosmos;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosMultiProjectionSnapshotTests : MultiProjectionSnapshotTests
{
    public CosmosMultiProjectionSnapshotTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new CosmosSekibanServiceProviderGenerator())
    {
    }
}

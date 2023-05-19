using Sekiban.Infrastructure.Cosmos;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosMultiProjectionSnapshotTests : MultiProjectionSnapshotTests
{
    public CosmosMultiProjectionSnapshotTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new CosmosSekibanServiceProviderGenerator())
    {
    }
}

using Sekiban.Infrastructure.Dynamo;
using Sekiban.Test.CosmosDb.Stories;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class DynamoMultiProjectionSnapshotTests : MultiProjectionSnapshotTests
{

    public DynamoMultiProjectionSnapshotTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture, testOutputHelper, new DynamoSekibanServiceProviderGenerator())
    {
    }
}

using Sekiban.Infrastructure.Dynamo;
using Sekiban.Test.CosmosDb.Stories.Abstracts;
using Xunit.Abstractions;
namespace Sekiban.Test.Dynamo.Tests;

public class DynamoMultiTenantDocumentTests : MultiTenantDocumentTests
{

    public DynamoMultiTenantDocumentTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper output) : base(
        sekibanTestFixture,
        output,
        new DynamoSekibanServiceProviderGenerator())
    {
    }
}

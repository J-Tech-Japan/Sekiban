using Sekiban.Test.Abstructs.Abstructs;
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

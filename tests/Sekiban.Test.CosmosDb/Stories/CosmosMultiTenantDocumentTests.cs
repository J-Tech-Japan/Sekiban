using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class CosmosMultiTenantDocumentTests : MultiTenantDocumentTests
{

    public CosmosMultiTenantDocumentTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper output) : base(
        sekibanTestFixture,
        output,
        new CosmosSekibanServiceProviderGenerator())
    {
    }
}

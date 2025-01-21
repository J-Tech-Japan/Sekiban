using Sekiban.Test.Abstructs.Abstructs;
using Xunit.Abstractions;
namespace Sekiban.Test.IndexedDb.Tests;

public class IndexedDbMultiTenantDocumentTests : MultiTenantDocumentTests
{

    public IndexedDbMultiTenantDocumentTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper output) : base(
        sekibanTestFixture,
        output,
        new IndexedDbSekibanServiceProviderGenerator())
    {
    }
}

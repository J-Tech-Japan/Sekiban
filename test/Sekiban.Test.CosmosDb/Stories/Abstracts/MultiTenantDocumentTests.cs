using MultiTenant.Domain.Aggregates;
using Sekiban.Testing.Story;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories.Abstracts;

public abstract class MultiTenantDocumentTests : TestBase<MultiTenantDependency>
{
    public MultiTenantDocumentTests(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper output,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, output, providerGenerator)
    {
    }

    [Fact]
    public void TenantExecTest()
    {

    }
}

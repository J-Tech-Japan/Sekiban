using MultiTenant.Domain.Aggregates;
using MultiTenant.Domain.Aggregates.Clients.Commands;
using MultiTenant.Domain.Aggregates.Clients.Queries;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Testing.Story;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories.Abstracts;

public abstract class MultiTenantDocumentTests : TestBase<MultiTenantDependency>
{
    private const string clientName1 = "Tenant Name 1";
    private const string clientName2 = "Tenant Name 2";
    private const string tenantId1 = "tenant-1";
    private const string tenantId2 = "tenant-2";

    public MultiTenantDocumentTests(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper output,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, output, providerGenerator)
    {
    }

    [Fact]
    public async Task TenantExecTest()
    {
        RemoveAllFromDefaultAndDissolvable();
        var responseTenant1 = await commandExecutor.ExecCommandAsync(new CreateClient { Name = clientName1, TenantId = tenantId1 });
        var responseTenant2 = await commandExecutor.ExecCommandAsync(new CreateClient { Name = clientName2, TenantId = tenantId2 });
        Assert.Equal(1, responseTenant1.Version);
        Assert.Equal(1, responseTenant2.Version);

        var queryResult = await queryExecutor.ExecuteAsync(new ClientListQuery.Parameter(tenantId1));
        Assert.Single(queryResult.Items);
        Assert.Equal(clientName1, queryResult.Items.First().Name);
        var queryResult2 = await queryExecutor.ExecuteAsync(new ClientListQuery.Parameter(tenantId2));
        Assert.Single(queryResult2.Items);
        Assert.Equal(clientName2, queryResult2.Items.First().Name);
        var queryResultAll = await queryExecutor.ExecuteAsync(new ClientListQuery.Parameter(IMultiProjectionService.ProjectionAllRootPartitions));
        Assert.Equal(2, queryResultAll.Items.Count());
    }
}

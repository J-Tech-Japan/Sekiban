using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Projections;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing;
using System.Linq;
using Xunit;
namespace FeatureCheck.Test.QueryTests;

public class GeneralQuerySampleSpec : UnifiedTest<FeatureCheckDependency>
{
    [Fact]
    public void GeneralQueryBasicTest()
    {
        var branchId = GivenCommand(new CreateBranch("test branch"));
        GivenCommand(new CreateClient(branchId, "Test Client1", "testclient1@example.com"));
        GivenCommand(new CreateClient(branchId, "Test Client2", "testclient2@example.com"));
        GivenCommand(new CreateClient(branchId, "Test Client3", "testclient3@example.co.jp"));
        GivenCommand(new CreateClient(branchId, "Test Client4", "testclient4@example.jp"));

        ThenGetQueryResponse(new GeneralQuerySample.Parameter("test"), response => Assert.Equal(4, response.Count));
        ThenGetQueryResponse(new GeneralQuerySample.Parameter("example.com"), response => Assert.Equal(2, response.Count));

        ThenGetQueryResponse(new GeneralListQuerySample.Parameter("test"), response => Assert.Equal(4, response.Items.Count()));
        ThenGetQueryResponse(new GeneralListQuerySample.Parameter("example.com"), response => Assert.Equal(2, response.Items.Count()));
    }
}

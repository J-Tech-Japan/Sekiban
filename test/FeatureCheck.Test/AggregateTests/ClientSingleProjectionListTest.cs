using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Testing;
using System.Collections.Generic;
using System.Linq;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class ClientSingleProjectionListTest : UnifiedTest<FeatureCheckDependency>
{
    [Fact]
    public void Test()
    {
        var branchId = RunCommand(new CreateBranch("test"));
        var client1Id = RunCommand(new CreateClient(branchId, "John Smith", "john@example.com"));
        var datetime1 = GetLatestEvents().FirstOrDefault()!.TimeStamp;
        RunCommand(new ChangeClientName(client1Id, "John Doe"));
        var datetime2 = GetLatestEvents().FirstOrDefault()!.TimeStamp;
        var client2Id = RunCommand(new CreateClient(branchId, "test name", "test@example.com"));
        var datetime3 = GetLatestEvents().FirstOrDefault()!.TimeStamp;

        ThenQueryResponseIs(
            new ClientNameHistoryProjectionQuery.Parameter(null, null, null, null, null),
            new ListQueryResult<ClientNameHistoryProjectionQuery.Response>(
                3,
                null,
                null,
                null,
                new List<ClientNameHistoryProjectionQuery.Response>
                {
                    new(branchId, client1Id, "John Smith", "john@example.com", datetime1),
                    new(branchId, client1Id, "John Doe", "john@example.com", datetime2),
                    new(branchId, client2Id, "test name", "test@example.com", datetime3)
                }
            ));
    }
}

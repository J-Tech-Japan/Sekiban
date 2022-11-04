using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Shared;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Testing.Projection;
using System.Collections.Generic;
using System.Linq;
using Xunit;
namespace Customer.Test.AggregateTests;

public class ClientSingleProjectionListTest : SingleProjectionListTestBase<Client, ClientNameHistorySingleProjection,
    ClientNameHistorySingleProjection.PayloadDefinition, CustomerDependency>
{
    [Fact]
    public void Test()
    {
        var branchId = RunCreateCommand(new CreateBranch("test"));
        var client1Id = RunCreateCommand(new CreateClient(branchId, "John Smith", "john@example.com"));
        var datetime1 = GetLatestEvents().FirstOrDefault()!.TimeStamp;
        RunChangeCommand(new ChangeClientName(client1Id, "John Doe"));
        var datetime2 = GetLatestEvents().FirstOrDefault()!.TimeStamp;
        var client2Id = RunCreateCommand(new CreateClient(branchId, "test name", "test@example.com"));
        var datetime3 = GetLatestEvents().FirstOrDefault()!.TimeStamp;
        WhenProjection()
            .ThenGetListQueryTest<ClientNameHistoryProjectionQuery, ClientNameHistoryProjectionQuery.Parameter,
                ClientNameHistoryProjectionQuery.Response>(
                test => test.WhenParam(new ClientNameHistoryProjectionQuery.Parameter(null, null, null, null, null))
                    .ThenResponseIs(
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
                        ))
            );
    }
}

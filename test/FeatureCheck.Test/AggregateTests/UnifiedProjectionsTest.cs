using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.Queries;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Customer.Domain.Shared;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Testing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace Customer.Test.AggregateTests;

public class UnifiedProjectionsTest : UnifiedTest<CustomerDependency>
{
    private readonly string branchName = "BranchName";
    private readonly string clientEmail = "test@exmple.com";
    private readonly string clientName = "CreateClient Name";
    private readonly string clientName2 = "CreateClient Name2";

    private Guid _branchId = Guid.Empty;
    private Guid _clientId = Guid.Empty;
    private DateTime dateNameSet = DateTime.Now;

    [Fact]
    public void Test()
    {
        _branchId = RunCommand(new CreateBranch(branchName));
        _clientId = RunCommand(new CreateClient(_branchId, clientName, clientEmail));
        GetLatestEvents()
            .ToList()
            .ForEach(
                m =>
                {
                    if (m.GetPayload() is ClientCreated created)
                    {
                        dateNameSet = m.TimeStamp;
                    }
                });
        ThenMultiProjectionPayloadIs(
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultiProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultiProjection.ProjectedRecord(
                            _branchId,
                            branchName,
                            _clientId,
                            clientName,
                            0))))
            .ThenMultiProjectionQueryResponseIs<ClientLoyaltyPointMultiProjection,
                ClientLoyaltyPointMultiProjectionQuery,
                ClientLoyaltyPointMultiProjectionQuery.QueryParameter, ClientLoyaltyPointMultiProjection>(
                new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(
                    null,
                    ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultiProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultiProjection.ProjectedRecord(
                            _branchId,
                            branchName,
                            _clientId,
                            clientName,
                            0))));
    }

    [Fact]
    public void TestAggregateQuery()
    {
        GivenScenario(Test)
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, BranchExistsQuery.Output>(
                new BranchExistsQuery.QueryParameter(_branchId),
                new BranchExistsQuery.Output(true))
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, BranchExistsQuery.Output>(
                new BranchExistsQuery.QueryParameter(Guid.NewGuid()),
                new BranchExistsQuery.Output(false));
    }

    [Fact]
    public void TestSingleProjection()
    {
        GivenScenario(Test)
            .ThenSingleProjectionListQueryResponseIs<ClientNameHistoryProjection, ClientNameHistoryProjectionQuery,
                ClientNameHistoryProjectionQuery.Parameter, ClientNameHistoryProjectionQuery.Response>(
                new ClientNameHistoryProjectionQuery.Parameter(null, null, null, null, null),
                new ListQueryResult<ClientNameHistoryProjectionQuery.Response>(
                    1,
                    null,
                    null,
                    null,
                    new List<ClientNameHistoryProjectionQuery.Response>
                    {
                        new(_branchId, _clientId, clientName, clientEmail, dateNameSet)
                    }));
    }


    [Fact]
    public void WhenChangeName()
    {
        GivenScenario(Test);
        RunCommand(new ChangeClientName(_clientId, clientName2));
        ThenMultiProjectionQueryResponseIs<ClientLoyaltyPointMultiProjection, ClientLoyaltyPointMultiProjectionQuery,
            ClientLoyaltyPointMultiProjectionQuery.QueryParameter, ClientLoyaltyPointMultiProjection>(
            new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(
                null,
                ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
            new ClientLoyaltyPointMultiProjection(
                ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty.Add(
                    new ClientLoyaltyPointMultiProjection.ProjectedBranch(_branchId, branchName)),
                ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty.Add(
                    new ClientLoyaltyPointMultiProjection.ProjectedRecord(
                        _branchId,
                        branchName,
                        _clientId,
                        clientName2,
                        0))));
    }
}

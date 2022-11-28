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

public class UnifiedProjectionsTest : UnifiedTestBase<CustomerDependency>
{
    private readonly string branchName = "BranchName";
    private readonly string clientEmail = "test@exmple.com";
    private readonly string clientName = "Client Name";
    private readonly string clientName2 = "Client Name2";

    private Guid _branchId = Guid.Empty;
    private Guid _clientId = Guid.Empty;
    private DateTime dateNameSet = DateTime.Now;

    [Fact]
    public void Test()
    {
        _branchId = RunCreateCommand(new CreateBranch(branchName));
        _clientId = RunCreateCommand(new Client(_branchId, clientName, clientEmail));
        GetLatestEvents()
            .ToList()
            .ForEach(
                m =>
                {
                    if (m.GetPayload() is ClientCreated created) { dateNameSet = m.TimeStamp; }
                });
        ThenMultiProjectionPayloadIs(
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultiProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultiProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName, 0))))
            .ThenMultiProjectionQueryResponseIs<ClientLoyaltyPointMultiProjection, ClientLoyaltyPointMultiProjectionQuery,
                ClientLoyaltyPointMultiProjectionQuery.QueryParameter, ClientLoyaltyPointMultiProjection>(
                new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(
                    null,
                    ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultiProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultiProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName, 0))));
    }

    [Fact]
    public void TestAggregateQuery()
    {
        GivenScenario(Test)
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                new BranchExistsQuery.QueryParameter(_branchId),
                true)
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                new BranchExistsQuery.QueryParameter(Guid.NewGuid()),
                false);
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
        RunChangeCommand(new ChangeClientName(_clientId, clientName2));
        ThenMultiProjectionQueryResponseIs<ClientLoyaltyPointMultiProjection, ClientLoyaltyPointMultiProjectionQuery,
            ClientLoyaltyPointMultiProjectionQuery.QueryParameter, ClientLoyaltyPointMultiProjection>(
            new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(
                null,
                ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
            new ClientLoyaltyPointMultiProjection(
                ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty.Add(
                    new ClientLoyaltyPointMultiProjection.ProjectedBranch(_branchId, branchName)),
                ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty.Add(
                    new ClientLoyaltyPointMultiProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName2, 0))));
    }
}

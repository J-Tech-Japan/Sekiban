using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.Queries;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Customer.Domain.Shared;
using Sekiban.Testing.Projection;
using System;
using System.Collections.Immutable;
using Xunit;
namespace Customer.Test.AggregateTests;

public class SimpleUnifiedProjectionTest : MultiProjectionsAndQueriesTestBase<CustomerDependency>
{
    public Guid branchId = Guid.NewGuid();
    public string branchName = "test";
    [Fact]
    public void CreateBranch()
    {

        RunCreateCommand(new CreateBranch(branchName), branchId);
        GetMultiProjectionQueryTest<ClientLoyaltyPointMultiProjection, ClientLoyaltyPointMultiProjection.PayloadDefinition,
                ClientLoyaltyPointMultipleMultiProjectionQuery, ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter,
                ClientLoyaltyPointMultiProjection.PayloadDefinition>(
                test => test.WhenParam(
                        new ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter(
                            branchId,
                            ClientLoyaltyPointMultipleMultiProjectionQuery.QuerySortKeys.ClientName))
                    .ThenResponseIs(
                        new ClientLoyaltyPointMultiProjection.PayloadDefinition(
                            ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                                .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                            ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty)
                    )
            )
            .GetAggregateQueryTest<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                test => test.WhenParam(
                        new BranchExistsQuery.QueryParameter(branchId))
                    .ThenResponseIs(true)
                    .WhenParam(new BranchExistsQuery.QueryParameter(Guid.NewGuid()))
                    .ThenResponseIs(false)
            )
            .GetMultiProjectionTest<ClientLoyaltyPointMultiProjection, ClientLoyaltyPointMultiProjection.PayloadDefinition>(
                test => test.WhenProjection()
                    .ThenPayloadIs(
                        new ClientLoyaltyPointMultiProjection.PayloadDefinition(
                            ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                                .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                            ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty)
                    )
            )
            .GetAggregateListProjectionTest<Client>(
                test => test.WhenProjection()
                    .ThenGetPayload(payload => Assert.Empty(payload.List))
            )
            .GetSingleProjectionListProjectionTest<Client, ClientNameHistoryMultiProjection, ClientNameHistoryMultiProjection.PayloadDefinition>(
                test => test.WhenProjection()
                    .ThenGetPayload(payload => Assert.Empty(payload.List))
            );
    }
}

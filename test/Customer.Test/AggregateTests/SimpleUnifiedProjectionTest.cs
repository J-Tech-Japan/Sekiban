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

public class SimpleUnifiedProjectionTest : MultipleProjectionsAndQueriesTestBase<CustomerDependency>
{
    public Guid branchId = Guid.NewGuid();
    public string branchName = "test";
    [Fact]
    public void CreateBranch()
    {

        RunCreateCommand(new CreateBranch(branchName), branchId);
        GetMultiProjectionQueryTest<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.PayloadDefinition,
                ClientLoyaltyPointMultipleMultiProjectionQuery, ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter,
                ClientLoyaltyPointMultipleProjection.PayloadDefinition>(
                test => test.WhenParam(
                        new ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter(
                            branchId,
                            ClientLoyaltyPointMultipleMultiProjectionQuery.QuerySortKeys.ClientName))
                    .ThenResponseIs(
                        new ClientLoyaltyPointMultipleProjection.PayloadDefinition(
                            ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty
                                .Add(new ClientLoyaltyPointMultipleProjection.ProjectedBranch(branchId, branchName)),
                            ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty)
                    )
            )
            .GetAggregateQueryTest<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                test => test.WhenParam(
                        new BranchExistsQuery.QueryParameter(branchId))
                    .ThenResponseIs(true)
                    .WhenParam(new BranchExistsQuery.QueryParameter(Guid.NewGuid()))
                    .ThenResponseIs(false)
            )
            .GetMultipleProjectionTest<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.PayloadDefinition>(
                test => test.WhenProjection()
                    .ThenPayloadIs(
                        new ClientLoyaltyPointMultipleProjection.PayloadDefinition(
                            ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty
                                .Add(new ClientLoyaltyPointMultipleProjection.ProjectedBranch(branchId, branchName)),
                            ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty)
                    )
            )
            .GetAggregateListProjectionTest<Client>(
                test => test.WhenProjection()
                    .ThenGetPayload(payload => Assert.Empty(payload.List))
            )
            .GetSingleProjectionListProjectionTest<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>(
                test => test.WhenProjection()
                    .ThenGetPayload(payload => Assert.Empty(payload.List))
            );
    }
}

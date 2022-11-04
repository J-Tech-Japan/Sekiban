using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.Queries;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Customer.Domain.Shared;
using Sekiban.Testing.Projection;
using System;
using System.Collections.Immutable;
using Xunit;
namespace Customer.Test.AggregateTests;

public class SimpleUnifiedProjectionTest : UnifiedTestBase<CustomerDependency>
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
            .GetSingleProjectionListProjectionTest<Client, ClientNameHistorySingleProjection, ClientNameHistorySingleProjection.PayloadDefinition>(
                test => test.WhenProjection()
                    .ThenGetPayload(payload => Assert.Empty(payload.List))
            );
    }
    [Fact]
    public void ClientTest()
    {
        RunCreateCommand(new CreateBranch(branchName), branchId);
        GetAggregateTest<Client>(
            test => test.WhenCreate(new CreateClient(branchId, "test", "test@example.com"))
                .WhenChange(new ChangeClientName(test.GetAggregateId(), "Test2") { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }
    [Fact]
    public void ChangeClientNameTest()
    {
        RunCreateCommand(new CreateBranch(branchName), branchId);
        var clientId = RunCreateCommand(new CreateClient(branchId, "test", "test@example.com"));
        GetAggregateTestFoeExistingAggregate<Client>(
            clientId,
            test => test.WhenChange(new ChangeClientName(clientId, "Test2") { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }

    [Fact]
    public void ChangeClientNameTest2()
    {
        RunCreateCommand(new CreateBranch(branchName), branchId);
        var clientId = Guid.Empty;
        GetAggregateTest<Client>(
            test =>
            {
                test.WhenCreate(new CreateClient(branchId, "test", "test@example.com"))
                    .ThenNotThrowsAnException();
                clientId = test.GetAggregateId();
            });
        GetAggregateTestFoeExistingAggregate<Client>(
            clientId,
            test => test.WhenChange(new ChangeClientName(clientId, "Test2") { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }
    [Fact]
    public void TestMediatR()
    {
        RunCreateCommand(new CreateBranch(branchName), branchId);
        var clientId = Guid.Empty;
        GetAggregateTest<Client>(
            test =>
            {
                test.WhenCreate(new CreateClient(branchId, "test", "test@example.com"))
                    .ThenNotThrowsAnException();
                clientId = test.GetAggregateId();
            });
        // Create Loyalty Point Runs automatically with event Created Client
        GetAggregateTestFoeExistingAggregate<LoyaltyPoint>(
            clientId,
            test => test.WhenChange(
                    new AddLoyaltyPoint(clientId, new DateTime(2022, 11, 1), LoyaltyPointReceiveTypeKeys.CreditcardUsage, 100, string.Empty)
                        { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }
}

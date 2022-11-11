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
using Sekiban.Testing;
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
        ThenGetMultiProjectionQueryTest<ClientLoyaltyPointMultiProjection,
                ClientLoyaltyPointMultipleMultiProjectionQuery, ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter,
                ClientLoyaltyPointMultiProjection>(
                test => test.WhenParam(
                        new ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter(
                            branchId,
                            ClientLoyaltyPointMultipleMultiProjectionQuery.QuerySortKeys.ClientName))
                    .ThenResponseIs(
                        new ClientLoyaltyPointMultiProjection(
                            ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                                .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                            ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty)
                    )
            )
            .ThenGetAggregateQueryTest<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                test => test.WhenParam(
                        new BranchExistsQuery.QueryParameter(branchId))
                    .ThenResponseIs(true)
                    .WhenParam(new BranchExistsQuery.QueryParameter(Guid.NewGuid()))
                    .ThenResponseIs(false)
            )
            .ThenGetMultiProjectionTest<ClientLoyaltyPointMultiProjection>(
                test => test.WhenProjection()
                    .ThenPayloadIs(
                        new ClientLoyaltyPointMultiProjection(
                            ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                                .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                            ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty)
                    )
            )
            .ThenGetAggregateListProjectionTest<Client>(
                test => test.WhenProjection()
                    .ThenGetPayload(payload => Assert.Empty(payload.List))
            )
            .ThenGetSingleProjectionListProjectionTest<ClientNameHistoryProjection>(
                test => test.WhenProjection()
                    .ThenGetPayload(payload => Assert.Empty(payload.List))
            );
    }
    [Fact]
    public void CreateBranchWithoutAction()
    {
        RunCreateCommand(new CreateBranch(branchName), branchId);
        GetMultiProjectionQueryTest<ClientLoyaltyPointMultiProjection,
                ClientLoyaltyPointMultipleMultiProjectionQuery, ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter,
                ClientLoyaltyPointMultiProjection>()
            .WhenParam(
                new ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter(
                    branchId,
                    ClientLoyaltyPointMultipleMultiProjectionQuery.QuerySortKeys.ClientName))
            .ThenResponseIs(
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                        .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty)
            );
        GetAggregateQueryTest<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>()
            .WhenParam(
                new BranchExistsQuery.QueryParameter(branchId))
            .ThenResponseIs(true)
            .WhenParam(new BranchExistsQuery.QueryParameter(Guid.NewGuid()))
            .ThenResponseIs(false);
        GetMultiProjectionTest<ClientLoyaltyPointMultiProjection>()
            .WhenProjection()
            .ThenPayloadIs(
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                        .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty)
            );
        GetAggregateListProjectionTest<Client>()
            .WhenProjection()
            .ThenGetPayload(payload => Assert.Empty(payload.List));

        GetSingleProjectionListProjectionTest<ClientNameHistoryProjection>()
            .WhenProjection()
            .ThenGetPayload(payload => Assert.Empty(payload.List));
    }

    [Fact]
    public void ClientTest()
    {
        RunCreateCommand(new CreateBranch(branchName), branchId);
        ThenGetAggregateTest<Client>(
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
        ThenGetAggregateTest<Client>(
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
        ThenGetAggregateTest<Client>(
            test =>
            {
                test.WhenCreate(new CreateClient(branchId, "test", "test@example.com"))
                    .ThenNotThrowsAnException();
                clientId = test.GetAggregateId();
            });
        ThenGetAggregateTest<Client>(
            clientId,
            test => test.WhenChange(new ChangeClientName(clientId, "Test2") { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
        var clientTest = GetAggregateTest<Client>(clientId);
        clientTest.WhenChange(new ChangeClientName(clientId, "Test3") { ReferenceVersion = clientTest.GetCurrentVersion() });
    }
    [Fact]
    public void TestMediatR()
    {
        RunCreateCommand(new CreateBranch(branchName), branchId);
        var test = GetAggregateTest<Client>();
        test.WhenCreateWithPublish(new CreateClient(branchId, "test", "test@example.com"))
            .ThenNotThrowsAnException();
        var clientId = test.GetAggregateId();
        // Create Loyalty Point Runs automatically with event Created Client
        ThenGetAggregateTest<LoyaltyPoint>(
            clientId,
            test => test.WhenChange(
                    new AddLoyaltyPoint(clientId, new DateTime(2022, 11, 1), LoyaltyPointReceiveTypeKeys.CreditcardUsage, 100, string.Empty)
                        { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }
}

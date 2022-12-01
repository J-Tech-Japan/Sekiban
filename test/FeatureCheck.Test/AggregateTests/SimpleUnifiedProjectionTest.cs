using System;
using System.Collections.Immutable;
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
using Xunit;

namespace Customer.Test.AggregateTests;

public class SimpleUnifiedProjectionTest : UnifiedTestBase<CustomerDependency>
{
    public Guid branchId = Guid.NewGuid();
    public string branchName = "test";

    [Fact]
    public void CreateBranch()
    {
        RunCommand(new CreateBranch(branchName), branchId);

        ThenMultiProjectionQueryResponseIs<ClientLoyaltyPointMultiProjection,
                ClientLoyaltyPointMultiProjectionQuery, ClientLoyaltyPointMultiProjectionQuery.QueryParameter,
                ClientLoyaltyPointMultiProjection>(
                new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(
                    branchId,
                    ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                        .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty))
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                new BranchExistsQuery.QueryParameter(branchId),
                true)
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                new BranchExistsQuery.QueryParameter(Guid.NewGuid()),
                false)
            .ThenMultiProjectionPayloadIs(
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                        .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty))
            .ThenGetAggregateListProjectionPayload<Client>(payload => Assert.Empty(payload.List))
            .ThenGetSingleProjectionListPayload<ClientNameHistoryProjection>(payload => Assert.Empty(payload.List));
    }

    [Fact]
    public void CreateBranchWithoutAction()
    {
        RunCommand(new CreateBranch(branchName), branchId);
        ThenMultiProjectionQueryResponseIs<ClientLoyaltyPointMultiProjection,
                ClientLoyaltyPointMultiProjectionQuery, ClientLoyaltyPointMultiProjectionQuery.QueryParameter,
                ClientLoyaltyPointMultiProjection>(
                new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(
                    branchId,
                    ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                        .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty))
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                new BranchExistsQuery.QueryParameter(branchId),
                true)
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>(
                new BranchExistsQuery.QueryParameter(Guid.NewGuid()),
                false)
            .ThenMultiProjectionPayloadIs(
                new ClientLoyaltyPointMultiProjection(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                        .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty))
            .ThenGetAggregateListProjectionPayload<Client>(payload => Assert.Empty(payload.List))
            .ThenGetSingleProjectionListPayload<ClientNameHistoryProjection>(payload => Assert.Empty(payload.List));
    }

    [Fact]
    public void ClientTest()
    {
        RunCommand(new CreateBranch(branchName), branchId);
        ThenGetAggregateTest<Client>(
            test => test.WhenCommand(new CreateClient(branchId, "test", "test@example.com"))
                .WhenCommand(new ChangeClientName(test.GetAggregateId(), "Test2")
                    { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }

    [Fact]
    public void ChangeClientNameTest()
    {
        RunCommand(new CreateBranch(branchName), branchId);
        var clientId = RunCommand(new CreateClient(branchId, "test", "test@example.com"));
        ThenGetAggregateTest<Client>(
            clientId,
            test => test.WhenCommand(new ChangeClientName(clientId, "Test2")
                    { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }

    [Fact]
    public void ChangeClientNameTest2()
    {
        RunCommand(new CreateBranch(branchName), branchId);
        var clientId = Guid.Empty;
        ThenGetAggregateTest<Client>(
            test =>
            {
                test.WhenCommand(new CreateClient(branchId, "test", "test@example.com"))
                    .ThenNotThrowsAnException();
                clientId = test.GetAggregateId();
            });
        ThenGetAggregateTest<Client>(
            clientId,
            test => test.WhenCommand(new ChangeClientName(clientId, "Test2")
                    { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
        var clientTest = GetAggregateTest<Client>(clientId);
        clientTest.WhenCommand(new ChangeClientName(clientId, "Test3")
            { ReferenceVersion = clientTest.GetCurrentVersion() });
    }

    [Fact]
    public void TestMediatR()
    {
        RunCommand(new CreateBranch(branchName), branchId);
        var test = GetAggregateTest<Client>();
        test.WhenCommandWithPublish(new CreateClient(branchId, "test", "test@example.com"))
            .ThenNotThrowsAnException();
        var clientId = test.GetAggregateId();
        // Create Loyalty Point Runs automatically with event Created CreateClient
        ThenGetAggregateTest<LoyaltyPoint>(
            clientId,
            test => test.WhenCommand(
                    new AddLoyaltyPoint(clientId, new DateTime(2022, 11, 1),
                            LoyaltyPointReceiveTypeKeys.CreditcardUsage, 100, string.Empty)
                        { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }
}

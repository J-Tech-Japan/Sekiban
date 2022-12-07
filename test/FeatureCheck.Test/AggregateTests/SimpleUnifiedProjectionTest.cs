using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing;
using System;
using System.Collections.Immutable;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class SimpleUnifiedProjectionTest : UnifiedTest<CustomerDependency>
{
    public Guid branchId = Guid.NewGuid();
    public string branchName = "test";

    [Fact]
    public void CreateBranch()
    {
        RunCommand(new CreateBranch(branchName), branchId);

        ThenMultiProjectionQueryResponseIs<ClientLoyaltyPointMultiProjection,
                ClientLoyaltyPointMultiProjectionQuery, ClientLoyaltyPointMultiProjectionQuery.QueryParameter,
                ClientLoyaltyPointMultiProjectionQuery.Response>(
                new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(
                    branchId,
                    ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
                new ClientLoyaltyPointMultiProjectionQuery.Response(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                        .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty))
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, BranchExistsQuery.Response>(
                new BranchExistsQuery.QueryParameter(branchId),
                new BranchExistsQuery.Response(true))
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, BranchExistsQuery.Response>(
                new BranchExistsQuery.QueryParameter(Guid.NewGuid()),
                new BranchExistsQuery.Response(false))
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
                ClientLoyaltyPointMultiProjectionQuery.Response>(
                new ClientLoyaltyPointMultiProjectionQuery.QueryParameter(
                    branchId,
                    ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
                new ClientLoyaltyPointMultiProjectionQuery.Response(
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedBranch>.Empty
                        .Add(new ClientLoyaltyPointMultiProjection.ProjectedBranch(branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultiProjection.ProjectedRecord>.Empty))
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, BranchExistsQuery.Response>(
                new BranchExistsQuery.QueryParameter(branchId),
                new BranchExistsQuery.Response(true))
            .ThenAggregateQueryResponseIs<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, BranchExistsQuery.Response>(
                new BranchExistsQuery.QueryParameter(Guid.NewGuid()),
                new BranchExistsQuery.Response(false))
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
                .WhenCommand(
                    new ChangeClientName(test.GetAggregateId(), "Test2")
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
            test => test.WhenCommand(
                    new ChangeClientName(clientId, "Test2")
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
            test => test.WhenCommand(
                    new ChangeClientName(clientId, "Test2")
                        { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
        var clientTest = GetAggregateTest<Client>(clientId);
        clientTest.WhenCommand(
            new ChangeClientName(clientId, "Test3")
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
                    new AddLoyaltyPoint(
                            clientId,
                            new DateTime(2022, 11, 1),
                            LoyaltyPointReceiveTypeKeys.CreditcardUsage,
                            100,
                            string.Empty)
                        { ReferenceVersion = test.GetCurrentVersion() })
                .ThenNotThrowsAnException()
        );
    }
}

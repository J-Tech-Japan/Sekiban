using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Testing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class UnifiedProjectionsTest : UnifiedTest<FeatureCheckDependency>
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
            .ThenQueryResponseIs(
                new ClientLoyaltyPointMultiProjectionQuery.Parameter(
                    null,
                    ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
                new ClientLoyaltyPointMultiProjectionQuery.Response(
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
            .ThenQueryResponseIs(
                new BranchExistsQuery.Parameter(_branchId),
                new BranchExistsQuery.Response(true))
            .ThenQueryResponseIs(
                new BranchExistsQuery.Parameter(Guid.NewGuid()),
                new BranchExistsQuery.Response(false));
    }
    [Fact]
    public void TestAggregateQuery2()
    {
        GivenScenario(Test)
            .ThenQueryResponseIs(
                new BranchExistsQuery.Parameter(_branchId),
                new BranchExistsQuery.Response(true))
            .ThenQueryResponseIs(
                new BranchExistsQuery.Parameter(Guid.NewGuid()),
                new BranchExistsQuery.Response(false));
    }
    [Fact]
    public void TestSingleProjection()
    {
        GivenScenario(Test)
            .ThenQueryResponseIs(
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
    public void TestSingleProjection2()
    {
        GivenScenario(Test)
            .ThenQueryResponseIs(
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
        ThenQueryResponseIs(
            new ClientLoyaltyPointMultiProjectionQuery.Parameter(
                null,
                ClientLoyaltyPointMultiProjectionQuery.QuerySortKeys.ClientName),
            new ClientLoyaltyPointMultiProjectionQuery.Response(
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

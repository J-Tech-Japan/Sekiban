using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.QueryFilters;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Projections.ClientLoyaltyPointLists;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Customer.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Testing.Projection;
using Sekiban.Testing.QueryFilter;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace Customer.Test.AggregateTests;

public class UnifiedProjectionsTest : MultipleProjectionsAndQueriesTestBase<CustomerDependency>
{

    private readonly AggregateQueryFilterTestChecker<Branch, BranchContents, BranchExistsQueryFilter, BranchExistsQueryFilter.QueryParameter, bool>
        _branchExistsQueryFilterChecker = new();

    private readonly AggregateListProjectionTestBase<Branch, BranchContents, CustomerDependency> _branchListProjection;
    private readonly
        MultipleAggregateProjectionTestBase<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition,
            CustomerDependency> _clientLoyaltyProjectionTest;

    private readonly
        MultipleAggregateProjectionTestBase<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.ContentsDefinition, CustomerDependency>
        _listProjectionTest;
    private readonly ProjectionListQueryFilterTestChecker<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.ContentsDefinition,
        ClientLoyaltyPointQueryFilter, ClientLoyaltyPointQueryFilter.QueryFilterParameter,
        ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> _listQueryFilter = new();

    private readonly ProjectionQueryFilterTestChecker<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition,
        ClientLoyaltyPointMultipleProjectionQueryFilter, ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter,
        ClientLoyaltyPointMultipleProjection.ContentsDefinition> _projectionQueryFilterTestChecker = new();

    private readonly
        SingleAggregateProjectionListProjectionTestBase<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition,
            CustomerDependency> _singleAggregateProjectionListProjectionTestBase;

    private readonly SingleAggregateListProjectionListQueryFilterTestChecker<Client, ClientNameHistoryProjection,
        ClientNameHistoryProjection.ContentsDefinition, ClientNameHistoryProjectionQueryFilter,
        ClientNameHistoryProjectionQueryFilter.ClientNameHistoryProjectionParameter,
        ClientNameHistoryProjectionQueryFilter.ClientNameHistoryProjectionQueryResponse> _singleAggregateProjectionListQueryFilterTestChecker = new();


    private readonly string branchName = "BranchName";
    private readonly string clientEmail = "test@exmple.com";
    private readonly string clientName = "Client Name";
    private readonly string clientName2 = "Client Name2";
    private Guid _branchId = Guid.Empty;
    private Guid _clientId = Guid.Empty;
    private DateTime dateNameSet = DateTime.Now;

    public UnifiedProjectionsTest()
    {
        _clientLoyaltyProjectionTest
            = SetupMultipleAggregateProjectionTest<MultipleAggregateProjectionTestBase<ClientLoyaltyPointMultipleProjection,
                ClientLoyaltyPointMultipleProjection.ContentsDefinition, CustomerDependency>>();
        _clientLoyaltyProjectionTest.GivenQueryFilterChecker(_projectionQueryFilterTestChecker);

        _listProjectionTest
            = SetupMultipleAggregateProjectionTest<MultipleAggregateProjectionTestBase<ClientLoyaltyPointListProjection,
                ClientLoyaltyPointListProjection.ContentsDefinition, CustomerDependency>>();

        _branchListProjection = SetupMultipleAggregateProjectionTest<AggregateListProjectionTestBase<Branch, BranchContents, CustomerDependency>>();

        _singleAggregateProjectionListProjectionTestBase
            = SetupMultipleAggregateProjectionTest<SingleAggregateProjectionListProjectionTestBase<Client, ClientNameHistoryProjection,
                ClientNameHistoryProjection.ContentsDefinition, CustomerDependency>>();


    }
    protected override void SetupDependency(IServiceCollection serviceCollection)
    {
    }

    [Fact]
    public void Test()
    {
        _branchId = RunCreateCommand(new CreateBranch(branchName));
        _clientId = RunCreateCommand(new CreateClient(_branchId, clientName, clientEmail));
        GetLatestEvents()
            .ToList()
            .ForEach(
                m =>
                {
                    if (m.GetPayload() is ClientCreated created) { dateNameSet = m.TimeStamp; }
                });
        _clientLoyaltyProjectionTest.WhenProjection()
            .ThenNotThrowsAnException()
            .ThenContentsIs(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName, 0))));
        _projectionQueryFilterTestChecker
            .WhenParam(
                new ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter(
                    null,
                    ClientLoyaltyPointMultipleProjectionQueryFilter.QuerySortKeys.ClientName))
            .ThenResponseIs(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName, 0))));
    }

    [Fact]
    public void TestAggregateQueryFilter()
    {
        GivenScenario(Test);
        _branchListProjection.GivenQueryFilterChecker(_branchExistsQueryFilterChecker).WhenProjection().ThenNotThrowsAnException();
        _branchExistsQueryFilterChecker.WhenParam(new BranchExistsQueryFilter.QueryParameter(_branchId)).ThenResponseIs(true);
        _branchExistsQueryFilterChecker.WhenParam(new BranchExistsQueryFilter.QueryParameter(Guid.NewGuid())).ThenResponseIs(false);
    }

    [Fact]
    public void TestSingleAggregateProjection()
    {
        GivenScenario(Test);
        _singleAggregateProjectionListProjectionTestBase.GivenQueryFilterChecker(_singleAggregateProjectionListQueryFilterTestChecker)
            .WhenProjection()
            .ThenNotThrowsAnException();
        _singleAggregateProjectionListQueryFilterTestChecker
            .WhenParam(new ClientNameHistoryProjectionQueryFilter.ClientNameHistoryProjectionParameter(null, null, null, null, null))
            .ThenResponseIs(
                new QueryFilterListResult<ClientNameHistoryProjectionQueryFilter.ClientNameHistoryProjectionQueryResponse>(
                    1,
                    null,
                    null,
                    null,
                    new List<ClientNameHistoryProjectionQueryFilter.ClientNameHistoryProjectionQueryResponse>
                    {
                        new(_branchId, _clientId, clientName, clientEmail, dateNameSet)
                    }));
    }


    [Fact]
    public void WhenChangeName()
    {
        GivenScenario(Test);
        RunChangeCommand(new ChangeClientName(_clientId, clientName2));
        _clientLoyaltyProjectionTest.WhenProjection();
        _projectionQueryFilterTestChecker
            .WhenParam(
                new ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter(
                    null,
                    ClientLoyaltyPointMultipleProjectionQueryFilter.QuerySortKeys.ClientName))
            .ThenResponseIs(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName2, 0))));
    }
}

using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Branches.QueryFilters;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.Clients.Projections;
using CustomerDomainContext.Projections.ClientLoyaltyPointLists;
using CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;
using CustomerDomainContext.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.QueryModels;
using Sekiban.EventSourcing.TestHelpers.ProjectionTests;
using Sekiban.EventSourcing.TestHelpers.QueryFilters;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

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

    private readonly
        SingleAggregateListProjectionListQueryFilterTestChecker<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition,
            ClientNameHistoryProjectionQueryFilter, ClientNameHistoryProjectionParameter, ClientNameHistoryProjectionQueryResponse>
        _singleAggregateProjectionListQueryFilterTestChecker = new();


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
            .ThenContents(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch> { new(_branchId, branchName) },
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedRecord> { new(_branchId, branchName, _clientId, clientName, 0) }));
        _projectionQueryFilterTestChecker
            .WhenParam(
                new ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter(
                    null,
                    ClientLoyaltyPointMultipleProjectionQueryFilter.QuerySortKeys.ClientName))
            .ThenResponse(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch> { new(_branchId, branchName) },
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedRecord> { new(_branchId, branchName, _clientId, clientName, 0) }));
    }

    [Fact]
    public void TestAggregateQueryFilter()
    {
        GivenScenario(Test);
        _branchListProjection.GivenQueryFilterChecker(_branchExistsQueryFilterChecker).WhenProjection().ThenNotThrowsAnException();
        _branchExistsQueryFilterChecker.WhenParam(new BranchExistsQueryFilter.QueryParameter(_branchId)).ThenResponse(true);
        _branchExistsQueryFilterChecker.WhenParam(new BranchExistsQueryFilter.QueryParameter(Guid.NewGuid())).ThenResponse(false);
    }

    [Fact]
    public void TestSingleAggregateProjection()
    {
        GivenScenario(Test);
        _singleAggregateProjectionListProjectionTestBase.GivenQueryFilterChecker(_singleAggregateProjectionListQueryFilterTestChecker)
            .WhenProjection()
            .ThenNotThrowsAnException();
        _singleAggregateProjectionListQueryFilterTestChecker.WhenParam(new ClientNameHistoryProjectionParameter(null, null, null, null, null))
            .ThenResponse(
                new QueryFilterListResult<ClientNameHistoryProjectionQueryResponse>(
                    1,
                    null,
                    null,
                    null,
                    new List<ClientNameHistoryProjectionQueryResponse> { new(_branchId, _clientId, clientName, clientEmail, dateNameSet) }));
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
            .ThenResponse(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch> { new(_branchId, branchName) },
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedRecord> { new(_branchId, branchName, _clientId, clientName2, 0) }));
    }
}

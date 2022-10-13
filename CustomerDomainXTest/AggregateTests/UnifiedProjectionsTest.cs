using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Projections.ClientLoyaltyPointLists;
using CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;
using CustomerDomainContext.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.TestHelpers;
using Sekiban.EventSourcing.TestHelpers.ProjectionTests;
using Sekiban.EventSourcing.TestHelpers.QueryFilters;
using System;
using System.Collections.Generic;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class UnifiedProjectionsTest : MultipleProjectionsAndQueriesTestBase<CustomerDependency>
{
    private readonly
        CommonMultipleAggregateProjectionTestBase<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition,
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
    private readonly string branchName = "BranchName";
    private readonly string clientEmail = "test@exmple.com";
    private readonly string clientName = "Client Name";
    private readonly string clientName2 = "Client Name2";

    private Guid _branchId = Guid.Empty;
    private Guid _clientId = Guid.Empty;

    public UnifiedProjectionsTest()
    {
        _clientLoyaltyProjectionTest
            = SetupMultipleAggregateProjectionTest<MultipleAggregateProjectionTestBase<ClientLoyaltyPointMultipleProjection,
                ClientLoyaltyPointMultipleProjection.ContentsDefinition, CustomerDependency>>();
        _clientLoyaltyProjectionTest.GivenQueryFilterChecker(_projectionQueryFilterTestChecker);

        _listProjectionTest
            = SetupMultipleAggregateProjectionTest<MultipleAggregateProjectionTestBase<ClientLoyaltyPointListProjection,
                ClientLoyaltyPointListProjection.ContentsDefinition, CustomerDependency>>();

    }
    protected override void SetupDependency(IServiceCollection serviceCollection)
    {
    }

    [Fact]
    public void Test()
    {
        _branchId = RunCreateCommand(new CreateBranch(branchName));
        _clientId = RunCreateCommand(new CreateClient(_branchId, clientName, clientEmail));

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

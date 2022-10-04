using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;
using CustomerDomainContext.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.TestHelpers;
using System;
using System.Collections.Generic;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class UnifiedProjectionsTest : MultipleProjectionsAndQueriesTestBase
{
    private readonly
        CommonMultipleAggregateProjectionTestBase<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition>
        _clientLoyaltyProjectionTest;

    private readonly ProjectionQueryFilterTestChecker<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition,
        ClientLoyaltyPointMultipleProjectionQueryFilter, ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter,
        ClientLoyaltyPointMultipleProjection.ContentsDefinition> _projectionQueryFilterTestChecker = new();
    private readonly string branchName = "BranchName";
    private readonly string clientEmail = "test@exmple.com";
    private readonly string clientName = "Client Name";
    private readonly string clientName2 = "Client Name2";

    private Guid _branchId = Guid.Empty;
    private Guid _clientId = Guid.Empty;

    public UnifiedProjectionsTest() : base(CustomerDependency.GetOptions())
    {
        _clientLoyaltyProjectionTest
            = SetupMultipleAggregateProjectionTest<CommonMultipleAggregateProjectionTestBase<ClientLoyaltyPointMultipleProjection,
                ClientLoyaltyPointMultipleProjection.ContentsDefinition>>();
        _clientLoyaltyProjectionTest.GivenQueryFilterChecker(_projectionQueryFilterTestChecker);
    }
    protected override void SetupDependency(IServiceCollection serviceCollection)
    {
        serviceCollection.AddQueryFilters(
            CustomerDependency.GetProjectionQueryFilterTypes(),
            CustomerDependency.GetProjectionListQueryFilterTypes(),
            CustomerDependency.GetAggregateListQueryFilterTypes(),
            CustomerDependency.GetSingleAggregateProjectionListQueryFilterTypes());
    }

    [Fact]
    public void Test()
    {
        _branchId = RunCreateCommand(new CreateBranch(branchName));
        _clientId = RunCreateCommand(new CreateClient(_branchId, clientName, clientEmail));
        RunChangeCommand(new ChangeClientName(_clientId, clientName2));
        
        
        _clientLoyaltyProjectionTest.WhenProjection()
            .ThenNotThrowsAnException()
            .ThenContents(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch> { new(_branchId, branchName) },
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedRecord>()));
        _projectionQueryFilterTestChecker
            .WhenParam(
                new ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter(
                    null,
                    ClientLoyaltyPointMultipleProjectionQueryFilter.QuerySortKeys.ClientName))
            .ThenResponse(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch> { new(_branchId, branchName) },
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedRecord>()));
        
        _projectionQueryFilterTestChecker
            .WhenParam(
                new ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter(
                    null,
                    ClientLoyaltyPointMultipleProjectionQueryFilter.QuerySortKeys.Points))
            .ThenResponse(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch> { new(_branchId, branchName) },
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedRecord>()));
        
        RunChangeCommand(new ChangeClientName(_clientId, clientName2));


    }

    public void WhenSecondTest()
    {
        GivenScenario(Test);
        
        _projectionQueryFilterTestChecker
            .WhenParam(
                new ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter(
                    null,
                    ClientLoyaltyPointMultipleProjectionQueryFilter.QuerySortKeys.Points))
            .ThenResponse(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch> { new(_branchId, branchName) },
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedRecord>()));
    }
    public void WhenThirdTest()
    {
        GivenScenario(Test);
        
        _projectionQueryFilterTestChecker
            .WhenParam(
                new ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter(
                    null,
                    ClientLoyaltyPointMultipleProjectionQueryFilter.QuerySortKeys.Points))
            .ThenResponse(
                new ClientLoyaltyPointMultipleProjection.ContentsDefinition(
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch> { new(_branchId, branchName) },
                    new List<ClientLoyaltyPointMultipleProjection.ProjectedRecord>()));
    }

}

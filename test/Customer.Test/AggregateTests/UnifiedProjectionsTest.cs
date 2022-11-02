using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.Queries;
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
using Sekiban.Testing.Queries;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace Customer.Test.AggregateTests;

public class UnifiedProjectionsTest : MultipleProjectionsAndQueriesTestBase<CustomerDependency>
{

    private readonly AggregateQueryTest<Branch, BranchExistsQuery, BranchExistsQuery.QueryParameter, bool>
        branchExistsQuery = new();

    private readonly AggregateListProjectionTestBase<Branch, CustomerDependency> branchListProjection;


    private readonly string branchName = "BranchName";
    private readonly string clientEmail = "test@exmple.com";
    private readonly
        MultiProjectionTestBase<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.PayloadDefinition,
            CustomerDependency> clientLoyaltyProjectionTest;
    private readonly string clientName = "Client Name";
    private readonly string clientName2 = "Client Name2";

    private readonly
        MultiProjectionTestBase<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.PayloadDefinition, CustomerDependency>
        listProjectionTest;
    private readonly MultiProjectionListQueryTest<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.PayloadDefinition,
        ClientLoyaltyPointQuery, ClientLoyaltyPointQuery.QueryParameter,
        ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord> listQuery = new();

    private readonly MultiProjectionQueryTest<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.PayloadDefinition,
        ClientLoyaltyPointMultipleMultiProjectionQuery, ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter,
        ClientLoyaltyPointMultipleProjection.PayloadDefinition> multiProjectionQueryTest = new();

    private readonly
        SingleProjectionListTestBase<Client, ClientNameHistoryMultiProjection, ClientNameHistoryMultiProjection.PayloadDefinition,
            CustomerDependency> singleProjectionListTestBase;

    private readonly SingleProjectionListQueryTest<Client, ClientNameHistoryMultiProjection,
        ClientNameHistoryMultiProjection.PayloadDefinition, ClientNameHistoryProjectionQuery,
        ClientNameHistoryProjectionQuery.ClientNameHistoryProjectionParameter,
        ClientNameHistoryProjectionQuery.ClientNameHistoryProjectionQueryResponse> singleProjectionQueryTest = new();
    private Guid _branchId = Guid.Empty;
    private Guid _clientId = Guid.Empty;
    private DateTime dateNameSet = DateTime.Now;

    public UnifiedProjectionsTest()
    {
        clientLoyaltyProjectionTest
            = SetupMultiProjectionTest<MultiProjectionTestBase<ClientLoyaltyPointMultipleProjection,
                ClientLoyaltyPointMultipleProjection.PayloadDefinition, CustomerDependency>>();
        clientLoyaltyProjectionTest.GivenQueryTest(multiProjectionQueryTest);

        listProjectionTest
            = SetupMultiProjectionTest<MultiProjectionTestBase<ClientLoyaltyPointListProjection,
                ClientLoyaltyPointListProjection.PayloadDefinition, CustomerDependency>>();

        branchListProjection = SetupMultiProjectionTest<AggregateListProjectionTestBase<Branch, CustomerDependency>>();

        singleProjectionListTestBase
            = SetupMultiProjectionTest<SingleProjectionListTestBase<Client, ClientNameHistoryMultiProjection,
                ClientNameHistoryMultiProjection.PayloadDefinition, CustomerDependency>>();
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
        clientLoyaltyProjectionTest.WhenProjection()
            .ThenNotThrowsAnException()
            .ThenPayloadIs(
                new ClientLoyaltyPointMultipleProjection.PayloadDefinition(
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName, 0))));
        multiProjectionQueryTest
            .WhenParam(
                new ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter(
                    null,
                    ClientLoyaltyPointMultipleMultiProjectionQuery.QuerySortKeys.ClientName))
            .ThenResponseIs(
                new ClientLoyaltyPointMultipleProjection.PayloadDefinition(
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName, 0))));
    }

    [Fact]
    public void TestAggregateQuery()
    {
        GivenScenario(Test);
        branchListProjection.GivenQueryTest(branchExistsQuery).WhenProjection().ThenNotThrowsAnException();
        branchExistsQuery.WhenParam(new BranchExistsQuery.QueryParameter(_branchId)).ThenResponseIs(true);
        branchExistsQuery.WhenParam(new BranchExistsQuery.QueryParameter(Guid.NewGuid())).ThenResponseIs(false);
    }

    [Fact]
    public void TestSingleProjection()
    {
        GivenScenario(Test);
        singleProjectionListTestBase.GivenQueryTest(singleProjectionQueryTest)
            .WhenProjection()
            .ThenNotThrowsAnException();
        singleProjectionQueryTest
            .WhenParam(new ClientNameHistoryProjectionQuery.ClientNameHistoryProjectionParameter(null, null, null, null, null))
            .ThenResponseIs(
                new QueryListResult<ClientNameHistoryProjectionQuery.ClientNameHistoryProjectionQueryResponse>(
                    1,
                    null,
                    null,
                    null,
                    new List<ClientNameHistoryProjectionQuery.ClientNameHistoryProjectionQueryResponse>
                    {
                        new(_branchId, _clientId, clientName, clientEmail, dateNameSet)
                    }));
    }


    [Fact]
    public void WhenChangeName()
    {
        GivenScenario(Test);
        RunChangeCommand(new ChangeClientName(_clientId, clientName2));
        clientLoyaltyProjectionTest.WhenProjection();
        multiProjectionQueryTest
            .WhenParam(
                new ClientLoyaltyPointMultipleMultiProjectionQuery.QueryParameter(
                    null,
                    ClientLoyaltyPointMultipleMultiProjectionQuery.QuerySortKeys.ClientName))
            .ThenResponseIs(
                new ClientLoyaltyPointMultipleProjection.PayloadDefinition(
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedBranch>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedBranch(_branchId, branchName)),
                    ImmutableList<ClientLoyaltyPointMultipleProjection.ProjectedRecord>.Empty.Add(
                        new ClientLoyaltyPointMultipleProjection.ProjectedRecord(_branchId, branchName, _clientId, clientName2, 0))));
    }
}

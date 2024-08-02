using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using System.Data;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class ResultClientSpec : AggregateTest<Client, FeatureCheckDependency>
{
    [Fact]
    public void ShouldCreateClient()
    {
        var branchId = RunEnvironmentCommand(new CreateBranchWithResult("branch1"));
        WhenCommand(new CreateClientR(branchId, "client1", "client1@example.com"));
        ThenPayloadIs(new Client(branchId, "client1", "client1@example.com"));
    }

    [Fact]
    public void CreateClientErrorIfBranchNotExists()
    {
        var branchId = Guid.NewGuid();
        WhenCommand(new CreateClientR(branchId, "client1", "client1@example.com"));
        ThenThrowsAnException();
    }
    [Fact]
    public void CreateClientErrorIfEmailAlreadyExists()
    {
        var branchId = RunEnvironmentCommand(new CreateBranchWithResult("branch1"));
        RunEnvironmentCommand(new CreateClientR(branchId, "client0", "client1@example.com"));
        WhenCommand(new CreateClientR(branchId, "client1", "client1@example.com"));
        ThenThrowsAnException();
    }

    [Fact]
    public void CanChangeNameTest()
    {
        var branchId = RunEnvironmentCommand(new CreateBranchWithResult("branch1"));
        GivenCommand(new CreateClientR(branchId, "client0", "client1@example.com"));
        WhenCommand(new ChangeClientNameWithoutLoadingWithResult(GetAggregateId(), "client1"));
        ThenPayloadIs(new Client(branchId, "client1", "client1@example.com"));
    }

    [Fact]
    public void QueryCanGetExceptionResult()
    {
        var branchId = RunEnvironmentCommand(new CreateBranchWithResult("branch1"));
        GivenCommand(new CreateClientR(branchId, "client0", "client1@example.com"));
        ThenQueryThrows<NoNullAllowedException>(new GetClientPayloadQueryNext(" "));
        ThenQueryThrows<NoNullAllowedException>(new ClientEmailExistQueryNext(" "));
    }

    [Fact]
    public void CanChangeName2Test()
    {
        var branchId = RunEnvironmentCommand(new CreateBranchWithResult("branch1"));
        var clientId = RunEnvironmentCommand(new CreateClientR(branchId, "client0", "client1@example.com"));
        RunEnvironmentCommand(new ChangeClientNameWithoutLoadingWithResult(clientId, "client1"));
        WhenCommand(new CreateClientR(branchId, "client1", "client2@example.com"));
        ThenPayloadIs(new Client(branchId, "client1", "client2@example.com"));
    }
}

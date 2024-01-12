using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Exceptions;
using Sekiban.Testing.SingleProjections;
using System;
using System.ComponentModel.DataAnnotations;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class BranchSpec : AggregateTest<Branch, FeatureCheckDependency>
{
    [Fact]
    public void CreateBranchCommandTest()
    {
        WhenCommand(new CreateBranch("Japan Tokyo")).ThenPayloadIs(new Branch("Japan Tokyo", 0));
    }
    [Fact]
    public void ValidationErrorTest()
    {
        Assert.Throws<ValidationException>(
            () =>
            {
                RunEnvironmentCommand(new CreateBranch("Japan Tokyo99999999999999999999999"));
            });
    }
    [Fact]
    public void AddNumberOfClientsShouldFailIfAggregateNotExists()
    {
        WhenCommand(new AddNumberOfClients { BranchId = Guid.NewGuid(), ClientId = Guid.NewGuid() });
        ThenThrows<SekibanAggregateNotExistsException>();
    }
    [Fact]
    public void AddNumberOfClientsSucceed()
    {
        GivenCommand(new CreateBranch("Japan Tokyo"));
        var clientId = GivenEnvironmentCommand(new CreateClient(GetAggregateId(), "John", "john@example.com"));
        WhenCommand(new AddNumberOfClients { BranchId = GetAggregateId(), ClientId = clientId });
        ThenNotThrowsAnException();
        ThenPayloadIs(new Branch("Japan Tokyo", 1));
    }
}

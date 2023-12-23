using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class CheckValidationErrorBeforeThenSpec : AggregateTest<Client, FeatureCheckDependency>
{
    [Fact]
    public void ShouldThrowValidationException()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("BranchName"));
        WhenCommand(new CreateClient(branchId, "ClientName", ""));
        try
        {
            ThenPayloadIs(new Client(branchId, "ClientName", ""));
        }
        catch (Exception e)
        {
            Assert.Contains("validation error", e.ToString());
        }
    }

    [Fact]
    public void ShouldThrowValidationExceptionForCommand()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("BranchName"));
        WhenCommand(new CreateClient(branchId, "ClientName", ""));
        try
        {
            WhenCommand(new ChangeClientName(GetAggregateId(), "test"));
        }
        catch (Exception e)
        {
            Assert.Contains("validation error", e.ToString());
        }
    }
}

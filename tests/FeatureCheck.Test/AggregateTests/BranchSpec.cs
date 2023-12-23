using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
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
}

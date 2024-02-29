using FeatureCheck.Domain.Aggregates.DerivedTypes;
using FeatureCheck.Domain.Aggregates.DerivedTypes.Commands;
using FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class DerivedTypeSpec : AggregateTest<DerivedTypeAggregate, FeatureCheckDependency>
{
    [Fact]
    public void CreateCar()
    {
        WhenCommand(new CreateCar("blue", "outback"));
        ThenPayloadIs(new DerivedTypeAggregate(new Car("blue", "outback")));
    }

    [Fact]
    public void CreateCarValidateError1()
    {
        WhenCommand(new CreateCar("", "outback"));
        ThenHasValidationErrors();
    }
    [Fact]
    public void CreateCarValidateError2()
    {
        WhenCommand(new CreateCar("blue", ""));
        ThenHasValidationErrors();
    }
}

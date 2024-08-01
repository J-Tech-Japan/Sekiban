using FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;
using Sekiban.Core.Command;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.DerivedTypes.Commands;

public record CreateCar(
    string Color,
    [property: Required] string Name) : ICommandConverter<DerivedTypeAggregate>
{
    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandConverterHandler<DerivedTypeAggregate, CreateCar>
    {
        public ICommand<DerivedTypeAggregate> ConvertCommand(CreateCar command) => new CreateVehicle(new Car(command.Color, command.Name));
    }
}

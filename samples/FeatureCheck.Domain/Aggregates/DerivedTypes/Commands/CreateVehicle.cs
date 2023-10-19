using FeatureCheck.Domain.Aggregates.DerivedTypes.Events;
using FeatureCheck.Domain.Aggregates.DerivedTypes.ValueObjects;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.DerivedTypes.Commands;

public record CreateVehicle(IVehicle Vehicle) : ICommand<DerivedTypeAggregate>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public class Handler : ICommandHandler<DerivedTypeAggregate, CreateVehicle>
    {
        public IEnumerable<IEventPayloadApplicableTo<DerivedTypeAggregate>> HandleCommand(
            Func<AggregateState<DerivedTypeAggregate>> getAggregateState,
            CreateVehicle command)
        {
            yield return new DerivedTypeCreated(command.Vehicle);
        }
    }
}

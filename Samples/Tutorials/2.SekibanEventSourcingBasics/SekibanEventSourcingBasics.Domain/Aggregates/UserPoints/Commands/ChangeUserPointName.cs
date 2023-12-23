using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Events;
using System.ComponentModel.DataAnnotations;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Commands;

public record ChangeUserPointName(
    [property: Required] Guid UserPointId,
    [property: Required] string NameToChange) : ICommand<UserPoint>
{
    // Aggregate Id should be current aggregate.
    public Guid GetAggregateId() => UserPointId;

    public class Handler : ICommandHandler<UserPoint, ChangeUserPointName>
    {
        public IEnumerable<IEventPayloadApplicableTo<UserPoint>> HandleCommand(ChangeUserPointName command, ICommandContext<UserPoint> context)
        {
            if (command.NameToChange == context.GetState().Payload.Name)
                throw new InvalidOperationException("Already have same name as requested.");
            yield return new UserPointNameChanged(command.NameToChange);
        }
    }
}
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Events;
using System.ComponentModel.DataAnnotations;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Commands;


public record CreateUserPoint(
    [property: Required]string Name, 
    [property:Required, EmailAddress]string Email,
    [property:Range(0,10000)] int Point) : ICommand<UserPoint>
{
    // Assign new Aggregate Id by NewGuid()
    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<UserPoint, CreateUserPoint>
    {
        public IEnumerable<IEventPayloadApplicableTo<UserPoint>> HandleCommand(CreateUserPoint command, ICommandContext<UserPoint> context)
        {
            yield return new UserPointCreated(command.Name, command.Email, command.Point);
        }
    }
}
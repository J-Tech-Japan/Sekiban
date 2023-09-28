using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;

namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints;


public record CreateUserPoint(
    [property: Required]string Name, 
    [property:Required, EmailAddress]string Email,
    [property:Range(0,10000)] int Point) : ICommand<UserPoint>
{
    // Assign new Aggregate Id by NewGuid()
    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<UserPoint, CreateUserPoint>
    {
        public IEnumerable<IEventPayloadApplicableTo<UserPoint>> HandleCommand(Func<AggregateState<UserPoint>> getAggregateState, CreateUserPoint command)
        {
            yield return new UserPointCreated(command.Name, command.Email, command.Point);
        }
    }
}

public record ChangeUserPointName(
    [property: Required] Guid UserPointId,
    [property: Required] string NameToChange) : ICommand<UserPoint>
{
    // Aggregate Id should be current aggregate.
    public Guid GetAggregateId() => UserPointId;

    public class Handler : ICommandHandler<UserPoint, ChangeUserPointName>
    {
        public IEnumerable<IEventPayloadApplicableTo<UserPoint>> HandleCommand(Func<AggregateState<UserPoint>> getAggregateState, ChangeUserPointName command)
        {
            if (command.NameToChange == getAggregateState().Payload.Name)
                throw new InvalidOperationException("Already have same name as requested.");
            yield return new UserPointNameChanged(command.NameToChange);
        }
    }
}
using ResultBoxes;
using Sekiban.Core.Command;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Events;
using System.ComponentModel.DataAnnotations;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Commands;

public record ChangeUserPointName(
    [property: Required]
    Guid UserPointId,
    [property: Required]
    string NameToChange) : ICommandWithHandler<UserPoint, ChangeUserPointName>
{
    public static Guid SpecifyAggregateId(ChangeUserPointName command) => command.UserPointId;
    public static ResultBox<EventOrNone<UserPoint>> HandleCommand(ChangeUserPointName command, ICommandContext<UserPoint> context) =>
    ResultBox.Start
        .Verify(
            _ => context.GetState().Payload.Name == command.NameToChange
                ? new InvalidOperationException("Already have same name as requested.")
                : ExceptionOrNone.None)
        .Conveyor(_ => EventOrNone.Event(new UserPointNameChanged(command.NameToChange)));
}

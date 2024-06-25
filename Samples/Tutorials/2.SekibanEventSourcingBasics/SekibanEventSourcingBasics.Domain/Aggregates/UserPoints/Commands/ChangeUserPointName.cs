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
    // Aggregate Id should be current aggregate.
    public Guid GetAggregateId() => UserPointId;
    public static ResultBox<UnitValue> HandleCommand(
        ChangeUserPointName command,
        ICommandContext<UserPoint> context) =>
        ResultBox.Start
            .Verify(
                _ => context.GetState().Payload.Name == command.NameToChange
                    ? new InvalidOperationException("Already have same name as requested.")
                    : ExceptionOrNone.None)
            .Conveyor(_ => context.AppendEvent(new UserPointNameChanged(command.NameToChange)));
}

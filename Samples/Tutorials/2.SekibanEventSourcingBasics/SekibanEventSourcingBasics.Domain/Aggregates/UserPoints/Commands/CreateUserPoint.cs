using ResultBoxes;
using Sekiban.Core.Command;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Events;
using System.ComponentModel.DataAnnotations;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Commands;

public record CreateUserPoint(
    [property: Required]
    string Name,
    [property: Required]
    [property: EmailAddress]
    string Email,
    [property: Range(0, 10000)]
    int Point) : ICommandWithHandler<UserPoint, CreateUserPoint>
{
    // Assign new Aggregate Id by NewGuid()
    public Guid GetAggregateId() => Guid.NewGuid();

    public static ResultBox<UnitValue> HandleCommand(
        CreateUserPoint command,
        ICommandContext<UserPoint> context) =>
        context.AppendEvent(new UserPointCreated(command.Name, command.Email, command.Point));
}

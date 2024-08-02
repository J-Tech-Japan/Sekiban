using FeatureCheck.Domain.Aggregates.Clients.Events;
using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record ChangeClientNameWithoutLoadingWithResult(Guid ClientId, string ClientName)
    : ICommandWithHandlerWithoutLoadingAggregate<Client, ChangeClientNameWithoutLoadingWithResult>
{
    public Guid GetAggregateId() => ClientId;

    public static ResultBox<UnitValue> HandleCommand(
        ChangeClientNameWithoutLoadingWithResult command,
        ICommandContextWithoutGetState<Client> context) =>
        context.AppendEvent(new ClientNameChanged(command.ClientName));
}

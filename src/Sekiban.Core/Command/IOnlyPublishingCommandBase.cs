using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface IOnlyPublishingCommandBase<TAggregatePayload> : ICommandBase<TAggregatePayload>, IOnlyPublishingCommand
    where TAggregatePayload : IAggregatePayload, new()
{
}

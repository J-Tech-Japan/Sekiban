using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface IOnlyPublishingCommand<TAggregatePayload> : ICommand<TAggregatePayload>, IOnlyPublishingCommandCommon
    where TAggregatePayload : IAggregatePayload, new()
{
}

using Sekiban.Core.Aggregate;

namespace Sekiban.Core.Command;

public interface IVersionValidationCommand<TAggregatePayload> : ICommand<TAggregatePayload>,
    IVersionValidationCommandCommon
    where TAggregatePayload : IAggregatePayload, new()
{
}

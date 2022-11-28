using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface IVersionValidationCommandBase<TAggregatePayload> : ICommandBase<TAggregatePayload>, IVersionValidationCommand
    where TAggregatePayload : IAggregatePayload, new()
{
}

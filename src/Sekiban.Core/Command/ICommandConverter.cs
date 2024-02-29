using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandConverter<TAggregatePayload> : ICommand<TAggregatePayload>, ICommandConverterCommon
    where TAggregatePayload : IAggregatePayloadCommon
{
}
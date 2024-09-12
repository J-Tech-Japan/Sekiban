using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandConverter<TAggregatePayload> : ICommandCommon<TAggregatePayload>, ICommandConverterCommon
    where TAggregatePayload : IAggregatePayloadCommon
{
}

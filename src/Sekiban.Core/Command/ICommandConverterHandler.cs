using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICommandConverterHandler<TAggregatePayload, TCommand> : ICommandHandlerCommon<TAggregatePayload, TCommand>,
    ICommandConverterHandlerCommon where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommandConverter<TAggregatePayload>
{
    public ICommandCommon ConvertCommand(TCommand command);
}
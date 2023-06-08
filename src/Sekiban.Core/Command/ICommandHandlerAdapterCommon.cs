namespace Sekiban.Core.Command;

public interface ICommandHandlerAdapterCommon
{
    public Task<CommandResponse> HandleCommandAsync(
        ICommandDocumentCommon commandDocument,
        ICommandHandlerCommonBase handler,
        Guid aggregateId,
        string rootPartitionKey);
}

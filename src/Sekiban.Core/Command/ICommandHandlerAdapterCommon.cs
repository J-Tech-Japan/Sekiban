namespace Sekiban.Core.Command;

/// <summary>
///     Common class for the command handler adapter.
///     Actually this interface is not implemented by the application developer because parameter types are slightly
///     different.
///     Using this interface for the getting method name.
///     Internal Use Only
/// </summary>
public interface ICommandHandlerAdapterCommon
{
    public Task<CommandResponse> HandleCommandAsync(
        ICommandDocumentCommon commandDocument,
        ICommandHandlerCommonBase handler,
        Guid aggregateId,
        string rootPartitionKey);
}

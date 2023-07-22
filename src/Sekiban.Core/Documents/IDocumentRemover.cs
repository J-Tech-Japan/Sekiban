using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Documents;

/// <summary>
///     Use for testing purpose.
///     Remove all events, items from document store.
/// </summary>
public interface IDocumentRemover
{
    /// <summary>
    ///     remove all events
    /// </summary>
    /// <param name="aggregateContainerGroup"></param>
    /// <returns></returns>
    public Task RemoveAllEventsAsync(AggregateContainerGroup aggregateContainerGroup);
    /// <summary>
    ///     remove all items ( commands and snapshots )
    /// </summary>
    /// <param name="aggregateContainerGroup"></param>
    /// <returns></returns>
    public Task RemoveAllItemsAsync(AggregateContainerGroup aggregateContainerGroup);
}

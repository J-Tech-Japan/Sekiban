namespace Sekiban.Dcb.Queries;

/// <summary>
/// Interface for queries that need to wait for a specific sortable unique ID to be processed
/// </summary>
public interface IWaitForSortableUniqueId
{
    /// <summary>
    /// The sortable unique ID to wait for before executing the query
    /// </summary>
    string? WaitForSortableUniqueId { get; }
}
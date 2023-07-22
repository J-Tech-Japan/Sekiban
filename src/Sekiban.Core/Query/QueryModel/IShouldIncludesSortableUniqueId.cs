namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     If query implements IShouldIncludesSortableUniqueId and put sortable unique id,
///     query will include specific Sortable Unique Id Value.
/// </summary>
public interface IShouldIncludesSortableUniqueId
{
    /// <summary>
    ///     Sortable Unique Id that need to include in the query
    /// </summary>
    public string? IncludesSortableUniqueIdValue { get; }
}

using Amazon.DynamoDBv2.DocumentModel;
using ResultBoxes;
using Sekiban.Core.Documents.ValueObjects;
using Document = Sekiban.Core.Documents.Document;
namespace Sekiban.Infrastructure.Dynamo.Documents;

public static class QueryFilterExtensions
{
    public static void AddSortableUniqueIdIfNull(this QueryFilter queryFilter, string? sinceSortableUniqueId)
    {
        if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
        {
            return;
        }
        queryFilter.AddCondition(nameof(Document.SortableUniqueId), QueryOperator.GreaterThan, sinceSortableUniqueId);
    }
    public static void AddSortableUniqueIdIfNull(
        this QueryFilter queryFilter,
        OptionalValue<SortableUniqueIdValue> sinceSortableUniqueId)
    {
        if (!sinceSortableUniqueId.HasValue)
        {
            return;
        }
        queryFilter.AddCondition(
            nameof(Document.SortableUniqueId),
            QueryOperator.GreaterThan,
            sinceSortableUniqueId.GetValue().Value);
    }
    public static void AddSortableUniqueIdIfNull(this ScanFilter scanFilter, string? sinceSortableUniqueId)
    {
        if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
        {
            return;
        }
        scanFilter.AddCondition(nameof(Document.SortableUniqueId), ScanOperator.GreaterThan, sinceSortableUniqueId);
    }
    public static void AddSortableUniqueIdIfNull(
        this ScanFilter scanFilter,
        OptionalValue<SortableUniqueIdValue> sinceSortableUniqueId)
    {
        if (!sinceSortableUniqueId.HasValue)
        {
            return;
        }
        scanFilter.AddCondition(
            nameof(Document.SortableUniqueId),
            ScanOperator.GreaterThan,
            sinceSortableUniqueId.GetValue().Value);
    }
}

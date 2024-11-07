using Amazon.DynamoDBv2.DocumentModel;
using ResultBoxes;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Exceptions;
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
    public static void AddSortableUniqueIdIfNull(this QueryFilter queryFilter, EventRetrievalInfo eventRetrievalInfo)
    {
        switch (eventRetrievalInfo.SortableIdCondition)
        {
            case SortableIdConditionNone _:
                return;
            case SinceSortableIdCondition sinceSortableIdCondition:
                queryFilter.AddCondition(
                    nameof(Document.SortableUniqueId),
                    QueryOperator.GreaterThan,
                    sinceSortableIdCondition.SortableUniqueId.Value);
                return;
            default:
                throw new SekibanEventRetrievalException("Unknown SortableIdCondition");
        }
    }
    public static void AddSortableUniqueIdIfNull(this ScanFilter scanFilter, ISortableIdCondition sortableIdCondition)
    {
        switch (sortableIdCondition)
        {
            case SortableIdConditionNone _:
                return;
            case SinceSortableIdCondition sinceSortableIdCondition:
                scanFilter.AddCondition(
                    nameof(Document.SortableUniqueId),
                    ScanOperator.GreaterThan,
                    sinceSortableIdCondition.SortableUniqueId.Value);
                return;
            default:
                throw new SekibanEventRetrievalException("Unknown SortableIdCondition");
        }
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

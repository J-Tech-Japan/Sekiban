using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelInstance<TReadModel>(
    Guid ReadModelInstanceId,
    string ReadModelName,
    string TableName,
    SortableUniqueIdValue SafeSortableUniqueIdValue,
    string ServerId,
    bool IsActive) where TReadModel : IReadModelCommon;

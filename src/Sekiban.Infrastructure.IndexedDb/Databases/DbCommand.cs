using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Shared;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbCommand
{
    public string Id { get; init; } = string.Empty;
    public string AggregateContainerGroup { get; init; } = string.Empty;
    public string AggregateId { get; init; } = string.Empty;
    public string PartitionKey { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentTypeName { get; init; } = string.Empty;
    public string? ExecutedUser { get; init; }
    public string? Exception { get; init; }
    public string CallHistories { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string TimeStamp { get; init; } = string.Empty;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;

    public static DbCommand FromCommand(ICommandDocumentCommon command, AggregateContainerGroup aggregateContainerGroup) =>
        new()
        {
            Id = command.Id.ToString(),
            AggregateContainerGroup = aggregateContainerGroup.ToString(),
            AggregateId = command.AggregateId.ToString(),
            PartitionKey = command.PartitionKey,
            DocumentType = command.DocumentType.ToString(),
            DocumentTypeName = command.DocumentTypeName,
            ExecutedUser = command.ExecutedUser,
            Exception = command.Exception,
            CallHistories = SekibanJsonHelper.Serialize(command.CallHistories) ?? string.Empty,
            Payload = SekibanJsonHelper.Serialize(command) ?? string.Empty,
            TimeStamp = DateTimeConverter.ToString(command.TimeStamp),
            SortableUniqueId = command.SortableUniqueId,
            AggregateType = command.AggregateType,
            RootPartitionKey = command.RootPartitionKey,
        };
}
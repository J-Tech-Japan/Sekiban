using Sekiban.Core.Documents;
using Sekiban.Core.History;
using Sekiban.Core.Partition;
using Sekiban.Core.Types;
namespace Sekiban.Core.Command;

/// <summary>
///     Class to persistence commands.
/// </summary>
/// <typeparam name="T"></typeparam>
public sealed record CommandDocument<T> : Document, ICallHistories where T : ICommandCommon
{
    public T Payload { get; init; } = default!;
    public string? ExecutedUser { get; init; } = string.Empty;
    public string? Exception { get; init; } = null;
    public CommandDocument() { }
    public CommandDocument(Guid aggregateId, T commandPayload, Type aggregateType, string rootPartitionKey, List<CallHistory>? callHistories = null) :
        base(
            aggregateId,
            PartitionKeyGenerator.ForCommand(aggregateId, aggregateType.GetBaseAggregatePayloadTypeFromAggregate(), rootPartitionKey),
            DocumentType.Command,
            typeof(T).Name,
            aggregateType.GetBaseAggregatePayloadTypeFromAggregate().Name,
            rootPartitionKey)
    {
        Payload = commandPayload;
        CallHistories = callHistories ?? new List<CallHistory>();
    }
    public List<CallHistory> CallHistories { get; init; } = new();
    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, Payload.GetType().Name, ExecutedUser));
        return histories;
    }
}

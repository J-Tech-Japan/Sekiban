using Sekiban.Core.Command.UserInformation;
using Sekiban.Core.Documents;
using Sekiban.Core.History;
using Sekiban.Core.Partition;
using Sekiban.Core.Types;
namespace Sekiban.Core.Command;

/// <summary>
///     Class to persistence commands.
/// </summary>
/// <typeparam name="T">Command Type</typeparam>
public sealed record CommandDocument<T> : Document, ICommandDocumentCommon, ICallHistories where T : ICommandCommon
{
    /// <summary>
    ///     Command Payload
    /// </summary>
    public T Payload { get; init; } = default!;
    /// <summary>
    ///     Executed user can be set by implementing <see cref="IUserInformationFactory" />
    /// </summary>
    public string? ExecutedUser { get; init; } = string.Empty;
    /// <summary>
    ///     Exception message will be set when an exception occurs during command execution.
    /// </summary>
    public string? Exception { get; init; } = null;
    public CommandDocument()
    {
    }
    /// <summary>
    ///     Constructor for CommandDocument
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="commandPayload"></param>
    /// <param name="aggregateType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="callHistories"></param>
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
    /// <summary>
    ///     Command call histories it will be set by <see cref="ICommandExecutor" />
    /// </summary>
    public List<CallHistory> CallHistories { get; init; } = new();
    /// <summary>
    ///     Get call histories includes itself.
    ///     This is used to create history next to this command.
    /// </summary>
    /// <returns>New Command Hisotry.</returns>
    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, Payload.GetType().Name, ExecutedUser));
        return histories;
    }
}

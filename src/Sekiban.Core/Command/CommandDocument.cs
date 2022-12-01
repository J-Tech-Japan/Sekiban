using Sekiban.Core.Document;
using Sekiban.Core.History;
using Sekiban.Core.Partition;

namespace Sekiban.Core.Command;

public record CommandDocument<T> : DocumentBase, IDocument, ICallHistories where T : ICommandCommon
{
    public CommandDocument()
    {
    }

    public CommandDocument(Guid aggregateId, T commandPayload, Type aggregateType,
        List<CallHistory>? callHistories = null) : base(
        aggregateId,
        PartitionKeyGenerator.ForCommand(aggregateId, aggregateType),
        DocumentType.Command,
        typeof(T).Name)
    {
        Payload = commandPayload;
        CallHistories = callHistories ?? new List<CallHistory>();
    }

    /// <summary>
    ///     コマンド内容
    /// </summary>
    public T Payload { get; init; } = default!;

    /// <summary>
    ///     実行ユーザー
    ///     CommandDocumentで入力する
    /// </summary>
    public string? ExecutedUser { get; init; } = string.Empty;

    /// <summary>
    ///     コマンド内で発生したException Cosmos DBでシリアライズがうまくいかないケースがあるため、JSON化して文字で入れる
    ///     何もエラーがない場合は null
    /// </summary>
    public string? Exception { get; init; } = null;

    /// <summary>
    ///     イベント、コマンドの呼び出し履歴
    /// </summary>
    public List<CallHistory> CallHistories { get; init; } = new();

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, Payload.GetType().Name, ExecutedUser));
        return histories;
    }
}

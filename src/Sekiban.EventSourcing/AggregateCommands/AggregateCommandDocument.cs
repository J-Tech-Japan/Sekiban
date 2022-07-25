namespace Sekiban.EventSourcing.AggregateCommands;

public record AggregateCommandDocument<T> : DocumentBase, IDocument, ICallHistories
    where T : IAggregateCommand
{
    /// <summary>
    ///     コマンド内容
    /// </summary>
    public T Payload { get; init; } = default!;

    /// <summary>
    ///     対象集約ID
    /// </summary>
    public Guid AggregateId { get; init; } = Guid.Empty;

    /// <summary>
    ///     実行ユーザー
    ///     AggregateCommandDocumentで入力する
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

    public AggregateCommandDocument()
    { }
    
    public AggregateCommandDocument(
        Guid aggregateId,
        T payload,
        List<CallHistory>? callHistories = null
    ) : base(
        partitionKey: PartitionKeyCreator.ForAggregateCommand(aggregateId),
        documentType: DocumentType.AggregateCommand,
        documentTypeName: typeof(T).Name
    )
    {
        Payload = payload;
        CallHistories = callHistories ?? new List<CallHistory>();
    }

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, Payload.GetType().Name, ExecutedUser));
        return histories;
    }
}

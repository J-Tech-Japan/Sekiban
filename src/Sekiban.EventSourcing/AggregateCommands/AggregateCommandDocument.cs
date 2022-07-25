namespace Sekiban.EventSourcing.AggregateCommands;

public record AggregateCommandDocument<T> : IDocument, ICallHistories where T : IAggregateCommand
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    public string PartitionKey { get; init; } = string.Empty;

    public DocumentType DocumentType { get; init; }

    public string DocumentTypeName { get; init; } = null!;

    public DateTime TimeStamp { get; init; }

    public string SortableUniqueId { get; init; } = string.Empty;

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
    
    public AggregateCommandDocument(T payload, IPartitionKeyFactory partitionKeyFactory, List<CallHistory>? callHistories = null)
    {
        Id = Guid.NewGuid();
        DocumentType = DocumentType.AggregateCommand;
        DocumentTypeName = typeof(T).Name;
        TimeStamp = DateTime.UtcNow;
        SortableUniqueId = SortableUniqueIdGenerator.Generate(TimeStamp, Id);

        Payload = payload;
        PartitionKey = partitionKeyFactory.GetPartitionKey(DocumentType);
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

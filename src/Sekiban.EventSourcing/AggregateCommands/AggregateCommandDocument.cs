using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Histories;
namespace Sekiban.EventSourcing.AggregateCommands;

public record AggregateCommandDocument<T> : Document, ICallHistories
    where T : IAggregateCommand
{
    /// <summary>
    /// コマンド内容
    /// </summary>
    public T Payload { get; }

    /// <summary>
    /// 対象集約ID
    /// </summary>
    public Guid AggregateId { get; set; } = Guid.Empty;

    /// <summary>
    /// 実行ユーザー
    /// AggregateCommandDocumentで入力する
    /// </summary>
    public string? ExecutedUser { get; set; } = string.Empty;

    /// <summary>
    /// イベント、コマンドの呼び出し履歴
    /// </summary>
    public List<CallHistory> CallHistories { get; init; } = new();

    /// <summary>
    /// コマンド内で発生したException Cosmos DBでシリアライズがうまくいかないケースがあるため、JSON化して文字で入れる
    /// 何もエラーがない場合は null
    /// </summary>
    public string? Exception { get; set; } = null;

    public AggregateCommandDocument(
        T payload,
        IPartitionKeyFactory partitionKeyFactory,
        List<CallHistory>? callHistories = null)
        : base(DocumentType.AggregateCommand, partitionKeyFactory, typeof(T).Name)
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

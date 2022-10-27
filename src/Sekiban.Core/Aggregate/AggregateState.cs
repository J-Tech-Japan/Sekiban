using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleAggregate;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Aggregate;

public record AggregateState<TPayload> : ISingleAggregate where TPayload : IAggregatePayload, new()
{

    /// <summary>
    ///     スナップショットからの再構築用。
    /// </summary>
    public AggregateState() { }

    /// <summary>
    ///     一般の構築用。
    /// </summary>
    /// <param name="aggregate"></param>
    public AggregateState(IAggregate aggregate)
    {
        AggregateId = aggregate.AggregateId;
        Version = aggregate.Version;
        LastEventId = aggregate.LastEventId;
        LastSortableUniqueId = aggregate.LastSortableUniqueId;
        AppliedSnapshotVersion = aggregate.AppliedSnapshotVersion;
    }

    public AggregateState(IAggregate aggregate, TPayload payload) : this(aggregate)
    {
        Payload = payload;
    }

    public TPayload Payload { get; init; } = new();

    [Required]
    [Description("集約ID")]
    public Guid AggregateId { get; init; }

    [Required]
    [Description("集約の現在のバージョン")]
    public int Version { get; init; }

    [Required]
    [Description("集約で最後に発行されたイベントのID")]
    public Guid LastEventId { get; init; }

    [Required]
    [Description("適用されたスナップショットのバージョン（未適用の場合は0）")]
    public int AppliedSnapshotVersion { get; init; }

    [Required]
    [Description("並べ替え可能なユニークID（自動付与）、このIDの順番でイベントは常に順番を決定する")]
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public bool GetIsDeleted()
    {
        return Payload is IDeletable { IsDeleted: true };
    }
    
    public dynamic GetComparableObject(AggregateState<TPayload> original, bool copyVersion = true)
    {
        return this with
        {
            AggregateId = original.AggregateId,
            Version = copyVersion ? original.Version : Version,
            LastEventId = original.LastEventId,
            AppliedSnapshotVersion = original.AppliedSnapshotVersion,
            LastSortableUniqueId = original.LastSortableUniqueId
        };
    }
}

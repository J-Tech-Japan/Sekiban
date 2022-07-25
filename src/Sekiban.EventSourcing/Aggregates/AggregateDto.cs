using Sekiban.EventSourcing.Queries.SingleAggregates;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.Aggregates;

public record AggregateDto<TContents> : ISingleAggregate where TContents : IAggregateContents
{
    [Required]
    [Description("集約が削除済みかどうか")]
    public bool IsDeleted { get; init; }

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

    public TContents Contents { get; init; } = default!;

    /// <summary>
    ///     スナップショットからの再構築用。
    /// </summary>
    public AggregateDto()
    { }
    
    /// <summary>
    ///     一般の構築用。
    /// </summary>
    /// <param name="aggregate"></param>
    public AggregateDto(IAggregate aggregate)
    {
        AggregateId = aggregate.AggregateId;
        Version = aggregate.Version;
        LastEventId = aggregate.LastEventId;
        LastSortableUniqueId = aggregate.LastSortableUniqueId;
        AppliedSnapshotVersion = aggregate.AppliedSnapshotVersion;
        IsDeleted = aggregate.IsDeleted;
    }

    public AggregateDto(IAggregate aggregate, TContents contents) : this(aggregate) =>
        Contents = contents;

    public dynamic GetComparableObject(AggregateDto<TContents> original, bool copyVersion = true) =>
        this with
        {
            Version = copyVersion ? original.Version : Version,
            LastEventId = original.LastEventId,
            AppliedSnapshotVersion = original.AppliedSnapshotVersion,
            LastSortableUniqueId = original.LastSortableUniqueId
        };
}

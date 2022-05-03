namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers;

public record SnapshotManagerDto : AggregateDtoBase
{
    public List<string> Requests { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SnapshotManagerDto() { }
    public SnapshotManagerDto(SnapshotManager aggregate) : base(aggregate) { }
}

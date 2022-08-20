namespace Sekiban.EventSourcing.Snapshots.SnapshotManagers
{
    public record SnapshotManagerContents : IAggregateContents
    {
        public IReadOnlyCollection<string> Requests { get; set; } = new List<string>();
        public IReadOnlyCollection<string> RequestTakens { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

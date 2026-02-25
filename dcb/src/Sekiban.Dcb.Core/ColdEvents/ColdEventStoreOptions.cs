namespace Sekiban.Dcb.ColdEvents;

public record ColdEventStoreOptions
{
    public bool Enabled { get; init; }
    public TimeSpan PullInterval { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan SafeWindow { get; init; } = TimeSpan.FromMinutes(2);
    public int SegmentMaxEvents { get; init; } = 100_000;
    public long SegmentMaxBytes { get; init; } = 512L * 1024 * 1024;
}

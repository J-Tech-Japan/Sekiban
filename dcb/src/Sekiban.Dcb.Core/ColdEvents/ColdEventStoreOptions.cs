namespace Sekiban.Dcb.ColdEvents;

public record ColdEventStoreOptions
{
    public bool Enabled { get; init; }
    public TimeSpan PullInterval { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan? ExportCycleBudget { get; init; }
    public bool RunOnStartup { get; init; } = true;
    public TimeSpan SafeWindow { get; init; } = TimeSpan.FromMinutes(2);
    public int SegmentMaxEvents { get; init; } = 100_000;
    public int ExportMaxEventsPerRun { get; init; } = 100_000;
    public long SegmentMaxBytes { get; init; } = 512L * 1024 * 1024;
    public int ColdCatchUpBatchSize { get; init; } = 100_000;
    public bool AlignCatchUpReadsToSegmentBoundary { get; init; } = true;
    public bool PersistSnapshotOnColdSegmentBoundary { get; init; } = true;
    public int CatchUpPersistMaxEventsWithoutSnapshot { get; init; } = 100_000;
    public TimeSpan CatchUpPersistMaxInterval { get; init; } = TimeSpan.FromMinutes(5);
}

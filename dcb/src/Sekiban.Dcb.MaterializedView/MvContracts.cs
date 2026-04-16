using System.Data;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MaterializedView;

public enum MvDbType
{
    Postgres = 1
}

public enum MvStatus
{
    Initializing = 0,
    CatchingUp = 1,
    Ready = 2,
    Active = 3,
    Retired = 4
}

public enum MvApplySource
{
    CatchUp = 0,
    Stream = 1
}

public delegate string PhysicalNameResolver(string viewName, int viewVersion, string logicalTable);

public sealed class MvTable
{
    public MvTable(string logicalName, string physicalName, string viewName, int viewVersion)
    {
        LogicalName = logicalName;
        PhysicalName = physicalName;
        ViewName = viewName;
        ViewVersion = viewVersion;
    }

    public string LogicalName { get; }
    public string PhysicalName { get; }
    public string ViewName { get; }
    public int ViewVersion { get; }
}

public readonly record struct MvSqlStatement(string Sql, object? Parameters = null);

public sealed record MvRegistryEntry
{
    public string ServiceId { get; init; } = string.Empty;
    public string ViewName { get; init; } = string.Empty;
    public int ViewVersion { get; init; }
    public string LogicalTable { get; init; } = string.Empty;
    public string PhysicalTable { get; init; } = string.Empty;
    public MvStatus Status { get; init; }
    public string? CurrentPosition { get; init; }
    public string? TargetPosition { get; init; }
    public string? LastSortableUniqueId { get; init; }
    public long AppliedEventVersion { get; init; }
    public string? LastAppliedSource { get; init; }
    public DateTimeOffset? LastAppliedAt { get; init; }
    public string? LastStreamReceivedSortableUniqueId { get; init; }
    public DateTimeOffset? LastStreamReceivedAt { get; init; }
    public string? LastStreamAppliedSortableUniqueId { get; init; }
    public string? LastCatchUpSortableUniqueId { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
    public string? Metadata { get; init; }
}

public sealed record MvActiveEntry(
    string ServiceId,
    string ViewName,
    int ActiveVersion,
    DateTimeOffset ActivatedAt);

public sealed record MvPositionUpdate(
    string ServiceId,
    string ViewName,
    int ViewVersion,
    string SortableUniqueId,
    MvApplySource Source,
    long AppliedEventVersionDelta = 1);

public interface IMvInitContext
{
    MvDbType DatabaseType { get; }
    IDbConnection Connection { get; }
    MvTable RegisterTable(string logicalName);
    Task ExecuteAsync(string sql, object? param = null, CancellationToken cancellationToken = default);
}

public interface IMvApplyContext
{
    MvDbType DatabaseType { get; }
    IDbConnection Connection { get; }
    IDbTransaction Transaction { get; }
    Event CurrentEvent { get; }

    /// <summary>
    ///     Current event position. Materialized view rows can use this value to guard writes and stay idempotent.
    /// </summary>
    string CurrentSortableUniqueId { get; }

    Task<IMvRow?> QuerySingleOrDefaultRowAsync(string sql, object? param = null, CancellationToken cancellationToken = default);
    Task<IMvRowSet> QueryRowsAsync(string sql, object? param = null, CancellationToken cancellationToken = default);
    Task<TScalar> ExecuteScalarAsync<TScalar>(string sql, object? param = null, CancellationToken cancellationToken = default);
    MvTable GetDependencyViewTable(string viewName, string logicalTable);
    MvTable GetDependencyViewTable<TView>(string logicalTable) where TView : IMaterializedViewProjector;
}

public interface IMaterializedViewProjector
{
    string ViewName { get; }
    int ViewVersion { get; }
    Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(Event ev, IMvApplyContext ctx, CancellationToken cancellationToken = default);
}

public interface IMvRegistryStore
{
    Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default);
    Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);
    Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? transaction = null, CancellationToken cancellationToken = default);

    Task MarkStreamReceivedAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        string sortableUniqueId,
        DateTimeOffset receivedAt,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        MvStatus status,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        CancellationToken cancellationToken = default);

    Task<MvActiveEntry?> GetActiveAsync(
        string serviceId,
        string viewName,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        string serviceId,
        string viewName,
        int activeVersion,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);
}

public sealed record MvStorageInfo(
    MvDbType DatabaseType,
    string ConnectionString);

public interface IMvStorageInfoProvider
{
    MvStorageInfo GetStorageInfo();
}

public interface IMvExecutor
{
    Task InitializeAsync(
        IMaterializedViewProjector projector,
        string? serviceId = null,
        CancellationToken cancellationToken = default);

    Task<MvCatchUpResult> CatchUpOnceAsync(
        IMaterializedViewProjector projector,
        string? serviceId = null,
        CancellationToken cancellationToken = default);

    Task<int> ApplySerializableEventsAsync(
        IMaterializedViewProjector projector,
        IReadOnlyList<SerializableEvent> events,
        string? serviceId = null,
        CancellationToken cancellationToken = default);
}

public sealed record MvCatchUpResult(int AppliedEvents, bool ReachedUnsafeWindow, string? LastAppliedSortableUniqueId = null);

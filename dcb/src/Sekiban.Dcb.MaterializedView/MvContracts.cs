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

public sealed record MvRegistryEntry(
    string ServiceId,
    string ViewName,
    int ViewVersion,
    string LogicalTable,
    string PhysicalTable,
    MvStatus Status,
    string? CurrentPosition,
    string? TargetPosition,
    string? LastSortableUniqueId,
    DateTimeOffset LastUpdated,
    string? Metadata);

public sealed record MvActiveEntry(
    string ServiceId,
    string ViewName,
    int ActiveVersion,
    DateTimeOffset ActivatedAt);

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
    Task UpdatePositionAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        string sortableUniqueId,
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

public interface IMvExecutor
{
    Task InitializeAsync(IMaterializedViewProjector projector, CancellationToken cancellationToken = default);
    Task<MvCatchUpResult> CatchUpOnceAsync(IMaterializedViewProjector projector, CancellationToken cancellationToken = default);
}

public sealed record MvCatchUpResult(int AppliedEvents, bool ReachedUnsafeWindow);

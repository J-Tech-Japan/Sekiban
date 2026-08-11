using Microsoft.Data.Sqlite;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

public sealed partial class SqliteMvRegistryStore
{
    private const string LegacyActiveUpsertSql = """
        INSERT INTO sekiban_mv_active (service_id, view_name, active_version, active_generation, activated_at)
        VALUES (@ServiceId, @ViewName, @ActiveVersion, 1, @SwitchedAtUtc)
        ON CONFLICT (service_id, view_name) DO UPDATE SET
            active_version = excluded.active_version,
            active_generation = sekiban_mv_active.active_generation + 1,
            activated_at = excluded.activated_at;
        """;

    private static readonly MvForcedReverseSqlPlan ForcedReverseSql = MvForcedReverseSqlPlan.Create(
        candidateFenceSql: """
            UPDATE sekiban_mv_registry SET last_updated = last_updated
            WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion;
            """,
        fenceReturnsRows: false,
        savepointSql: "SAVEPOINT sekiban_mv_forced_reverse;",
        rollbackSavepointSql: "ROLLBACK TO SAVEPOINT sekiban_mv_forced_reverse;",
        releaseSavepointSql: "RELEASE SAVEPOINT sekiban_mv_forced_reverse;");

    protected override MvForcedReverseSqlPlan ForcedReversePlan => ForcedReverseSql;
    protected override string LegacySetActiveSql => LegacyActiveUpsertSql;
    protected override SqliteConnection CreateForcedReverseConnection() => new(_connectionString);
    protected override object FormatForcedReverseTimestamp(DateTimeOffset value) =>
        SerializeDate(value) ?? throw new InvalidOperationException("A forced-reverse timestamp is required.");
}

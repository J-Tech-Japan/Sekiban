using Npgsql;

namespace Sekiban.Dcb.MaterializedView.Postgres;

public sealed partial class PostgresMvRegistryStore
{
    private const string LegacyActiveUpsertSql = """
        INSERT INTO sekiban_mv_active (service_id, view_name, active_version, active_generation, activated_at)
        VALUES (@ServiceId, @ViewName, @ActiveVersion, 1, NOW())
        ON CONFLICT (service_id, view_name) DO UPDATE SET
            active_version = EXCLUDED.active_version,
            active_generation = sekiban_mv_active.active_generation + 1,
            activated_at = EXCLUDED.activated_at;
        """;

    private static readonly MvForcedReverseSqlPlan ForcedReverseSql = MvForcedReverseSqlPlan.Create(
        candidateFenceSql: """
            SELECT logical_table FROM sekiban_mv_registry
            WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion
            ORDER BY logical_table FOR UPDATE;
            """,
        fenceReturnsRows: true,
        savepointSql: "SAVEPOINT sekiban_mv_forced_reverse;",
        rollbackSavepointSql: "ROLLBACK TO SAVEPOINT sekiban_mv_forced_reverse;",
        releaseSavepointSql: "RELEASE SAVEPOINT sekiban_mv_forced_reverse;");

    protected override MvForcedReverseSqlPlan ForcedReversePlan => ForcedReverseSql;
    protected override string LegacySetActiveSql => LegacyActiveUpsertSql;
    protected override NpgsqlConnection CreateForcedReverseConnection() => new(_connectionString);
}

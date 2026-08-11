using Microsoft.Data.SqlClient;

namespace Sekiban.Dcb.MaterializedView.SqlServer;

public sealed partial class SqlServerMvRegistryStore
{
    private const string LegacyActiveUpsertSql = """
        UPDATE sekiban_mv_active WITH (UPDLOCK, HOLDLOCK)
        SET active_version = @ActiveVersion, active_generation = active_generation + 1, activated_at = SYSUTCDATETIME()
        WHERE service_id = @ServiceId AND view_name = @ViewName;
        IF @@ROWCOUNT = 0
            INSERT INTO sekiban_mv_active (service_id, view_name, active_version, active_generation, activated_at)
            VALUES (@ServiceId, @ViewName, @ActiveVersion, 1, SYSUTCDATETIME());
        """;

    private static readonly MvForcedReverseSqlPlan ForcedReverseSql = MvForcedReverseSqlPlan.Create(
        candidateFenceSql: """
            SELECT logical_table FROM sekiban_mv_registry WITH (UPDLOCK, HOLDLOCK)
            WHERE service_id = @ServiceId AND view_name = @ViewName AND view_version = @ViewVersion
            ORDER BY logical_table;
            """,
        fenceReturnsRows: true,
        savepointSql: "SAVE TRANSACTION sekiban_mv_forced_reverse;",
        rollbackSavepointSql: "ROLLBACK TRANSACTION sekiban_mv_forced_reverse;",
        releaseSavepointSql: null,
        pointerCasSql: """
            UPDATE sekiban_mv_active WITH (UPDLOCK, HOLDLOCK)
            SET active_version = @ViewVersion,
                active_generation = active_generation + 1,
                activated_at = @RequestedAtUtc,
                switch_kind = 'forced',
                switch_reason = @Reason,
                switched_at_utc = @RequestedAtUtc
            WHERE service_id = @ServiceId AND view_name = @ViewName
              AND active_version = @ExpectedActiveVersion
              AND active_generation = @ExpectedActiveGeneration;
            """);

    protected override MvForcedReverseSqlPlan ForcedReversePlan => ForcedReverseSql;
    protected override string LegacySetActiveSql => LegacyActiveUpsertSql;
    protected override SqlConnection CreateForcedReverseConnection() => new(_connectionString);
}

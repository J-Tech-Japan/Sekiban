namespace Sekiban.Dcb.MaterializedView.Postgres;

public sealed partial class PostgresMvRegistryStore
{
    private static readonly MvActiveStatusRestoreSqlPlan ActiveStatusRestoreSql = new(
        CandidateLockSql: """
            SELECT service_id,
                   view_name,
                   view_version,
                   logical_table,
                   physical_table,
                   status,
                   current_position,
                   target_position,
                   current_checkpoint_truth::text,
                   target_checkpoint_truth::text,
                   last_sortable_unique_id,
                   applied_event_version
            FROM sekiban_mv_registry
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
            ORDER BY logical_table
            FOR UPDATE;
            """,
        CandidateFenceSql: null,
        CandidateFenceReturnsRows: false,
        ActivePointerLockSql: """
            SELECT active_version,
                   active_generation
            FROM sekiban_mv_active
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
            FOR UPDATE;
            """,
        RestoreStatusesSql: """
            UPDATE sekiban_mv_registry
            SET status = 'active',
                last_updated = NOW()
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
              AND status IN ('catchingup', 'ready');
            """,
        SavepointSql: "SAVEPOINT sekiban_mv_restore_active_status;",
        RollbackSavepointSql: "ROLLBACK TO SAVEPOINT sekiban_mv_restore_active_status;",
        ReleaseSavepointSql: "RELEASE SAVEPOINT sekiban_mv_restore_active_status;");

    public Task<MvActivationResult> TryRestoreActiveStatusAsync(
        MvActiveStatusRestoreRequest request,
        System.Data.IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        MvActiveStatusRestoreExecution.ExecuteAsync(
            request,
            transaction,
            CreateForcedReverseConnection,
            ActiveStatusRestoreSql,
            FormatForcedReverseTimestamp(DateTimeOffset.UtcNow),
            cancellationToken);
}

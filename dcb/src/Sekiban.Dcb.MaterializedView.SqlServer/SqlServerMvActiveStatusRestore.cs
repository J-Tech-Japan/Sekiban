namespace Sekiban.Dcb.MaterializedView.SqlServer;

public sealed partial class SqlServerMvRegistryStore
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
                   current_checkpoint_truth,
                   target_checkpoint_truth,
                   last_sortable_unique_id,
                   applied_event_version
            FROM sekiban_mv_registry WITH (UPDLOCK, HOLDLOCK)
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
            ORDER BY logical_table;
            """,
        CandidateFenceSql: null,
        CandidateFenceReturnsRows: false,
        ActivePointerLockSql: """
            SELECT active_version,
                   active_generation
            FROM sekiban_mv_active WITH (UPDLOCK, HOLDLOCK)
            WHERE service_id = @ServiceId
              AND view_name = @ViewName;
            """,
        RestoreStatusesSql: """
            UPDATE sekiban_mv_registry
            SET status = 'active',
                last_updated = SYSUTCDATETIME()
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
              AND status IN ('catchingup', 'ready');
            """,
        SavepointSql: "SAVE TRANSACTION sekiban_mv_restore_active_status;",
        RollbackSavepointSql: "ROLLBACK TRANSACTION sekiban_mv_restore_active_status;",
        ReleaseSavepointSql: null);

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

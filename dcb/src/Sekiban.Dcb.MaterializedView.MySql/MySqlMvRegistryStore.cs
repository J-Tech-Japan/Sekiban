using System.Data;
using Dapper;
using MySqlConnector;

namespace Sekiban.Dcb.MaterializedView.MySql;

public sealed partial class MySqlMvRegistryStore : MvForcedReverseRegistryStoreBase<MySqlConnection>, IMvRegistryStore
{
    private readonly string _connectionString;

    public MySqlMvRegistryStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string registrySql = """
            CREATE TABLE IF NOT EXISTS sekiban_mv_registry (
                service_id VARCHAR(200) NOT NULL,
                view_name VARCHAR(200) NOT NULL,
                view_version INT NOT NULL,
                logical_table VARCHAR(200) NOT NULL,
                physical_table VARCHAR(256) NOT NULL,
                status VARCHAR(32) NOT NULL,
                current_position VARCHAR(64) NULL,
                target_position VARCHAR(64) NULL,
                current_checkpoint_truth LONGTEXT NULL,
                target_checkpoint_truth LONGTEXT NULL,
                last_sortable_unique_id VARCHAR(64) NULL,
                applied_event_version BIGINT NOT NULL DEFAULT 0,
                last_applied_source VARCHAR(32) NULL,
                last_applied_at DATETIME(6) NULL,
                last_stream_received_sortable_unique_id VARCHAR(64) NULL,
                last_stream_received_at DATETIME(6) NULL,
                last_stream_applied_sortable_unique_id VARCHAR(64) NULL,
                last_catch_up_sortable_unique_id VARCHAR(64) NULL,
                last_updated DATETIME(6) NOT NULL,
                metadata LONGTEXT NULL,
                PRIMARY KEY (service_id, view_name, view_version, logical_table)
            );
            """;

        const string activeSql = """
            CREATE TABLE IF NOT EXISTS sekiban_mv_active (
                service_id VARCHAR(200) NOT NULL,
                view_name VARCHAR(200) NOT NULL,
                active_version INT NOT NULL,
                active_generation BIGINT NOT NULL DEFAULT 0,
                activated_at DATETIME(6) NOT NULL,
                switch_kind VARCHAR(32) NOT NULL DEFAULT 'legacy',
                switch_reason VARCHAR(1024) NULL,
                switched_at_utc DATETIME(6) NULL,
                PRIMARY KEY (service_id, view_name)
            );
            """;

        await connection.ExecuteAsync(new CommandDefinition(registrySql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var checkpointColumns = (await connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT column_name
                    FROM information_schema.columns
                    WHERE table_schema = DATABASE()
                      AND table_name = 'sekiban_mv_registry'
                      AND column_name IN ('current_checkpoint_truth', 'target_checkpoint_truth');
                    """,
                    cancellationToken: cancellationToken)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!checkpointColumns.Contains("current_checkpoint_truth"))
        {
            await connection.ExecuteAsync(
                    new CommandDefinition(
                        "ALTER TABLE sekiban_mv_registry ADD COLUMN current_checkpoint_truth LONGTEXT NULL;",
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        if (!checkpointColumns.Contains("target_checkpoint_truth"))
        {
            await connection.ExecuteAsync(
                    new CommandDefinition(
                        "ALTER TABLE sekiban_mv_registry ADD COLUMN target_checkpoint_truth LONGTEXT NULL;",
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        await connection.ExecuteAsync(new CommandDefinition(activeSql, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var activeGenerationColumns = (await connection.QueryAsync<string>(
                new CommandDefinition(
                    """
                    SELECT column_name
                    FROM information_schema.columns
                    WHERE table_schema = DATABASE()
                      AND table_name = 'sekiban_mv_active'
                      AND column_name IN ('active_generation', 'switch_kind', 'switch_reason', 'switched_at_utc');
                    """,
                    cancellationToken: cancellationToken)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!activeGenerationColumns.Contains("active_generation"))
        {
            await connection.ExecuteAsync(
                    new CommandDefinition(
                        "ALTER TABLE sekiban_mv_active ADD COLUMN active_generation BIGINT NOT NULL DEFAULT 0;",
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
        }

        if (!activeGenerationColumns.Contains("switch_kind"))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "ALTER TABLE sekiban_mv_active ADD COLUMN switch_kind VARCHAR(32) NOT NULL DEFAULT 'legacy';",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if (!activeGenerationColumns.Contains("switch_reason"))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "ALTER TABLE sekiban_mv_active ADD COLUMN switch_reason VARCHAR(1024) NULL;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if (!activeGenerationColumns.Contains("switched_at_utc"))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "ALTER TABLE sekiban_mv_active ADD COLUMN switched_at_utc DATETIME(6) NULL;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    public async Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO sekiban_mv_registry (
                service_id,
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
                applied_event_version,
                last_applied_source,
                last_applied_at,
                last_stream_received_sortable_unique_id,
                last_stream_received_at,
                last_stream_applied_sortable_unique_id,
                last_catch_up_sortable_unique_id,
                last_updated,
                metadata
            )
            VALUES (
                @ServiceId,
                @ViewName,
                @ViewVersion,
                @LogicalTable,
                @PhysicalTable,
                @Status,
                @CurrentPosition,
                @TargetPosition,
                @CurrentCheckpointTruth,
                @TargetCheckpointTruth,
                @LastSortableUniqueId,
                @AppliedEventVersion,
                @LastAppliedSource,
                @LastAppliedAt,
                @LastStreamReceivedSortableUniqueId,
                @LastStreamReceivedAt,
                @LastStreamAppliedSortableUniqueId,
                @LastCatchUpSortableUniqueId,
                @LastUpdated,
                @Metadata
            )
            ON DUPLICATE KEY UPDATE
                physical_table = VALUES(physical_table),
                last_updated = VALUES(last_updated),
                current_checkpoint_truth = CASE
                    WHEN current_checkpoint_truth IS NULL AND current_position IS NULL THEN VALUES(current_checkpoint_truth)
                    ELSE current_checkpoint_truth
                END,
                target_checkpoint_truth = CASE
                    WHEN target_checkpoint_truth IS NULL AND target_position IS NULL THEN VALUES(target_checkpoint_truth)
                    ELSE target_checkpoint_truth
                END,
                applied_event_version = applied_event_version,
                last_applied_source = COALESCE(last_applied_source, VALUES(last_applied_source)),
                last_applied_at = COALESCE(last_applied_at, VALUES(last_applied_at)),
                last_stream_received_sortable_unique_id = COALESCE(last_stream_received_sortable_unique_id, VALUES(last_stream_received_sortable_unique_id)),
                last_stream_received_at = COALESCE(last_stream_received_at, VALUES(last_stream_received_at)),
                last_stream_applied_sortable_unique_id = COALESCE(last_stream_applied_sortable_unique_id, VALUES(last_stream_applied_sortable_unique_id)),
                last_catch_up_sortable_unique_id = COALESCE(last_catch_up_sortable_unique_id, VALUES(last_catch_up_sortable_unique_id)),
                metadata = COALESCE(VALUES(metadata), metadata);
            """;

        var parameters = new
        {
            entry.ServiceId,
            entry.ViewName,
            entry.ViewVersion,
            entry.LogicalTable,
            entry.PhysicalTable,
            Status = entry.Status.ToString().ToLowerInvariant(),
            CurrentPosition = entry.EffectiveCurrentPosition,
            TargetPosition = entry.EffectiveTargetPosition,
            CurrentCheckpointTruth = MvCheckpointTruthCodec.Encode(entry.CurrentCheckpointTruth),
            TargetCheckpointTruth = MvCheckpointTruthCodec.Encode(entry.TargetCheckpointTruth),
            entry.LastSortableUniqueId,
            entry.AppliedEventVersion,
            entry.LastAppliedSource,
            entry.LastAppliedAt,
            entry.LastStreamReceivedSortableUniqueId,
            entry.LastStreamReceivedAt,
            entry.LastStreamAppliedSortableUniqueId,
            entry.LastCatchUpSortableUniqueId,
            entry.LastUpdated,
            entry.Metadata
        };

        await ExecuteAsync(sql, parameters, transaction, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdatePositionAsync(
        MvPositionUpdate update,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sekiban_mv_registry
            SET current_position = CASE
                    WHEN current_checkpoint_truth IS NULL
                      OR current_position IS NULL
                      OR current_position < @SortableUniqueId THEN @SortableUniqueId
                    ELSE current_position
                END,
                current_checkpoint_truth = CASE
                    WHEN @CheckpointTruth IS NOT NULL
                      AND (current_checkpoint_truth IS NULL
                        OR current_position IS NULL
                        OR current_position <= @SortableUniqueId) THEN @CheckpointTruth
                    ELSE current_checkpoint_truth
                END,
                target_checkpoint_truth = CASE
                    WHEN @TargetCheckpointTruth IS NOT NULL THEN @TargetCheckpointTruth
                    ELSE target_checkpoint_truth
                END,
                last_sortable_unique_id = CASE
                    WHEN last_sortable_unique_id IS NULL
                      OR last_sortable_unique_id < @SortableUniqueId THEN @SortableUniqueId
                    ELSE last_sortable_unique_id
                END,
                applied_event_version = applied_event_version + @AppliedEventVersionDelta,
                last_applied_source = CASE WHEN @AppliedEventVersionDelta > 0 THEN @Source ELSE last_applied_source END,
                last_applied_at = CASE WHEN @AppliedEventVersionDelta > 0 THEN UTC_TIMESTAMP(6) ELSE last_applied_at END,
                last_stream_applied_sortable_unique_id = CASE
                    WHEN @Source = 'stream'
                      AND (last_stream_applied_sortable_unique_id IS NULL
                        OR last_stream_applied_sortable_unique_id < @SortableUniqueId) THEN @SortableUniqueId
                    ELSE last_stream_applied_sortable_unique_id
                END,
                last_catch_up_sortable_unique_id = CASE
                    WHEN @Source = 'catchup' AND @AppliedEventVersionDelta > 0
                      AND (last_catch_up_sortable_unique_id IS NULL
                        OR last_catch_up_sortable_unique_id < @SortableUniqueId) THEN @SortableUniqueId
                    ELSE last_catch_up_sortable_unique_id
                END,
                last_updated = UTC_TIMESTAMP(6)
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        await ExecuteAsync(
            sql,
            new
            {
                update.ServiceId,
                update.ViewName,
                update.ViewVersion,
                update.SortableUniqueId,
                update.AppliedEventVersionDelta,
                CheckpointTruth = MvCheckpointTruthCodec.Encode(MvCheckpointTruth.FromPositionUpdate(update)),
                TargetCheckpointTruth = update.TargetCheckpointTruth is null
                    ? null
                    : MvCheckpointTruthCodec.Encode(update.TargetCheckpointTruth),
                Source = update.Source == MvApplySource.Stream ? "stream" : "catchup"
            },
            transaction,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkStreamReceivedAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        string sortableUniqueId,
        DateTimeOffset receivedAt,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sekiban_mv_registry
            SET last_stream_received_sortable_unique_id = CASE
                    WHEN last_stream_received_sortable_unique_id IS NULL
                      OR last_stream_received_sortable_unique_id < @SortableUniqueId THEN @SortableUniqueId
                    ELSE last_stream_received_sortable_unique_id
                END,
                last_stream_received_at = @ReceivedAt,
                last_updated = UTC_TIMESTAMP(6)
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        await ExecuteAsync(
            sql,
            new
            {
                ServiceId = serviceId,
                ViewName = viewName,
                ViewVersion = viewVersion,
                SortableUniqueId = sortableUniqueId,
                ReceivedAt = receivedAt.UtcDateTime
            },
            transaction,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateStatusAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        MvStatus status,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE sekiban_mv_registry
            SET status = @Status,
                last_updated = UTC_TIMESTAMP(6)
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;

        await ExecuteAsync(
            sql,
            new { ServiceId = serviceId, ViewName = viewName, ViewVersion = viewVersion, Status = status.ToString().ToLowerInvariant() },
            transaction,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task SetTargetCheckpointAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        MvCheckpointTruth targetCheckpointTruth,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetCheckpointTruth);
        const string sql = """
            UPDATE sekiban_mv_registry
            SET target_position = @TargetPosition,
                target_checkpoint_truth = @TargetCheckpointTruth,
                last_updated = UTC_TIMESTAMP(6)
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion;
            """;
        await ExecuteAsync(
            sql,
            new
            {
                ServiceId = serviceId,
                ViewName = viewName,
                ViewVersion = viewVersion,
                TargetPosition = targetCheckpointTruth.PositionValue,
                TargetCheckpointTruth = MvCheckpointTruthCodec.Encode(targetCheckpointTruth)
            },
            transaction,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT service_id AS ServiceId,
                   view_name AS ViewName,
                   view_version AS ViewVersion,
                   logical_table AS LogicalTable,
                   physical_table AS PhysicalTable,
                   status AS Status,
                   current_position AS CurrentPosition,
                   target_position AS TargetPosition,
                   current_checkpoint_truth AS CurrentCheckpointTruth,
                   target_checkpoint_truth AS TargetCheckpointTruth,
                   last_sortable_unique_id AS LastSortableUniqueId,
                   applied_event_version AS AppliedEventVersion,
                   last_applied_source AS LastAppliedSource,
                   last_applied_at AS LastAppliedAt,
                   last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
                   last_stream_received_at AS LastStreamReceivedAt,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
                   last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId,
                   last_updated AS LastUpdated,
                   metadata AS Metadata
            FROM sekiban_mv_registry
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
            ORDER BY logical_table;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, new { ServiceId = serviceId, ViewName = viewName, ViewVersion = viewVersion }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return rows.Select(row => (MvRegistryEntry)MapEntry(ToDictionary(row))).ToList();
    }

    public async Task<MvActiveEntry?> GetActiveAsync(
        string serviceId,
        string viewName,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT service_id AS ServiceId,
                   view_name AS ViewName,
                   active_version AS ActiveVersion,
                   active_generation AS Generation,
                   activated_at AS ActivatedAt,
                   switch_kind AS SwitchKind,
                   switch_reason AS SwitchReason,
                   switched_at_utc AS SwitchedAtUtc
            FROM sekiban_mv_active
            WHERE service_id = @ServiceId
              AND view_name = @ViewName;
            """;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, new { ServiceId = serviceId, ViewName = viewName }, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return row is null ? null : MapActiveEntry(ToDictionary(row));
    }

    public async Task SetActiveAsync(
        string serviceId,
        string viewName,
        int activeVersion,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO sekiban_mv_active (
                service_id, view_name, active_version, active_generation, activated_at,
                switch_kind, switch_reason, switched_at_utc)
            VALUES (@ServiceId, @ViewName, @ActiveVersion, 1, UTC_TIMESTAMP(6), 'legacy', NULL, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                active_version = VALUES(active_version),
                active_generation = active_generation + 1,
                activated_at = VALUES(activated_at),
                switch_kind = 'legacy',
                switch_reason = NULL,
                switched_at_utc = VALUES(switched_at_utc);
            """;

        await ExecuteAsync(
            sql,
            new { ServiceId = serviceId, ViewName = viewName, ActiveVersion = activeVersion },
            transaction,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MvActivationResult> TryActivateAsync(
        MvActivationRequest request,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateActivationRequest(request);
        if (validation is not null)
        {
            return validation;
        }

        if (transaction is not null)
        {
            return await TryActivateWithSavepointAsync(transaction, request, cancellationToken).ConfigureAwait(false);
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var localTransaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await TryActivateInTransactionAsync(localTransaction, request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            await localTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        await localTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<MvActivationResult> TryActivateInTransactionAsync(
        IDbTransaction transaction,
        MvActivationRequest request,
        CancellationToken cancellationToken)
    {
        const string lockCandidatesSql = """
            SELECT logical_table
            FROM sekiban_mv_registry
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
            ORDER BY logical_table
            FOR UPDATE;
            """;
        const string matchingCandidatesSql = """
            SELECT COUNT(*)
            FROM sekiban_mv_registry
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
              AND status = @ExpectedStatus
              AND current_checkpoint_truth = @ExpectedCurrentCheckpointTruth
              AND target_checkpoint_truth = @ExpectedTargetCheckpointTruth;
            """;
        var parameters = new
        {
            request.ServiceId,
            request.ViewName,
            request.ViewVersion,
            request.ExpectedActiveVersion,
            request.ExpectedActiveGeneration,
            request.CandidateCount,
            ExpectedStatus = request.ExpectedStatus.ToString().ToLowerInvariant(),
            request.ExpectedCurrentCheckpointTruth,
            request.ExpectedTargetCheckpointTruth
        };
        var lockedCandidates = (await transaction.Connection!.QueryAsync<string>(
                new CommandDefinition(lockCandidatesSql, parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false)).AsList();
        var matchingCandidates = await transaction.Connection!.ExecuteScalarAsync<int>(
                new CommandDefinition(matchingCandidatesSql, parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        if (lockedCandidates.Count != request.CandidateCount || matchingCandidates != request.CandidateCount)
        {
            return CandidateSnapshotChanged();
        }

        const string insertSql = """
            INSERT INTO sekiban_mv_active (service_id, view_name, active_version, active_generation, activated_at)
            SELECT @ServiceId, @ViewName, @ViewVersion, 1, UTC_TIMESTAMP(6)
            WHERE @ExpectedActiveVersion IS NULL
              AND @ExpectedActiveGeneration = 0
            ON DUPLICATE KEY UPDATE active_version = active_version;
            """;
        const string updateSql = """
            UPDATE sekiban_mv_active
            SET active_version = @ViewVersion,
                active_generation = active_generation + 1,
                activated_at = UTC_TIMESTAMP(6)
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND active_version = @ExpectedActiveVersion
              AND active_generation = @ExpectedActiveGeneration;
            """;
        var affected = request.ExpectedActiveVersion is null
            ? await transaction.Connection!.ExecuteAsync(new CommandDefinition(insertSql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)
            : await transaction.Connection!.ExecuteAsync(new CommandDefinition(updateSql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (affected == 0)
        {
            return CandidateSnapshotChanged();
        }

        await PersistOrdinarySwitchAuditAsync(transaction, request, cancellationToken).ConfigureAwait(false);

        const string markActiveSql = """
            UPDATE sekiban_mv_registry
            SET status = 'active', last_updated = UTC_TIMESTAMP(6)
            WHERE service_id = @ServiceId
              AND view_name = @ViewName
              AND view_version = @ViewVersion
              AND status = @ExpectedStatus
              AND current_checkpoint_truth = @ExpectedCurrentCheckpointTruth
              AND target_checkpoint_truth = @ExpectedTargetCheckpointTruth;
            """;
        var marked = await transaction.Connection!.ExecuteAsync(
                new CommandDefinition(markActiveSql, parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        if (marked != request.CandidateCount)
        {
            return CandidateSnapshotChanged();
        }

        return MvActivationResult.Success(request.ExpectedActiveGeneration + 1);
    }

    private async Task<MvActivationResult> TryActivateWithSavepointAsync(
        IDbTransaction transaction,
        MvActivationRequest request,
        CancellationToken cancellationToken)
    {
        const string createSavepointSql = "SAVEPOINT sekiban_mv_activation;";
        const string rollbackSavepointSql = "ROLLBACK TO SAVEPOINT sekiban_mv_activation;";
        const string releaseSavepointSql = "RELEASE SAVEPOINT sekiban_mv_activation;";
        var connection = transaction.Connection ?? throw new InvalidOperationException("The transaction is not associated with a connection.");
        await connection.ExecuteAsync(
            new CommandDefinition(createSavepointSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        try
        {
            var result = await TryActivateInTransactionAsync(transaction, request, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(rollbackSavepointSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(releaseSavepointSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await connection.ExecuteAsync(
                new CommandDefinition(rollbackSavepointSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            await connection.ExecuteAsync(
                new CommandDefinition(releaseSavepointSql, transaction: transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            throw;
        }
    }

    private static MvActivationResult CandidateSnapshotChanged() =>
        MvActivationResult.Rejected(
            MvActivationFailureReason.ConcurrentSuperseded,
            "The expected active pointer, generation, or candidate snapshot changed before activation.");

    private static MvActivationResult? ValidateActivationRequest(MvActivationRequest request)
    {
        if (request.ExpectedActiveGeneration < 0)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ExpectedGenerationConflict,
                "The expected active generation cannot be negative.");
        }

        if (request.CandidateCount <= 0 || request.ExpectedStatus != MvStatus.Ready)
        {
            return MvActivationResult.Rejected(MvActivationFailureReason.UnsafeLifecycle, "Atomic activation accepts only a non-empty Ready candidate snapshot.");
        }

        try
        {
            var current = MvCheckpointTruthCodec.Decode(request.ExpectedCurrentCheckpointTruth);
            var target = MvCheckpointTruthCodec.Decode(request.ExpectedTargetCheckpointTruth);
            if (!current.IsKnown)
            {
                return MvActivationResult.Rejected(MvActivationFailureReason.CurrentCheckpointUnknown, "Atomic activation requires Known current checkpoint truth.");
            }

            if (current.Provenance is null || current.Provenance.Kind == MvCheckpointProvenanceKind.LegacyCompatibility)
            {
                return MvActivationResult.Rejected(MvActivationFailureReason.MissingProvenance, "Atomic activation requires non-legacy current checkpoint provenance.");
            }

            if (!target.IsKnown || target.Provenance?.Kind != MvCheckpointProvenanceKind.AuthoritativeTargetCapture)
            {
                return MvActivationResult.Rejected(MvActivationFailureReason.TargetUnknown, "Atomic activation requires an authoritative Known target checkpoint.");
            }

            if (!current.Satisfies(target))
            {
                return MvActivationResult.Rejected(MvActivationFailureReason.BehindTarget, "Atomic activation requires the current checkpoint to satisfy the target.");
            }
        }
        catch (MvCheckpointMalformedException ex)
        {
            return MvActivationResult.Rejected(MvActivationFailureReason.ProviderFailure, ex.Message);
        }

        return null;
    }

    private async Task ExecuteAsync(
        string sql,
        object parameters,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
            return;
        }

        await transaction.Connection!.ExecuteAsync(
            new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private static MvRegistryEntry MapEntry(IReadOnlyDictionary<string, object?> row)
    {
        var currentCheckpointTruth = MvCheckpointTruthCodec.Decode(ReadNullableString(row, "CurrentCheckpointTruth"));
        var targetCheckpointTruth = MvCheckpointTruthCodec.Decode(ReadNullableString(row, "TargetCheckpointTruth"));
        return new()
        {
            ServiceId = ReadRequiredString(row, "ServiceId"),
            ViewName = ReadRequiredString(row, "ViewName"),
            ViewVersion = ReadRequiredInt(row, "ViewVersion"),
            LogicalTable = ReadRequiredString(row, "LogicalTable"),
            PhysicalTable = ReadRequiredString(row, "PhysicalTable"),
            Status = Enum.Parse<MvStatus>(ReadRequiredString(row, "Status"), ignoreCase: true),
            CurrentPosition = ReadNullableString(row, "CurrentPosition") ?? currentCheckpointTruth.PositionValue,
            TargetPosition = ReadNullableString(row, "TargetPosition") ?? targetCheckpointTruth.PositionValue,
            CurrentCheckpointTruth = currentCheckpointTruth,
            TargetCheckpointTruth = targetCheckpointTruth,
            LastSortableUniqueId = ReadNullableString(row, "LastSortableUniqueId"),
            AppliedEventVersion = ReadRequiredLong(row, "AppliedEventVersion"),
            LastAppliedSource = ReadNullableString(row, "LastAppliedSource"),
            LastAppliedAt = ReadNullableDateTimeOffset(row, "LastAppliedAt"),
            LastStreamReceivedSortableUniqueId = ReadNullableString(row, "LastStreamReceivedSortableUniqueId"),
            LastStreamReceivedAt = ReadNullableDateTimeOffset(row, "LastStreamReceivedAt"),
            LastStreamAppliedSortableUniqueId = ReadNullableString(row, "LastStreamAppliedSortableUniqueId"),
            LastCatchUpSortableUniqueId = ReadNullableString(row, "LastCatchUpSortableUniqueId"),
            LastUpdated = ReadRequiredDateTimeOffset(row, "LastUpdated"),
            Metadata = ReadNullableString(row, "Metadata")
        };
    }

    private static MvActiveEntry MapActiveEntry(IReadOnlyDictionary<string, object?> row) => ReadActiveEntry(row);

    private static IReadOnlyDictionary<string, object?> ToDictionary(object row)
    {
        if (row is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary;
        }

        if (row is IDictionary<string, object?> dictionary)
        {
            return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
        }

        if (row is IDictionary<string, object> nonNullableDictionary)
        {
            return nonNullableDictionary
                .Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        if (row is System.Collections.IDictionary legacyDictionary)
        {
            return legacyDictionary.Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(
                    entry => entry.Key.ToString() ?? string.Empty,
                    entry => entry.Value,
                    StringComparer.OrdinalIgnoreCase);
        }

        return row.GetType()
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(row), StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadRequiredString(IReadOnlyDictionary<string, object?> row, string key) =>
        TryGetValue(row, key, out var value) && value is not null
            ? value.ToString()!
            : throw new InvalidOperationException($"Registry row is missing required value '{key}'.");

    private static string? ReadNullableString(IReadOnlyDictionary<string, object?> row, string key) =>
        TryGetValue(row, key, out var value) && value is not null
            ? value.ToString()
            : null;

    private static int ReadRequiredInt(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToInt32(TryGetValue(row, key, out var value)
            ? value
            : throw new InvalidOperationException($"Registry row is missing required value '{key}'."));

    private static long ReadRequiredLong(IReadOnlyDictionary<string, object?> row, string key) =>
        Convert.ToInt64(TryGetValue(row, key, out var value)
            ? value
            : throw new InvalidOperationException($"Registry row is missing required value '{key}'."));

    private static DateTimeOffset ReadRequiredDateTimeOffset(IReadOnlyDictionary<string, object?> row, string key) =>
        ReadDateTimeOffsetCore(
            TryGetValue(row, key, out var value)
                ? value
                : throw new InvalidOperationException($"Registry row is missing required value '{key}'."),
            key) ??
        throw new InvalidOperationException($"Registry row is missing required timestamp '{key}'.");

    private static DateTimeOffset? ReadNullableDateTimeOffset(IReadOnlyDictionary<string, object?> row, string key) =>
        TryGetValue(row, key, out var value) ? ReadDateTimeOffsetCore(value, key) : null;

    private static bool TryGetValue(IReadOnlyDictionary<string, object?> row, string key, out object? value)
    {
        if (row.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static DateTimeOffset? ReadDateTimeOffsetCore(object? value, string key) =>
        value switch
        {
            null or DBNull => null,
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => Normalize(dateTime),
            string text when DateTimeOffset.TryParse(text, out var parsed) => parsed,
            _ => throw new InvalidOperationException($"Registry row value '{key}' must be a timestamp.")
        };

    private static DateTimeOffset Normalize(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ResultBoxes;

namespace Sekiban.Dcb.Postgres;

public partial class PostgresMultiProjectionStateStore
{
    private const string ProjectionStatusSchemaSql = """
        CREATE TABLE IF NOT EXISTS dcb_projection_statuses (
            service_id varchar(64) NOT NULL,
            projector_name varchar(256) NOT NULL,
            projector_version varchar(128) NOT NULL,
            cluster_id varchar(256) NOT NULL,
            activation_id varchar(128) NOT NULL,
            sequence bigint NOT NULL,
            applied_event_count bigint NOT NULL,
            last_applied_sortable_unique_id varchar(100) NULL,
            last_traversed_sortable_unique_id varchar(100) NULL,
            recorded_at_utc timestamp with time zone NOT NULL,
            phase varchar(64) NULL,
            lease_expires_at_utc timestamp with time zone NULL,
            is_faulted boolean NOT NULL DEFAULT FALSE,
            fault_message varchar(2048) NULL,
            CONSTRAINT pk_dcb_projection_statuses PRIMARY KEY
                (service_id, projector_name, projector_version, cluster_id)
        );
        CREATE INDEX IF NOT EXISTS ix_dcb_projection_statuses_projector
            ON dcb_projection_statuses (service_id, projector_name, projector_version, cluster_id);
        """;

    public async Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
        ProjectionStatusHeartbeat heartbeat,
        long expectedSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        try
        {
            var serviceId = CurrentServiceId;
            if (!string.Equals(heartbeat.ServiceId, serviceId, StringComparison.Ordinal))
            {
                return ResultBox.Error<ProjectionStatusWriteResult>(
                    new UnauthorizedAccessException("Projection status ServiceId is owned by the server."));
            }

            if (expectedSequence < 0 || heartbeat.Sequence <= 0)
            {
                return ResultBox.Error<ProjectionStatusWriteResult>(
                    new ArgumentOutOfRangeException(nameof(expectedSequence), "Projection status sequences must be positive and expected sequence must not be negative."));
            }

            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await EnsureProjectionStatusSchemaAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO dcb_projection_statuses
                    (service_id, projector_name, projector_version, cluster_id, activation_id, sequence,
                     applied_event_count, last_applied_sortable_unique_id, last_traversed_sortable_unique_id, recorded_at_utc,
                     phase, lease_expires_at_utc, is_faulted, fault_message)
                SELECT @service_id, @projector_name, @projector_version, @cluster_id, @activation_id, @sequence,
                       @applied_event_count, @last_applied_sortable_unique_id, @last_traversed_sortable_unique_id, @recorded_at_utc,
                       @phase, @lease_expires_at_utc, @is_faulted, @fault_message
                WHERE @expected_sequence = 0
                ON CONFLICT (service_id, projector_name, projector_version, cluster_id)
                DO UPDATE SET
                    activation_id = EXCLUDED.activation_id,
                    sequence = EXCLUDED.sequence,
                    applied_event_count = EXCLUDED.applied_event_count,
                    last_applied_sortable_unique_id = EXCLUDED.last_applied_sortable_unique_id,
                    last_traversed_sortable_unique_id = EXCLUDED.last_traversed_sortable_unique_id,
                    recorded_at_utc = EXCLUDED.recorded_at_utc,
                    phase = EXCLUDED.phase,
                    lease_expires_at_utc = EXCLUDED.lease_expires_at_utc,
                    is_faulted = EXCLUDED.is_faulted,
                    fault_message = EXCLUDED.fault_message
                WHERE dcb_projection_statuses.sequence = @expected_sequence
                  AND EXCLUDED.sequence > dcb_projection_statuses.sequence
                RETURNING service_id, projector_name, projector_version, cluster_id, activation_id, sequence,
                          applied_event_count, last_applied_sortable_unique_id, last_traversed_sortable_unique_id,
                          recorded_at_utc, phase, lease_expires_at_utc, is_faulted, fault_message;
                """;
            AddParameter(command, "service_id", heartbeat.ServiceId);
            AddParameter(command, "projector_name", heartbeat.ProjectorName);
            AddParameter(command, "projector_version", heartbeat.ProjectorVersion);
            AddParameter(command, "cluster_id", heartbeat.ClusterId);
            AddParameter(command, "activation_id", heartbeat.ActivationId);
            AddParameter(command, "sequence", heartbeat.Sequence);
            AddParameter(command, "applied_event_count", heartbeat.AppliedEventCount);
            AddParameter(command, "last_applied_sortable_unique_id", (object?)heartbeat.LastAppliedSortableUniqueId ?? DBNull.Value);
            AddParameter(command, "last_traversed_sortable_unique_id", (object?)heartbeat.LastTraversedSortableUniqueId ?? DBNull.Value);
            AddParameter(command, "recorded_at_utc", heartbeat.RecordedAtUtc);
            AddParameter(command, "phase", heartbeat.Phase);
            AddParameter(command, "lease_expires_at_utc", (object?)heartbeat.LeaseExpiresAtUtc ?? DBNull.Value);
            AddParameter(command, "is_faulted", heartbeat.IsFaulted);
            AddParameter(command, "fault_message", (object?)heartbeat.FaultMessage ?? DBNull.Value);
            AddParameter(command, "expected_sequence", expectedSequence);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ResultBox.FromValue(ProjectionStatusWriteResult.Success(ReadHeartbeat(reader)));
            }

            await reader.DisposeAsync().ConfigureAwait(false);
            var current = await ReadCurrentHeartbeatAsync(
                connection,
                heartbeat.ServiceId,
                heartbeat.ProjectorName,
                heartbeat.ProjectorVersion,
                heartbeat.ClusterId,
                cancellationToken).ConfigureAwait(false);
            return ResultBox.FromValue(ProjectionStatusWriteResult.Rejected(
                current,
                $"Heartbeat CAS rejected: expected sequence {expectedSequence}."));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ProjectionStatusWriteResult>(ex);
        }
    }

    public async Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
        string? projectorName = null,
        string? projectorVersion = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await EnsureProjectionStatusSchemaAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT service_id, projector_name, projector_version, cluster_id, activation_id, sequence,
                       applied_event_count, last_applied_sortable_unique_id, last_traversed_sortable_unique_id,
                       recorded_at_utc, phase, lease_expires_at_utc, is_faulted, fault_message
                FROM dcb_projection_statuses
                WHERE service_id = @service_id
                  AND (@projector_name IS NULL OR projector_name = @projector_name)
                  AND (@projector_version IS NULL OR projector_version = @projector_version)
                ORDER BY projector_name, projector_version, cluster_id, activation_id;
                """;
            AddParameter(command, "service_id", CurrentServiceId);
            AddParameter(command, "projector_name", (object?)projectorName ?? DBNull.Value);
            AddParameter(command, "projector_version", (object?)projectorVersion ?? DBNull.Value);

            var rows = new List<ProjectionStatusHeartbeat>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(ReadHeartbeat(reader));
            }

            return ResultBox.FromValue<IReadOnlyList<ProjectionStatusHeartbeat>>(rows);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<ProjectionStatusHeartbeat>>(ex);
        }
    }

    private static async Task EnsureProjectionStatusSchemaAsync(
        SekibanDcbDbContext context,
        CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(ProjectionStatusSchemaSql, cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static ProjectionStatusHeartbeat ReadHeartbeat(DbDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetInt64(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.GetFieldValue<DateTimeOffset>(9))
    {
        Phase = reader.IsDBNull(10) ? ProjectionStatusPhases.Unknown : reader.GetString(10),
        LeaseExpiresAtUtc = reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
        IsFaulted = !reader.IsDBNull(12) && reader.GetBoolean(12),
        FaultMessage = reader.IsDBNull(13) ? null : reader.GetString(13)
    };

    private static async Task<ProjectionStatusHeartbeat?> ReadCurrentHeartbeatAsync(
        DbConnection connection,
        string serviceId,
        string projectorName,
        string projectorVersion,
        string clusterId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT service_id, projector_name, projector_version, cluster_id, activation_id, sequence,
                   applied_event_count, last_applied_sortable_unique_id, last_traversed_sortable_unique_id,
                   recorded_at_utc, phase, lease_expires_at_utc, is_faulted, fault_message
            FROM dcb_projection_statuses
            WHERE service_id = @service_id AND projector_name = @projector_name
              AND projector_version = @projector_version AND cluster_id = @cluster_id;
            """;
        AddParameter(command, "service_id", serviceId);
        AddParameter(command, "projector_name", projectorName);
        AddParameter(command, "projector_version", projectorVersion);
        AddParameter(command, "cluster_id", clusterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadHeartbeat(reader) : null;
    }
}

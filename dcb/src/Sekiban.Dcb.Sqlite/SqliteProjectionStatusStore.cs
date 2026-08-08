using Microsoft.Data.Sqlite;
using ResultBoxes;

namespace Sekiban.Dcb.Sqlite;

public partial class SqliteMultiProjectionStateStore
{
    private static void EnsureProjectionStatusSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS dcb_projection_statuses (
                ServiceId TEXT NOT NULL,
                ProjectorName TEXT NOT NULL,
                ProjectorVersion TEXT NOT NULL,
                ClusterId TEXT NOT NULL,
                ActivationId TEXT NOT NULL,
                Sequence INTEGER NOT NULL,
                AppliedEventCount INTEGER NOT NULL,
                LastAppliedSortableUniqueId TEXT NULL,
                LastTraversedSortableUniqueId TEXT NULL,
                RecordedAtUtc TEXT NOT NULL,
                Phase TEXT NULL,
                LeaseExpiresAtUtc TEXT NULL,
                IsFaulted INTEGER NOT NULL DEFAULT 0,
                FaultMessage TEXT NULL,
                PRIMARY KEY (ServiceId, ProjectorName, ProjectorVersion, ClusterId)
            );
            CREATE INDEX IF NOT EXISTS IX_ProjectionStatuses_Projector
                ON dcb_projection_statuses(ServiceId, ProjectorName, ProjectorVersion, ClusterId);
            """;
        command.ExecuteNonQuery();
    }

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

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            EnsureProjectionStatusSchema(connection);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO dcb_projection_statuses
                    (ServiceId, ProjectorName, ProjectorVersion, ClusterId, ActivationId, Sequence,
                     AppliedEventCount, LastAppliedSortableUniqueId, LastTraversedSortableUniqueId, RecordedAtUtc,
                     Phase, LeaseExpiresAtUtc, IsFaulted, FaultMessage)
                SELECT @serviceId, @projectorName, @projectorVersion, @clusterId, @activationId, @sequence,
                       @appliedEventCount, @lastAppliedSortableUniqueId, @lastTraversedSortableUniqueId, @recordedAtUtc,
                       @phase, @leaseExpiresAtUtc, @isFaulted, @faultMessage
                WHERE @expectedSequence = 0
                ON CONFLICT(ServiceId, ProjectorName, ProjectorVersion, ClusterId)
                DO UPDATE SET
                    ActivationId = excluded.ActivationId,
                    Sequence = excluded.Sequence,
                    AppliedEventCount = excluded.AppliedEventCount,
                    LastAppliedSortableUniqueId = excluded.LastAppliedSortableUniqueId,
                    LastTraversedSortableUniqueId = excluded.LastTraversedSortableUniqueId,
                    RecordedAtUtc = excluded.RecordedAtUtc,
                    Phase = excluded.Phase,
                    LeaseExpiresAtUtc = excluded.LeaseExpiresAtUtc,
                    IsFaulted = excluded.IsFaulted,
                    FaultMessage = excluded.FaultMessage
                WHERE dcb_projection_statuses.Sequence = @expectedSequence
                  AND excluded.Sequence > dcb_projection_statuses.Sequence
                RETURNING ServiceId, ProjectorName, ProjectorVersion, ClusterId, ActivationId, Sequence,
                          AppliedEventCount, LastAppliedSortableUniqueId, LastTraversedSortableUniqueId, RecordedAtUtc,
                          Phase, LeaseExpiresAtUtc, IsFaulted, FaultMessage;
                """;
            AddStatusParameter(command, "@serviceId", heartbeat.ServiceId);
            AddStatusParameter(command, "@projectorName", heartbeat.ProjectorName);
            AddStatusParameter(command, "@projectorVersion", heartbeat.ProjectorVersion);
            AddStatusParameter(command, "@clusterId", heartbeat.ClusterId);
            AddStatusParameter(command, "@activationId", heartbeat.ActivationId);
            AddStatusParameter(command, "@sequence", heartbeat.Sequence);
            AddStatusParameter(command, "@appliedEventCount", heartbeat.AppliedEventCount);
            AddStatusParameter(command, "@lastAppliedSortableUniqueId", (object?)heartbeat.LastAppliedSortableUniqueId ?? DBNull.Value);
            AddStatusParameter(command, "@lastTraversedSortableUniqueId", (object?)heartbeat.LastTraversedSortableUniqueId ?? DBNull.Value);
            AddStatusParameter(command, "@recordedAtUtc", heartbeat.RecordedAtUtc.ToString("O"));
            AddStatusParameter(command, "@phase", heartbeat.Phase);
            AddStatusParameter(command, "@leaseExpiresAtUtc", (object?)heartbeat.LeaseExpiresAtUtc?.ToString("O") ?? DBNull.Value);
            AddStatusParameter(command, "@isFaulted", heartbeat.IsFaulted ? 1 : 0);
            AddStatusParameter(command, "@faultMessage", (object?)heartbeat.FaultMessage ?? DBNull.Value);
            AddStatusParameter(command, "@expectedSequence", expectedSequence);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ResultBox.FromValue(ProjectionStatusWriteResult.Success(ReadHeartbeat(reader)));
            }

            var current = await ReadCurrentHeartbeatAsync(
                connection,
                heartbeat,
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
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            EnsureProjectionStatusSchema(connection);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ServiceId, ProjectorName, ProjectorVersion, ClusterId, ActivationId, Sequence,
                       AppliedEventCount, LastAppliedSortableUniqueId, LastTraversedSortableUniqueId, RecordedAtUtc,
                       Phase, LeaseExpiresAtUtc, IsFaulted, FaultMessage
                FROM dcb_projection_statuses
                WHERE ServiceId = @serviceId
                  AND (@projectorName IS NULL OR ProjectorName = @projectorName)
                  AND (@projectorVersion IS NULL OR ProjectorVersion = @projectorVersion)
                ORDER BY ProjectorName, ProjectorVersion, ClusterId, ActivationId;
                """;
            AddStatusParameter(command, "@serviceId", CurrentServiceId);
            AddStatusParameter(command, "@projectorName", (object?)projectorName ?? DBNull.Value);
            AddStatusParameter(command, "@projectorVersion", (object?)projectorVersion ?? DBNull.Value);

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

    private static void AddStatusParameter(SqliteCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value);
    }

    private static ProjectionStatusHeartbeat ReadHeartbeat(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetInt64(5),
        reader.GetInt64(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        DateTimeOffset.Parse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture))
    {
        Phase = reader.IsDBNull(10) ? ProjectionStatusPhases.Unknown : reader.GetString(10),
        LeaseExpiresAtUtc = reader.IsDBNull(11)
            ? null
            : DateTimeOffset.Parse(reader.GetString(11), System.Globalization.CultureInfo.InvariantCulture),
        IsFaulted = !reader.IsDBNull(12) && reader.GetInt64(12) != 0,
        FaultMessage = reader.IsDBNull(13) ? null : reader.GetString(13)
    };

    private static async Task<ProjectionStatusHeartbeat?> ReadCurrentHeartbeatAsync(
        SqliteConnection connection,
        ProjectionStatusHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ServiceId, ProjectorName, ProjectorVersion, ClusterId, ActivationId, Sequence,
                   AppliedEventCount, LastAppliedSortableUniqueId, LastTraversedSortableUniqueId, RecordedAtUtc,
                   Phase, LeaseExpiresAtUtc, IsFaulted, FaultMessage
            FROM dcb_projection_statuses
            WHERE ServiceId = @serviceId AND ProjectorName = @projectorName
              AND ProjectorVersion = @projectorVersion AND ClusterId = @clusterId;
            """;
        AddStatusParameter(command, "@serviceId", heartbeat.ServiceId);
        AddStatusParameter(command, "@projectorName", heartbeat.ProjectorName);
        AddStatusParameter(command, "@projectorVersion", heartbeat.ProjectorVersion);
        AddStatusParameter(command, "@clusterId", heartbeat.ClusterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadHeartbeat(reader) : null;
    }
}

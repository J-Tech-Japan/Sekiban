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
                SwitchKind TEXT NULL,
                SwitchReason TEXT NULL,
                SwitchedAtUtc TEXT NULL,
                PRIMARY KEY (ServiceId, ProjectorName, ProjectorVersion, ClusterId)
            );
            CREATE INDEX IF NOT EXISTS IX_ProjectionStatuses_Projector
                ON dcb_projection_statuses(ServiceId, ProjectorName, ProjectorVersion, ClusterId);
            """;
        command.ExecuteNonQuery();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info('dcb_projection_statuses');";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }

        EnsureSwitchColumns(connection, columns);
    }

    private static void EnsureSwitchColumns(SqliteConnection connection, IReadOnlySet<string> columns)
    {
        if (!columns.Contains("SwitchKind"))
        {
            using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE dcb_projection_statuses ADD COLUMN SwitchKind TEXT NULL;";
            add.ExecuteNonQuery();
        }

        if (!columns.Contains("SwitchReason"))
        {
            using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE dcb_projection_statuses ADD COLUMN SwitchReason TEXT NULL;";
            add.ExecuteNonQuery();
        }

        if (!columns.Contains("SwitchedAtUtc"))
        {
            using var add = connection.CreateCommand();
            add.CommandText = "ALTER TABLE dcb_projection_statuses ADD COLUMN SwitchedAtUtc TEXT NULL;";
            add.ExecuteNonQuery();
        }
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
                     Phase, LeaseExpiresAtUtc, IsFaulted, FaultMessage, SwitchKind, SwitchReason, SwitchedAtUtc)
                SELECT @serviceId, @projectorName, @projectorVersion, @clusterId, @activationId, @sequence,
                       @appliedEventCount, @lastAppliedSortableUniqueId, @lastTraversedSortableUniqueId, @recordedAtUtc,
                       @phase, @leaseExpiresAtUtc, @isFaulted, @faultMessage, @switchKind, @switchReason, @switchedAtUtc
                WHERE @expectedSequence = 0
                   OR EXISTS (
                       SELECT 1
                       FROM dcb_projection_statuses
                       WHERE ServiceId = @serviceId
                         AND ProjectorName = @projectorName
                         AND ProjectorVersion = @projectorVersion
                         AND ClusterId = @clusterId)
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
                    FaultMessage = excluded.FaultMessage,
                    SwitchKind = excluded.SwitchKind,
                    SwitchReason = excluded.SwitchReason,
                    SwitchedAtUtc = excluded.SwitchedAtUtc
                WHERE dcb_projection_statuses.Sequence = @expectedSequence
                  AND excluded.Sequence > dcb_projection_statuses.Sequence
                RETURNING ServiceId, ProjectorName, ProjectorVersion, ClusterId, ActivationId, Sequence,
                          AppliedEventCount, LastAppliedSortableUniqueId, LastTraversedSortableUniqueId, RecordedAtUtc,
                          Phase, LeaseExpiresAtUtc, IsFaulted, FaultMessage, SwitchKind, SwitchReason, SwitchedAtUtc;
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
            AddStatusParameter(command, "@switchKind", (object?)heartbeat.SwitchKind ?? DBNull.Value);
            AddStatusParameter(command, "@switchReason", (object?)heartbeat.SwitchReason ?? DBNull.Value);
            AddStatusParameter(command, "@switchedAtUtc", (object?)heartbeat.SwitchedAtUtc?.ToString("O") ?? DBNull.Value);
            AddStatusParameter(command, "@expectedSequence", expectedSequence);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ResultBox.FromValue(ProjectionStatusWriteResult.Success(ReadHeartbeat(reader)));
            }

            await reader.DisposeAsync().ConfigureAwait(false);
            var current = await ReadCurrentHeartbeatAsync(
                connection,
                heartbeat,
                cancellationToken).ConfigureAwait(false);
            return ResultBox.FromValue(ProjectionStatusWriteResult.Rejected(
                heartbeat,
                expectedSequence,
                current,
                current is null && expectedSequence > 0
                    ? ProjectionStatusConflictReason.RowAbsent
                    : null));
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
                       Phase, LeaseExpiresAtUtc, IsFaulted, FaultMessage, SwitchKind, SwitchReason, SwitchedAtUtc
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
        FaultMessage = reader.IsDBNull(13) ? null : reader.GetString(13),
        SwitchKind = reader.IsDBNull(14) ? null : reader.GetString(14),
        SwitchReason = reader.IsDBNull(15) ? null : reader.GetString(15),
        SwitchedAtUtc = reader.IsDBNull(16)
            ? null
            : DateTimeOffset.Parse(reader.GetString(16), System.Globalization.CultureInfo.InvariantCulture)
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
                   Phase, LeaseExpiresAtUtc, IsFaulted, FaultMessage, SwitchKind, SwitchReason, SwitchedAtUtc
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

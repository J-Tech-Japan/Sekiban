using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ResultBoxes;
using Sekiban.Dcb.Subscriptions;

namespace Sekiban.Dcb.Postgres;

/// <summary>
///     PostgreSQL durable state boundary for opt-in durable subscribers. Runtime methods use DML only when the table
///     was explicitly provisioned; legacy mode may provision the table before the first state operation.
/// </summary>
public sealed class PostgresDurableSubscriptionStore : IDurableSubscriptionStore
{
    private const string Halted = nameof(DurableSubscriptionPhase.Halted);
    private readonly IDbContextFactory<SekibanDcbDbContext> _contextFactory;
    private readonly bool _legacyAutoProvision;

    public PostgresDurableSubscriptionStore(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        bool legacyAutoProvision = false)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _legacyAutoProvision = legacyAutoProvision;
    }

    public async Task<ResultBox<DurableSubscriptionState>> InitializeOrGetAsync(
        DurableSubscriptionIdentity identity,
        DurableSubscriptionStartPolicy startPolicy,
        string safeTail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO dcb_durable_subscriptions
                    (service_id, subscription_name, initialized, acknowledged_position, phase,
                     active_failure_count, owner_generation, created_at_utc, updated_at_utc)
                SELECT @service_id, @subscription_name, TRUE,
                       CASE WHEN CAST(@start_policy AS varchar) = 'FromBeginning' THEN NULL
                            WHEN latest.max_position IS NULL THEN NULL
                            ELSE LEAST(latest.max_position, @safe_tail) END,
                       @phase, 0, 0, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM (
                    SELECT MAX("SortableUniqueId") AS max_position
                    FROM dcb_events
                    WHERE "ServiceId" = @service_id
                ) latest
                ON CONFLICT (service_id, subscription_name) DO NOTHING;
                """;
            AddParameter(command, "service_id", identity.ServiceId);
            AddParameter(command, "subscription_name", identity.Name);
            AddParameter(command, "start_policy", startPolicy.ToString());
            AddParameter(command, "safe_tail", safeTail);
            AddParameter(command, "phase", nameof(DurableSubscriptionPhase.CatchingUp));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return await ReadCoreAsync(connection, identity, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<DurableSubscriptionState>(ex);
        }
    }

    public async Task<ResultBox<DurableSubscriptionState>> ReadAsync(
        DurableSubscriptionIdentity identity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            return await ReadCoreAsync(connection, identity, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<DurableSubscriptionState>(ex);
        }
    }

    public async Task<ResultBox<DurableSubscriptionLease>> TryAcquireAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // The row read is intentionally before the update. It makes an existing owner/generation observable and
            // ensures a missing row is a provider failure rather than an implicit subscription.
            await using var read = connection.CreateCommand();
            read.Transaction = transaction;
            read.CommandText = """
                SELECT initialized, phase, owner_id, owner_generation, lease_expires_at_utc
                FROM dcb_durable_subscriptions
                WHERE service_id = @service_id AND subscription_name = @subscription_name
                FOR UPDATE;
                """;
            AddParameter(read, "service_id", identity.ServiceId);
            AddParameter(read, "subscription_name", identity.Name);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Durable subscription {identity.ServiceId}/{identity.Name} is not initialized.");
            }

            var initialized = reader.GetBoolean(0);
            var phase = reader.GetString(1);
            var owner = reader.IsDBNull(2) ? null : reader.GetString(2);
            var generation = reader.GetInt64(3);
            var expiry = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4);
            await reader.DisposeAsync().ConfigureAwait(false);

            if (!initialized || string.Equals(phase, Halted, StringComparison.Ordinal))
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                var state = await ReadCoreAsync(connection, identity, cancellationToken).ConfigureAwait(false);
                return state.IsSuccess
                    ? ResultBox.FromValue(new DurableSubscriptionLease(false, state.GetValue()))
                    : ResultBox.Error<DurableSubscriptionLease>(state.GetException());
            }

            var available = owner is null || expiry is null || expiry <= DateTimeOffset.UtcNow;
            if (!available)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                var state = await ReadCoreAsync(connection, identity, cancellationToken).ConfigureAwait(false);
                return state.IsSuccess
                    ? ResultBox.FromValue(new DurableSubscriptionLease(false, state.GetValue() with { Phase = DurableSubscriptionPhase.Standby }))
                    : ResultBox.Error<DurableSubscriptionLease>(state.GetException());
            }

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE dcb_durable_subscriptions
                SET phase = @phase,
                    owner_id = @owner_id,
                    owner_generation = owner_generation + 1,
                    lease_expires_at_utc = CURRENT_TIMESTAMP + (@lease_seconds * INTERVAL '1 second'),
                    updated_at_utc = CURRENT_TIMESTAMP
                WHERE service_id = @service_id AND subscription_name = @subscription_name
                  AND initialized = TRUE AND phase <> @halted
                  AND (owner_id IS NULL OR lease_expires_at_utc <= CURRENT_TIMESTAMP)
                RETURNING service_id, subscription_name, initialized, acknowledged_position, phase,
                          active_failure_position, active_failure_count, active_failure_reason,
                          next_attempt_at_utc, last_halt_position, last_halt_at_utc, last_halt_reason,
                          owner_id, owner_generation, lease_expires_at_utc, updated_at_utc;
                """;
            AddCommonParameters(update, identity);
            AddParameter(update, "phase", nameof(DurableSubscriptionPhase.CatchingUp));
            AddParameter(update, "owner_id", ownerId);
            AddParameter(update, "lease_seconds", leaseDuration.TotalSeconds);
            AddParameter(update, "halted", Halted);
            await using var updated = await update.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await updated.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                await updated.DisposeAsync().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                var state = await ReadCoreAsync(connection, identity, cancellationToken).ConfigureAwait(false);
                return state.IsSuccess
                    ? ResultBox.FromValue(new DurableSubscriptionLease(false, state.GetValue() with { Phase = DurableSubscriptionPhase.Standby }))
                    : ResultBox.Error<DurableSubscriptionLease>(state.GetException());
            }

            var acquiredState = ReadState(updated);
            await updated.DisposeAsync().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return ResultBox.FromValue(new DurableSubscriptionLease(true, acquiredState));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<DurableSubscriptionLease>(ex);
        }
    }

    public async Task<ResultBox<bool>> RenewAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        long ownerGeneration,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dcb_durable_subscriptions
                SET lease_expires_at_utc = CURRENT_TIMESTAMP + (@lease_seconds * INTERVAL '1 second'),
                    updated_at_utc = CURRENT_TIMESTAMP
                WHERE service_id = @service_id AND subscription_name = @subscription_name
                  AND owner_id = @owner_id AND owner_generation = @owner_generation
                  AND phase <> @halted AND lease_expires_at_utc > CURRENT_TIMESTAMP;
                """;
            AddCommonParameters(command, identity);
            AddParameter(command, "owner_id", ownerId);
            AddParameter(command, "owner_generation", ownerGeneration);
            AddParameter(command, "lease_seconds", leaseDuration.TotalSeconds);
            AddParameter(command, "halted", Halted);
            return ResultBox.FromValue(await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<DurableSubscriptionState>> AcknowledgeAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        long ownerGeneration,
        string position,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dcb_durable_subscriptions
                SET acknowledged_position = @position,
                    phase = @phase,
                    active_failure_position = NULL,
                    active_failure_count = 0,
                    active_failure_reason = NULL,
                    next_attempt_at_utc = NULL,
                    updated_at_utc = CURRENT_TIMESTAMP
                WHERE service_id = @service_id AND subscription_name = @subscription_name
                  AND owner_id = @owner_id AND owner_generation = @owner_generation
                  AND phase <> @halted AND lease_expires_at_utc > CURRENT_TIMESTAMP
                  AND (acknowledged_position IS NULL OR acknowledged_position < @position)
                RETURNING service_id, subscription_name, initialized, acknowledged_position, phase,
                          active_failure_position, active_failure_count, active_failure_reason,
                          next_attempt_at_utc, last_halt_position, last_halt_at_utc, last_halt_reason,
                          owner_id, owner_generation, lease_expires_at_utc, updated_at_utc;
                """;
            AddCommonParameters(command, identity);
            AddParameter(command, "owner_id", ownerId);
            AddParameter(command, "owner_generation", ownerGeneration);
            AddParameter(command, "position", position);
            AddParameter(command, "phase", nameof(DurableSubscriptionPhase.CatchingUp));
            AddParameter(command, "halted", Halted);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The durable subscription lease was lost before acknowledgement.");
            }

            return ResultBox.FromValue(ReadState(reader));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<DurableSubscriptionState>(ex);
        }
    }

    public async Task<ResultBox<DurableSubscriptionState>> RecordFailureAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        long ownerGeneration,
        string position,
        string reason,
        bool conversionFailure,
        int maxHandlerAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dcb_durable_subscriptions
                SET active_failure_position = @position,
                    active_failure_count = active_failure_count + 1,
                    active_failure_reason = @reason,
                    next_attempt_at_utc = CASE
                        WHEN @conversion_failure OR active_failure_count + 1 >= @max_attempts THEN NULL
                        ELSE CURRENT_TIMESTAMP + (@retry_seconds * INTERVAL '1 second') END,
                    phase = CASE
                        WHEN @conversion_failure OR active_failure_count + 1 >= @max_attempts THEN @halted
                        ELSE @retrying END,
                    last_halt_position = CASE
                        WHEN @conversion_failure OR active_failure_count + 1 >= @max_attempts THEN @position
                        ELSE last_halt_position END,
                    last_halt_at_utc = CASE
                        WHEN @conversion_failure OR active_failure_count + 1 >= @max_attempts THEN CURRENT_TIMESTAMP
                        ELSE last_halt_at_utc END,
                    last_halt_reason = CASE
                        WHEN @conversion_failure OR active_failure_count + 1 >= @max_attempts THEN @reason
                        ELSE last_halt_reason END,
                    owner_id = CASE
                        WHEN @conversion_failure OR active_failure_count + 1 >= @max_attempts THEN NULL
                        ELSE owner_id END,
                    lease_expires_at_utc = CASE
                        WHEN @conversion_failure OR active_failure_count + 1 >= @max_attempts THEN NULL
                        ELSE lease_expires_at_utc END,
                    updated_at_utc = CURRENT_TIMESTAMP
                WHERE service_id = @service_id AND subscription_name = @subscription_name
                  AND owner_id = @owner_id AND owner_generation = @owner_generation
                  AND phase <> @halted AND lease_expires_at_utc > CURRENT_TIMESTAMP
                RETURNING service_id, subscription_name, initialized, acknowledged_position, phase,
                          active_failure_position, active_failure_count, active_failure_reason,
                          next_attempt_at_utc, last_halt_position, last_halt_at_utc, last_halt_reason,
                          owner_id, owner_generation, lease_expires_at_utc, updated_at_utc;
                """;
            AddCommonParameters(command, identity);
            AddParameter(command, "owner_id", ownerId);
            AddParameter(command, "owner_generation", ownerGeneration);
            AddParameter(command, "position", position);
            AddParameter(command, "reason", TruncateReason(reason));
            AddParameter(command, "conversion_failure", conversionFailure);
            AddParameter(command, "max_attempts", maxHandlerAttempts);
            AddParameter(command, "retry_seconds", retryDelay.TotalSeconds);
            AddParameter(command, "halted", Halted);
            AddParameter(command, "retrying", nameof(DurableSubscriptionPhase.Retrying));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The durable subscription lease was lost before failure recording.");
            }

            return ResultBox.FromValue(ReadState(reader));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<DurableSubscriptionState>(ex);
        }
    }

    public async Task<ResultBox<DurableSubscriptionState>> HaltAsync(
        DurableSubscriptionIdentity identity,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dcb_durable_subscriptions
                SET phase = @halted,
                    last_halt_at_utc = CURRENT_TIMESTAMP,
                    last_halt_reason = @reason,
                    owner_id = NULL,
                    lease_expires_at_utc = NULL,
                    updated_at_utc = CURRENT_TIMESTAMP
                WHERE service_id = @service_id AND subscription_name = @subscription_name
                RETURNING service_id, subscription_name, initialized, acknowledged_position, phase,
                          active_failure_position, active_failure_count, active_failure_reason,
                          next_attempt_at_utc, last_halt_position, last_halt_at_utc, last_halt_reason,
                          owner_id, owner_generation, lease_expires_at_utc, updated_at_utc;
                """;
            AddCommonParameters(command, identity);
            AddParameter(command, "halted", Halted);
            AddParameter(command, "reason", TruncateReason(reason));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The durable subscription does not exist.");
            }

            return ResultBox.FromValue(ReadState(reader));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<DurableSubscriptionState>(ex);
        }
    }

    public async Task<ResultBox<DurableSubscriptionState>> ResumeAsync(
        DurableSubscriptionIdentity identity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dcb_durable_subscriptions
                SET phase = @catching_up,
                    active_failure_position = NULL,
                    active_failure_count = 0,
                    active_failure_reason = NULL,
                    next_attempt_at_utc = NULL,
                    owner_id = NULL,
                    owner_generation = owner_generation + 1,
                    lease_expires_at_utc = NULL,
                    updated_at_utc = CURRENT_TIMESTAMP
                WHERE service_id = @service_id AND subscription_name = @subscription_name
                  AND phase = @halted
                RETURNING service_id, subscription_name, initialized, acknowledged_position, phase,
                          active_failure_position, active_failure_count, active_failure_reason,
                          next_attempt_at_utc, last_halt_position, last_halt_at_utc, last_halt_reason,
                          owner_id, owner_generation, lease_expires_at_utc, updated_at_utc;
                """;
            AddCommonParameters(command, identity);
            AddParameter(command, "catching_up", nameof(DurableSubscriptionPhase.CatchingUp));
            AddParameter(command, "halted", Halted);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return await ReadCoreAsync(connection, identity, cancellationToken).ConfigureAwait(false);
            }

            return ResultBox.FromValue(ReadState(reader));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<DurableSubscriptionState>(ex);
        }
    }

    public async Task<ResultBox<DurableSubscriptionState>> MarkIdleAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        long ownerGeneration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await CreateContextAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSchemaIfAllowedAsync(context, cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(context, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dcb_durable_subscriptions
                SET phase = @idle, updated_at_utc = CURRENT_TIMESTAMP
                WHERE service_id = @service_id AND subscription_name = @subscription_name
                  AND owner_id = @owner_id AND owner_generation = @owner_generation
                  AND phase <> @halted AND lease_expires_at_utc > CURRENT_TIMESTAMP
                RETURNING service_id, subscription_name, initialized, acknowledged_position, phase,
                          active_failure_position, active_failure_count, active_failure_reason,
                          next_attempt_at_utc, last_halt_position, last_halt_at_utc, last_halt_reason,
                          owner_id, owner_generation, lease_expires_at_utc, updated_at_utc;
                """;
            AddCommonParameters(command, identity);
            AddParameter(command, "owner_id", ownerId);
            AddParameter(command, "owner_generation", ownerGeneration);
            AddParameter(command, "idle", nameof(DurableSubscriptionPhase.Idle));
            AddParameter(command, "halted", Halted);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The durable subscription lease was lost before idle recording.");
            }

            return ResultBox.FromValue(ReadState(reader));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<DurableSubscriptionState>(ex);
        }
    }

    private async Task<SekibanDcbDbContext> CreateContextAsync(CancellationToken cancellationToken) =>
        await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

    private async Task EnsureSchemaIfAllowedAsync(SekibanDcbDbContext context, CancellationToken cancellationToken)
    {
        if (_legacyAutoProvision)
        {
            await PostgresDurableSubscriptionSchema.ProvisionAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<DbConnection> OpenConnectionAsync(
        SekibanDcbDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }

    private static async Task<ResultBox<DurableSubscriptionState>> ReadCoreAsync(
        DbConnection connection,
        DurableSubscriptionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT service_id, subscription_name, initialized, acknowledged_position, phase,
                   active_failure_position, active_failure_count, active_failure_reason,
                   next_attempt_at_utc, last_halt_position, last_halt_at_utc, last_halt_reason,
                   owner_id, owner_generation, lease_expires_at_utc, updated_at_utc
            FROM dcb_durable_subscriptions
            WHERE service_id = @service_id AND subscription_name = @subscription_name;
            """;
        AddCommonParameters(command, identity);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ResultBox.FromValue(ReadState(reader))
            : ResultBox.Error<DurableSubscriptionState>(new InvalidOperationException($"Durable subscription {identity.ServiceId}/{identity.Name} is not initialized."));
    }

    private static DurableSubscriptionState ReadState(DbDataReader reader) => new(
        new DurableSubscriptionIdentity(reader.GetString(0), reader.GetString(1)),
        reader.GetBoolean(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        Enum.Parse<DurableSubscriptionPhase>(reader.GetString(4), ignoreCase: false),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetInt32(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.IsDBNull(12) ? null : reader.GetString(12),
        reader.GetInt64(13),
        reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
        reader.GetFieldValue<DateTimeOffset>(15));

    private static void AddCommonParameters(DbCommand command, DurableSubscriptionIdentity identity)
    {
        AddParameter(command, "service_id", identity.ServiceId);
        AddParameter(command, "subscription_name", identity.Name);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string TruncateReason(string reason)
    {
        reason ??= "Unknown durable subscription failure.";
        return reason.Length <= 2048 ? reason : reason[..2048];
    }
}

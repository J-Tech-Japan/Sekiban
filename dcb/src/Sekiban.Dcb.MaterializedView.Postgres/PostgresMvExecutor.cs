using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.Postgres;

public sealed class PostgresMvExecutor : IMvExecutor
{
    private readonly IEventStore _eventStore;
    private readonly IEventTypes _eventTypes;
    private readonly ILogger<PostgresMvExecutor> _logger;
    private readonly MvOptions _options;
    private readonly IMvRegistryStore _registryStore;
    private readonly string _connectionString;
    private readonly IServiceIdProvider _serviceIdProvider;

    public PostgresMvExecutor(
        IEventStore eventStore,
        IEventTypes eventTypes,
        IServiceIdProvider serviceIdProvider,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<PostgresMvExecutor> logger,
        string connectionString)
    {
        _eventStore = eventStore;
        _eventTypes = eventTypes;
        _serviceIdProvider = serviceIdProvider;
        _registryStore = registryStore;
        _logger = logger;
        _connectionString = connectionString;
        _options = options.Value;
    }

    public async Task InitializeAsync(IMaterializedViewProjector projector, CancellationToken cancellationToken = default)
    {
        await _registryStore.EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);
        var serviceId = _serviceIdProvider.GetCurrentServiceId();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var initContext = new PostgresMvInitContext(connection, transaction, projector.ViewName, projector.ViewVersion, _options);
        await projector.InitializeAsync(initContext, cancellationToken).ConfigureAwait(false);

        foreach (var table in initContext.RegisteredTables)
        {
            await _registryStore.RegisterAsync(
                new MvRegistryEntry(
                    serviceId,
                    projector.ViewName,
                    projector.ViewVersion,
                    table.LogicalName,
                    table.PhysicalName,
                    MvStatus.CatchingUp,
                    CurrentPosition: null,
                    TargetPosition: null,
                    LastSortableUniqueId: null,
                    LastUpdated: DateTimeOffset.UtcNow,
                    Metadata: null),
                transaction,
                cancellationToken).ConfigureAwait(false);
        }

        var active = await _registryStore.GetActiveAsync(serviceId, projector.ViewName, cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            await _registryStore.SetActiveAsync(serviceId, projector.ViewName, projector.ViewVersion, transaction, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MvCatchUpResult> CatchUpOnceAsync(IMaterializedViewProjector projector, CancellationToken cancellationToken = default)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        var entries = await _registryStore.GetEntriesAsync(serviceId, projector.ViewName, projector.ViewVersion, cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeAsync(projector, cancellationToken).ConfigureAwait(false);
            entries = await _registryStore.GetEntriesAsync(serviceId, projector.ViewName, projector.ViewVersion, cancellationToken)
                .ConfigureAwait(false);
        }

        var currentPosition = entries
            .Select(entry => entry.CurrentPosition)
            .FirstOrDefault(position => !string.IsNullOrWhiteSpace(position));
        var readResult = await _eventStore.ReadAllSerializableEventsAsync(
            SortableUniqueId.NullableValue(currentPosition),
            _options.BatchSize).ConfigureAwait(false);

        if (!readResult.IsSuccess)
        {
            var exception = readResult.GetException();
            if (exception is NotSupportedException)
            {
                throw exception;
            }

            _logger.LogWarning(
                exception,
                "Failed to read events for materialized view {ViewName}/{ViewVersion}.",
                projector.ViewName,
                projector.ViewVersion);
            return new MvCatchUpResult(0, false);
        }

        var safeThreshold = CreateSafeThreshold(_options.SafeWindowMs);
        var appliedEvents = 0;
        var reachedUnsafeWindow = false;
        var batch = readResult.GetValue().OrderBy(serializable => serializable.SortableUniqueIdValue).ToList();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        foreach (var serializableEvent in batch)
        {
            if (!new SortableUniqueId(serializableEvent.SortableUniqueIdValue).IsEarlierThanOrEqual(safeThreshold))
            {
                reachedUnsafeWindow = true;
                break;
            }

            var eventResult = serializableEvent.ToEvent(_eventTypes);
            if (!eventResult.IsSuccess)
            {
                throw eventResult.GetException();
            }

            var ev = eventResult.GetValue();
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var applyContext = new PostgresMvApplyContext(connection, transaction, ev, serializableEvent.SortableUniqueIdValue);
            var statements = await projector.ApplyToViewAsync(ev, applyContext, cancellationToken).ConfigureAwait(false);
            foreach (var statement in statements)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        statement.Sql,
                        statement.Parameters,
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            await _registryStore.UpdatePositionAsync(
                serviceId,
                projector.ViewName,
                projector.ViewVersion,
                serializableEvent.SortableUniqueIdValue,
                transaction,
                cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            appliedEvents++;
        }

        return new MvCatchUpResult(appliedEvents, reachedUnsafeWindow);
    }

    private static SortableUniqueId CreateSafeThreshold(int safeWindowMs) =>
        new(SortableUniqueId.Generate(DateTime.UtcNow.AddMilliseconds(-safeWindowMs), Guid.Empty));
}

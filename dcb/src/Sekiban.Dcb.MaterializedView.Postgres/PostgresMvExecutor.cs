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

    public async Task InitializeAsync(
        IMaterializedViewProjector projector,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        await _registryStore.EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);
        serviceId = ResolveServiceId(serviceId);

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
                    AppliedEventVersion: 0,
                    LastAppliedSource: null,
                    LastAppliedAt: null,
                    LastStreamReceivedSortableUniqueId: null,
                    LastStreamReceivedAt: null,
                    LastStreamAppliedSortableUniqueId: null,
                    LastCatchUpSortableUniqueId: null,
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

    public async Task<MvCatchUpResult> CatchUpOnceAsync(
        IMaterializedViewProjector projector,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        serviceId = ResolveServiceId(serviceId);
        var entries = await _registryStore.GetEntriesAsync(serviceId, projector.ViewName, projector.ViewVersion, cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeAsync(projector, serviceId, cancellationToken).ConfigureAwait(false);
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
        var reachedUnsafeWindow = false;
        var batch = readResult.GetValue().OrderBy(serializable => serializable.SortableUniqueIdValue).ToList();

        if (batch.Count == 0)
        {
            return new MvCatchUpResult(0, false);
        }

        var safeBatch = new List<SerializableEvent>(batch.Count);
        foreach (var serializableEvent in batch)
        {
            if (!new SortableUniqueId(serializableEvent.SortableUniqueIdValue).IsEarlierThanOrEqual(safeThreshold))
            {
                reachedUnsafeWindow = true;
                break;
            }

            safeBatch.Add(serializableEvent);
        }

        if (safeBatch.Count == 0)
        {
            return new MvCatchUpResult(0, reachedUnsafeWindow);
        }

        var appliedEvents = await ApplySerializableEventsCoreAsync(
                projector,
                safeBatch,
                serviceId,
                MvApplySource.CatchUp,
                cancellationToken)
            .ConfigureAwait(false);

        var lastAppliedSortableUniqueId = appliedEvents > 0
            ? safeBatch[^1].SortableUniqueIdValue
            : null;

        return new MvCatchUpResult(appliedEvents, reachedUnsafeWindow, lastAppliedSortableUniqueId);
    }

    public async Task<int> ApplySerializableEventsAsync(
        IMaterializedViewProjector projector,
        IReadOnlyList<SerializableEvent> events,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        serviceId = ResolveServiceId(serviceId);
        return await ApplySerializableEventsCoreAsync(projector, events, serviceId, MvApplySource.Stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ApplySerializableEventsCoreAsync(
        IMaterializedViewProjector projector,
        IReadOnlyList<SerializableEvent> events,
        string serviceId,
        MvApplySource source,
        CancellationToken cancellationToken)
    {
        var entries = await _registryStore.GetEntriesAsync(serviceId, projector.ViewName, projector.ViewVersion, cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeAsync(projector, serviceId, cancellationToken).ConfigureAwait(false);
            entries = await _registryStore.GetEntriesAsync(serviceId, projector.ViewName, projector.ViewVersion, cancellationToken)
                .ConfigureAwait(false);
        }

        var currentPosition = entries
            .Select(entry => entry.CurrentPosition)
            .FirstOrDefault(position => !string.IsNullOrWhiteSpace(position));
        var orderedEvents = events
            .GroupBy(serializableEvent => serializableEvent.Id)
            .Select(group => group.OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal).Last())
            .Where(serializableEvent =>
                string.IsNullOrWhiteSpace(currentPosition) ||
                string.Compare(serializableEvent.SortableUniqueIdValue, currentPosition, StringComparison.Ordinal) > 0)
            .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        if (orderedEvents.Count == 0)
        {
            return 0;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var appliedEvents = 0;

        foreach (var serializableEvent in orderedEvents)
        {
            await ApplySerializableEventAsync(connection, projector, serviceId, serializableEvent, source, cancellationToken)
                .ConfigureAwait(false);
            appliedEvents += 1;
        }

        return appliedEvents;
    }

    private async Task ApplySerializableEventAsync(
        NpgsqlConnection connection,
        IMaterializedViewProjector projector,
        string serviceId,
        SerializableEvent serializableEvent,
        MvApplySource source,
        CancellationToken cancellationToken)
    {
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
            new MvPositionUpdate(
                serviceId,
                projector.ViewName,
                projector.ViewVersion,
                serializableEvent.SortableUniqueIdValue,
                source),
            transaction: transaction,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private string ResolveServiceId(string? serviceId) =>
        string.IsNullOrWhiteSpace(serviceId)
            ? _serviceIdProvider.GetCurrentServiceId()
            : serviceId;

    private static SortableUniqueId CreateSafeThreshold(int safeWindowMs) =>
        new(SortableUniqueId.Generate(DateTime.UtcNow.AddMilliseconds(-safeWindowMs), Guid.Empty));
}

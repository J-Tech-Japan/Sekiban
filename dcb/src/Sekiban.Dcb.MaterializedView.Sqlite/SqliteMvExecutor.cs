using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

public sealed class SqliteMvExecutor : IMvExecutor
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<SqliteMvExecutor> _logger;
    private readonly MvOptions _options;
    private readonly IMvRegistryStore _registryStore;
    private readonly string _connectionString;
    private readonly IServiceIdProvider _serviceIdProvider;

    public SqliteMvExecutor(
        IEventStore eventStore,
        IServiceIdProvider serviceIdProvider,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<SqliteMvExecutor> logger,
        string connectionString)
    {
        _eventStore = eventStore;
        _serviceIdProvider = serviceIdProvider;
        _registryStore = registryStore;
        _logger = logger;
        _connectionString = connectionString;
        _options = options.Value;
    }

    public async Task InitializeAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        await _registryStore.EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);
        serviceId = ResolveServiceId(serviceId);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var bindings = new MvTableBindings(host.ViewName, host.ViewVersion, _options);
        var statements = await host.InitializeAsync(bindings, cancellationToken).ConfigureAwait(false);
        foreach (var statement in statements)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    statement.Sql,
                    ToDynamicParameters(statement.Parameters),
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        foreach (var table in bindings.Tables)
        {
            await _registryStore.RegisterAsync(
                new MvRegistryEntry
                {
                    ServiceId = serviceId,
                    ViewName = host.ViewName,
                    ViewVersion = host.ViewVersion,
                    LogicalTable = table.LogicalName,
                    PhysicalTable = table.PhysicalName,
                    Status = MvStatus.CatchingUp,
                    AppliedEventVersion = 0,
                    LastUpdated = DateTimeOffset.UtcNow
                },
                transaction,
                cancellationToken).ConfigureAwait(false);
        }

        var active = await _registryStore.GetActiveAsync(serviceId, host.ViewName, cancellationToken).ConfigureAwait(false);
        if (active is null)
        {
            await _registryStore.SetActiveAsync(serviceId, host.ViewName, host.ViewVersion, transaction, cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MvCatchUpResult> CatchUpOnceAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        serviceId = ResolveServiceId(serviceId);
        var entries = await _registryStore.GetEntriesAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
            entries = await _registryStore.GetEntriesAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken)
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
                host.ViewName,
                host.ViewVersion);
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
                host,
                safeBatch,
                serviceId,
                MvApplySource.CatchUp,
                cancellationToken)
            .ConfigureAwait(false);

        var lastAppliedSortableUniqueId = appliedEvents > 0
            ? safeBatch[appliedEvents - 1].SortableUniqueIdValue
            : null;

        reachedUnsafeWindow |= appliedEvents < safeBatch.Count;

        return new MvCatchUpResult(appliedEvents, reachedUnsafeWindow, lastAppliedSortableUniqueId);
    }

    public async Task<int> ApplySerializableEventsAsync(
        IMvApplyHost host,
        IReadOnlyList<SerializableEvent> events,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        serviceId = ResolveServiceId(serviceId);
        return await ApplySerializableEventsCoreAsync(host, events, serviceId, MvApplySource.Stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ApplySerializableEventsCoreAsync(
        IMvApplyHost host,
        IReadOnlyList<SerializableEvent> events,
        string serviceId,
        MvApplySource source,
        CancellationToken cancellationToken)
    {
        var entries = await _registryStore.GetEntriesAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
            entries = await _registryStore.GetEntriesAsync(serviceId, host.ViewName, host.ViewVersion, cancellationToken)
                .ConfigureAwait(false);
        }

        var currentPosition = entries
            .Select(entry => entry.CurrentPosition)
            .FirstOrDefault(position => !string.IsNullOrWhiteSpace(position));
        var orderedEvents = events
            .GroupBy(serializableEvent => serializableEvent.SortableUniqueIdValue)
            .Select(group => group.First())
            .Where(serializableEvent =>
                source == MvApplySource.Stream ||
                string.IsNullOrWhiteSpace(currentPosition) ||
                string.Compare(serializableEvent.SortableUniqueIdValue, currentPosition, StringComparison.Ordinal) > 0)
            .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        if (orderedEvents.Count == 0)
        {
            return 0;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var appliedEvents = 0;
        var bindings = CreateBindings(host, entries);

        foreach (var serializableEvent in orderedEvents)
        {
            var applied = await ApplySerializableEventAsync(
                    connection,
                    host,
                    serviceId,
                    bindings,
                    serializableEvent,
                    currentPosition,
                    source,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!applied)
            {
                break;
            }

            appliedEvents += 1;
        }

        return appliedEvents;
    }

    private async Task<bool> ApplySerializableEventAsync(
        SqliteConnection connection,
        IMvApplyHost host,
        string serviceId,
        MvTableBindings bindings,
        SerializableEvent serializableEvent,
        string? currentPosition,
        MvApplySource source,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var queryPort = new SqliteMvApplyQueryPort(connection, transaction);
        var statements = await host.ApplyEventAsync(
            serializableEvent,
            bindings,
            queryPort,
            serializableEvent.SortableUniqueIdValue,
            cancellationToken).ConfigureAwait(false);
        var affectedRows = 0;
        foreach (var statement in statements)
        {
            affectedRows += await connection.ExecuteAsync(
                new CommandDefinition(
                    statement.Sql,
                    ToDynamicParameters(statement.Parameters),
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if (source == MvApplySource.Stream && statements.Count > 0 && affectedRows == 0)
        {
            if (!string.IsNullOrWhiteSpace(currentPosition) &&
                string.Compare(serializableEvent.SortableUniqueIdValue, currentPosition, StringComparison.Ordinal) <= 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }

            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await _registryStore.UpdatePositionAsync(
            new MvPositionUpdate(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                serializableEvent.SortableUniqueIdValue,
                source),
            transaction: transaction,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private string ResolveServiceId(string? serviceId) =>
        string.IsNullOrWhiteSpace(serviceId)
            ? _serviceIdProvider.GetCurrentServiceId()
            : serviceId;

    private MvTableBindings CreateBindings(IMvApplyHost host, IReadOnlyList<MvRegistryEntry> entries)
    {
        var bindings = new MvTableBindings(host.ViewName, host.ViewVersion, _options);
        foreach (var entry in entries)
        {
            bindings.RegisterTable(entry.LogicalTable, entry.PhysicalTable);
        }

        return bindings;
    }

    private static DynamicParameters ToDynamicParameters(IReadOnlyList<MvParam> parameters)
    {
        var dynamicParameters = new DynamicParameters();
        foreach (var parameter in parameters)
        {
            dynamicParameters.Add(parameter.Name, MvParamConverter.ToClrValue(parameter));
        }

        return dynamicParameters;
    }

    private static SortableUniqueId CreateSafeThreshold(int safeWindowMs) =>
        new(SortableUniqueId.Generate(DateTime.UtcNow.AddMilliseconds(-safeWindowMs), Guid.Empty));

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition("PRAGMA synchronous=NORMAL;", cancellationToken: cancellationToken)).ConfigureAwait(false);
        return connection;
    }
}

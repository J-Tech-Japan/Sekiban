using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.MySql;

public sealed class MySqlMvExecutor : IMvExecutor
{
    private readonly IEventStoreFactory? _eventStoreFactory;
    private readonly IEventStore? _legacyEventStore;
    private readonly IServiceIdProvider? _legacyServiceIdProvider;
    private readonly ILogger<MySqlMvExecutor> _logger;
    private readonly MvOptions _options;
    private readonly IMvRegistryStore _registryStore;
    private readonly string _connectionString;

    /// <summary>
    /// Creates the legacy single-service compatibility executor over an aggregate event store.
    /// Multi-service hosts should use the <see cref="IEventStoreFactory"/> constructor.
    /// </summary>
    public MySqlMvExecutor(
        IEventStore eventStore,
        IServiceIdProvider serviceIdProvider,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<MySqlMvExecutor> logger,
        string connectionString)
    {
        _legacyEventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _legacyServiceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _registryStore = registryStore;
        _logger = logger;
        _connectionString = connectionString;
        _options = options.Value;
    }

    /// <summary>Creates an executor whose event reads use the standard service-scoped factory.</summary>
    public MySqlMvExecutor(
        IEventStoreFactory eventStoreFactory,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<MySqlMvExecutor> logger,
        string connectionString)
    {
        _eventStoreFactory = eventStoreFactory ?? throw new ArgumentNullException(nameof(eventStoreFactory));
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
        serviceId = ResolveServiceId(serviceId);
        await _registryStore.EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        IEventStore eventStore;
        if (_eventStoreFactory is not null)
        {
            eventStore = _eventStoreFactory.CreateForService(serviceId) ??
                throw new InvalidOperationException($"The event-store factory returned null for ServiceId '{serviceId}'.");
        }
        else
        {
            eventStore = _legacyEventStore ??
                throw new InvalidOperationException("No legacy event store is registered for materialized views.");
        }

        var readResult = await eventStore.ReadAllSerializableEventsAsync(
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
        serviceId = ResolveServiceId(serviceId);
        if (events.Count == 0)
        {
            return 0;
        }

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

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        MySqlConnection connection,
        IMvApplyHost host,
        string serviceId,
        MvTableBindings bindings,
        SerializableEvent serializableEvent,
        string? currentPosition,
        MvApplySource source,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var queryPort = new MySqlMvApplyQueryPort(connection, transaction);
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

    private string ResolveServiceId(string? requestedServiceId)
    {
        var callerSuppliedServiceId = !string.IsNullOrWhiteSpace(requestedServiceId);
        var resolvedServiceId = requestedServiceId;
        if (string.IsNullOrWhiteSpace(resolvedServiceId))
        {
            resolvedServiceId = _options.ServiceId;
        }

        if (string.IsNullOrWhiteSpace(resolvedServiceId) && _eventStoreFactory is null)
        {
            resolvedServiceId = _legacyServiceIdProvider?.GetCurrentServiceId();
        }

        if (string.IsNullOrWhiteSpace(resolvedServiceId))
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlMvExecutor)} requires an explicit non-empty ServiceId. Configure MvOptions.ServiceId or pass the service id at the caller boundary.");
        }

        var normalizedServiceId = ServiceIdValidator.NormalizeAndValidate(resolvedServiceId);
        if (string.Equals(normalizedServiceId, DefaultServiceIdProvider.DefaultServiceId, StringComparison.Ordinal) &&
            !_options.AllowDefaultServiceId)
        {
            throw new InvalidOperationException(
                $"{nameof(MySqlMvExecutor)} cannot use the implicit default ServiceId. Opt into the named single-service compatibility option AllowDefaultServiceId or provide an explicit non-default service.");
        }

        if (callerSuppliedServiceId && !string.IsNullOrWhiteSpace(_options.ServiceId))
        {
            var configuredServiceId = ServiceIdValidator.NormalizeAndValidate(_options.ServiceId);
            if (!string.Equals(configuredServiceId, normalizedServiceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{nameof(MySqlMvExecutor)} requested ServiceId '{normalizedServiceId}', but MvOptions is bound to '{configuredServiceId}'.");
            }
        }

        if (_legacyEventStore is not null)
        {
            var legacyServiceId = ServiceIdValidator.NormalizeAndValidate(_legacyServiceIdProvider!.GetCurrentServiceId());
            if (!string.Equals(legacyServiceId, normalizedServiceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{nameof(MySqlMvExecutor)} requested ServiceId '{normalizedServiceId}', but the legacy aggregate event store is bound to '{legacyServiceId}'. Register IEventStoreFactory for service-scoped MV reads.");
            }
        }

        return normalizedServiceId;
    }

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
}

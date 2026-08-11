using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>
/// Database-neutral materialized-view execution workflow.
/// Provider executors retain their own event-store factory and perform service validation/source selection
/// at each public operation boundary before delegating database work here.
/// </summary>
public abstract class MvExecutorBase<TConnection> : IMvExecutor, IMvActivationExecutor
    where TConnection : DbConnection
{
    private readonly IMvRegistryStore _registryStore;
    private readonly ILogger _logger;
    private readonly MvOptions _options;
    private readonly string _connectionString;
    private readonly IServiceIdProvider? _legacyServiceIdProvider;

    protected MvExecutorBase(
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger logger,
        string connectionString,
        IServiceIdProvider? legacyServiceIdProvider = null)
    {
        _registryStore = registryStore;
        _logger = logger;
        _options = options.Value;
        _connectionString = connectionString;
        _legacyServiceIdProvider = legacyServiceIdProvider;
    }

    protected IMvRegistryStore RegistryStore => _registryStore;

    protected MvOptions Options => _options;

    protected string ConnectionString => _connectionString;

    // These helpers validate dependencies and move database-neutral workflow only. They never resolve or cache an
    // event store; each provider executor keeps its own factory field and selects its service-scoped source.
    protected static (IEventStore EventStore, IServiceIdProvider ServiceIdProvider) RequireLegacyCompatibilityDependencies(
        IEventStore eventStore,
        IServiceIdProvider serviceIdProvider)
    {
        return (
            eventStore ?? throw new ArgumentNullException(nameof(eventStore)),
            serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider)));
    }

    protected static IEventStoreFactory RequireEventStoreFactory(IEventStoreFactory eventStoreFactory) =>
        eventStoreFactory ?? throw new ArgumentNullException(nameof(eventStoreFactory));

    protected static IEventStore RequireSelectedEventStore(
        IEventStore? eventStore,
        string exactServiceId,
        bool selectedFromFactory)
    {
        if (eventStore is not null)
        {
            return eventStore;
        }

        throw selectedFromFactory
            ? new InvalidOperationException($"The event-store factory returned null for ServiceId '{exactServiceId}'.")
            : new InvalidOperationException("No legacy event store is registered for materialized views.");
    }

    protected string ValidateServiceId(
        string? requestedServiceId,
        IServiceIdProvider? legacyServiceIdProvider,
        string executorName) =>
        MvServiceIdValidation.Validate(requestedServiceId, _options, legacyServiceIdProvider, executorName);

    protected Task InitializeAtBoundaryAsync(
        IMvApplyHost host,
        string? requestedServiceId,
        CancellationToken cancellationToken) =>
        InitializeCoreAsync(
            host,
            ValidateServiceId(requestedServiceId, _legacyServiceIdProvider, GetType().Name),
            cancellationToken);

    protected Task<int> ApplySerializableEventsAtBoundaryAsync(
        IMvApplyHost host,
        IReadOnlyList<SerializableEvent> events,
        string? requestedServiceId,
        CancellationToken cancellationToken) =>
        ApplyStreamEventsAtBoundaryAsync(
            host,
            events,
            ValidateServiceId(requestedServiceId, _legacyServiceIdProvider, GetType().Name),
            cancellationToken);

    protected string ValidateServiceIdAtBoundary(string? requestedServiceId) =>
        ValidateServiceId(requestedServiceId, _legacyServiceIdProvider, GetType().Name);

    public Task InitializeAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default) =>
        InitializeAtBoundaryAsync(host, serviceId, cancellationToken);

    public Task<int> ApplySerializableEventsAsync(
        IMvApplyHost host,
        IReadOnlyList<SerializableEvent> events,
        string? serviceId = null,
        CancellationToken cancellationToken = default) =>
        ApplySerializableEventsAtBoundaryAsync(host, events, serviceId, cancellationToken);

    public abstract Task<MvCatchUpResult> CatchUpOnceAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default);

    protected abstract Task<TConnection> OpenConnectionAsync(CancellationToken cancellationToken);

    protected abstract IMvApplyQueryPort CreateQueryPort(
        TConnection connection,
        IDbTransaction transaction);

    protected abstract Task<int> ExecuteSqlAsync(
        TConnection connection,
        string sql,
        IReadOnlyList<MvParam> parameters,
        IDbTransaction transaction,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Provider-owned service selection. The caller validates the identity before this method is reached; each
    ///     concrete executor keeps the factory field and performs its direct CreateForService call here.
    /// </summary>
    protected abstract IEventStore SelectEventStoreForService(string exactServiceId);

    public async Task<MvCheckpointTruth> CaptureTargetCheckpointAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ValidateServiceIdAtBoundary(serviceId);
        var entries = await _registryStore.GetEntriesAsync(
                exactServiceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeCoreAsync(host, exactServiceId, cancellationToken).ConfigureAwait(false);
        }

        var target = await CaptureTargetCheckpointFromStoreAsync(SelectEventStoreForService(exactServiceId))
            .ConfigureAwait(false);
        await _registryStore.SetTargetCheckpointAsync(
                exactServiceId,
                host.ViewName,
                host.ViewVersion,
                target,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return target;
    }

    public async Task<MvActivationResult> TryActivateAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ValidateServiceIdAtBoundary(serviceId);
        var entries = await _registryStore.GetEntriesAsync(
                exactServiceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var active = await _registryStore.GetActiveAsync(
                exactServiceId,
                host.ViewName,
                cancellationToken)
            .ConfigureAwait(false);
        var (eligibility, request) = MvActivationEligibility.Evaluate(
            exactServiceId,
            host.ViewName,
            host.ViewVersion,
            entries,
            active);
        if (!eligibility.IsEligible || request is null)
        {
            return MvActivationResult.Rejected(eligibility.FailureReason, eligibility.Message);
        }

        try
        {
            return await _registryStore.TryActivateAsync(
                    request,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(
                ex,
                "Atomic materialized-view activation is not available for {ViewName}/{ViewVersion}.",
                host.ViewName,
                host.ViewVersion);
            return MvActivationResult.Rejected(MvActivationFailureReason.ProviderFailure, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Atomic materialized-view activation failed without changing the candidate contract for {ViewName}/{ViewVersion}.",
                host.ViewName,
                host.ViewVersion);
            return MvActivationResult.Rejected(MvActivationFailureReason.ProviderFailure, ex.Message);
        }
    }

    protected async Task InitializeCoreAsync(
        IMvApplyHost host,
        string serviceId,
        CancellationToken cancellationToken)
    {
        await _registryStore.EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var bindings = new MvTableBindings(host.ViewName, host.ViewVersion, _options);
        var statements = await host.InitializeAsync(bindings, cancellationToken).ConfigureAwait(false);
        foreach (var statement in statements)
        {
            await ExecuteSqlAsync(
                    connection,
                    statement.Sql,
                    statement.Parameters,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);
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
                        CurrentCheckpointTruth = MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.NotObserved),
                        TargetCheckpointTruth = MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.NotObserved),
                        AppliedEventVersion = 0,
                        LastUpdated = DateTimeOffset.UtcNow
                    },
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected async Task<MvProjectionStatusSnapshot> GetCurrentStatusAsync(
        IMvApplyHost host,
        string serviceId,
        CancellationToken cancellationToken)
    {
        var entries = await _registryStore.GetEntriesAsync(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeCoreAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
            entries = await _registryStore.GetEntriesAsync(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return MvProjectionStatusSnapshot.FromEntries(entries);
    }

    protected async Task<MvCatchUpResult> CatchUpFromStoreAsync(
        IMvApplyHost host,
        string serviceId,
        IEventStore? eventStore,
        bool selectedFromFactory,
        CancellationToken cancellationToken)
    {
        var selectedEventStore = RequireSelectedEventStore(eventStore, serviceId, selectedFromFactory);
        await EnsureTargetCheckpointCapturedAsync(
                host,
                serviceId,
                selectedEventStore,
                cancellationToken)
            .ConfigureAwait(false);
        var currentStatus = await GetCurrentStatusAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
        // Never use the nullable legacy position for a decisive read boundary. Old rows may contain a position but
        // have no provenance, so they remain Unknown and are replayed fail-closed from the beginning.
        var currentPosition = currentStatus.CurrentCheckpointTruth.IsKnown
            ? currentStatus.CurrentCheckpointTruth.PositionValue
            : null;
        var readResult = await selectedEventStore.ReadAllSerializableEventsAsync(
                SortableUniqueId.NullableValue(currentPosition),
                _options.BatchSize)
            .ConfigureAwait(false);
        var result = await CompleteCatchUpAsync(host, serviceId, readResult, currentStatus, cancellationToken)
            .ConfigureAwait(false);
        if (result.AppliedEvents == 0 || result.ReachedUnsafeWindow)
        {
            var status = await TryActivateIfEligibleAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
            result = result with
            {
                ProjectionStatus = result.ProjectionStatus is { } snapshot
                    ? snapshot with { Status = status }
                    : currentStatus with { Status = status }
            };
        }

        return result;
    }

    /// <summary>
    ///     Captures the source head once for a candidate until it has authoritative target provenance. A failed
    ///     capture is persisted as Unknown and retried on the next catch-up turn; it never authorizes activation.
    /// </summary>
    private async Task EnsureTargetCheckpointCapturedAsync(
        IMvApplyHost host,
        string serviceId,
        IEventStore eventStore,
        CancellationToken cancellationToken)
    {
        var entries = await _registryStore.GetEntriesAsync(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeCoreAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
            entries = await _registryStore.GetEntriesAsync(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (entries.Count == 0 || entries.All(entry =>
                entry.TargetCheckpointTruth.IsKnown &&
                entry.TargetCheckpointTruth.Provenance?.Kind == MvCheckpointProvenanceKind.AuthoritativeTargetCapture))
        {
            return;
        }

        var target = await CaptureTargetCheckpointFromStoreAsync(eventStore).ConfigureAwait(false);
        await _registryStore.SetTargetCheckpointAsync(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                target,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     The direct executor/worker path and the Orleans path share this promotion boundary. A candidate may move
    ///     from CatchingUp to Ready only after all non-lifecycle eligibility predicates pass. The final pointer write
    ///     is always the provider transaction's expected-active/generation CAS.
    /// </summary>
    private async Task<MvStatus> TryActivateIfEligibleAsync(
        IMvApplyHost host,
        string serviceId,
        CancellationToken cancellationToken)
    {
        var entries = await _registryStore.GetEntriesAsync(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var active = await _registryStore.GetActiveAsync(
                serviceId,
                host.ViewName,
                cancellationToken)
            .ConfigureAwait(false);

        // Retired and faulted rows are terminal lifecycle states. Only the normal catch-up/ready transition may be
        // promoted by this automatic initial-activation path.
        if (entries.Count == 0 || entries.Any(entry =>
                entry.Status is not (MvStatus.CatchingUp or MvStatus.Ready)))
        {
            return entries.FirstOrDefault()?.Status ?? MvStatus.Initializing;
        }

        // Evaluate the full contract before changing lifecycle state. The evaluator itself intentionally accepts
        // only Ready, so use an immutable Ready projection only for this preflight; the real request is re-read after
        // the status mutation and is checked again by the provider transaction.
        var readyEntries = entries
            .Select(entry => entry with { Status = MvStatus.Ready })
            .ToList();
        var (preflight, _) = MvActivationEligibility.Evaluate(
            serviceId,
            host.ViewName,
            host.ViewVersion,
            readyEntries,
            active);
        if (!preflight.IsEligible)
        {
            return entries[0].Status;
        }

        if (entries.Any(entry => entry.Status != MvStatus.Ready))
        {
            await _registryStore.UpdateStatusAsync(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    MvStatus.Ready,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        // Catch-up may authorize the first generation, but a prepared parallel generation never moves the serving
        // pointer implicitly. Forward and reverse transitions are explicit coordinator operations.
        if (active is not null)
        {
            return MvStatus.Ready;
        }

        var result = await TryActivateAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded && !result.IsConflict && result.FailureReason != MvActivationFailureReason.AlreadyActive)
        {
            _logger.LogWarning(
                "Materialized-view candidate was not activated for {ViewName}/{ViewVersion}: {Reason} ({Message})",
                host.ViewName,
                host.ViewVersion,
                result.FailureReason,
                result.Message);
        }

        return result.Succeeded || result.FailureReason == MvActivationFailureReason.AlreadyActive
            ? MvStatus.Active
            : MvStatus.Ready;
    }

    protected Task<int> ApplyStreamEventsAtBoundaryAsync(
        IMvApplyHost host,
        IReadOnlyList<SerializableEvent> events,
        string exactServiceId,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return Task.FromResult(0);
        }

        return ApplySerializableEventsCoreAsync(
            host,
            events,
            exactServiceId,
            MvApplySource.Stream,
            cancellationToken);
    }

    protected async Task<MvCatchUpResult> CompleteCatchUpAsync(
        IMvApplyHost host,
        string serviceId,
        ResultBox<IEnumerable<SerializableEvent>> readResult,
        MvProjectionStatusSnapshot currentStatus,
        CancellationToken cancellationToken)
    {
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
            return new MvCatchUpResult(0, false)
            {
                ProjectionStatus = currentStatus with { Status = MvStatus.Faulted }
            };
        }

        var safeThreshold = CreateSafeThreshold(_options.SafeWindowMs);
        var reachedUnsafeWindow = false;
        var batch = readResult.GetValue().OrderBy(serializable => serializable.SortableUniqueIdValue).ToList();

        if (batch.Count == 0)
        {
            await _registryStore.UpdatePositionAsync(
                    new MvPositionUpdate(
                        serviceId,
                        host.ViewName,
                        host.ViewVersion,
                        SortableUniqueId.MinValue.Value,
                        MvApplySource.CatchUp,
                        AppliedEventVersionDelta: 0)
                    {
                        CheckpointTruth = MvCheckpointTruth.KnownZero()
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var emptyTruth = currentStatus.CurrentCheckpointTruth.IsKnown &&
                !currentStatus.CurrentCheckpointTruth.IsKnownZero
                    ? currentStatus.CurrentCheckpointTruth
                    : MvCheckpointTruth.KnownZero();
            return new MvCatchUpResult(0, false)
            {
                ProjectionStatus = currentStatus with
                {
                    CurrentCheckpointTruth = emptyTruth,
                    Status = MvStatus.CatchingUp
                }
            };
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
            return new MvCatchUpResult(0, reachedUnsafeWindow)
            {
                ProjectionStatus = currentStatus with { Status = MvStatus.CatchingUp }
            };
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
        var truth = !string.IsNullOrWhiteSpace(lastAppliedSortableUniqueId)
            ? MvCheckpointTruth.Known(
                new SortableUniqueId(lastAppliedSortableUniqueId),
                MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp))
            : currentStatus.CurrentCheckpointTruth;
        return new MvCatchUpResult(appliedEvents, reachedUnsafeWindow, lastAppliedSortableUniqueId)
        {
            ProjectionStatus = currentStatus with
            {
                CurrentCheckpointTruth = truth,
                Status = MvStatus.CatchingUp,
                AppliedEventCount = currentStatus.AppliedEventCount + appliedEvents
            }
        };
    }

    protected async Task<int> ApplySerializableEventsCoreAsync(
        IMvApplyHost host,
        IReadOnlyList<SerializableEvent> events,
        string serviceId,
        MvApplySource source,
        CancellationToken cancellationToken)
    {
        var entries = await _registryStore.GetEntriesAsync(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0)
        {
            await InitializeCoreAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
            entries = await _registryStore.GetEntriesAsync(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var currentPosition = entries
            .Select(entry => entry.CurrentCheckpointTruth.IsKnown ? entry.CurrentCheckpointTruth.PositionValue : null)
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
        TConnection connection,
        IMvApplyHost host,
        string serviceId,
        MvTableBindings bindings,
        SerializableEvent serializableEvent,
        string? currentPosition,
        MvApplySource source,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var queryPort = CreateQueryPort(connection, transaction);
        var statements = await host.ApplyEventAsync(
                serializableEvent,
                bindings,
                queryPort,
                serializableEvent.SortableUniqueIdValue,
                cancellationToken)
            .ConfigureAwait(false);
        var affectedRows = 0;
        foreach (var statement in statements)
        {
            affectedRows += await ExecuteSqlAsync(
                    connection,
                    statement.Sql,
                    statement.Parameters,
                    transaction,
                    cancellationToken)
                .ConfigureAwait(false);
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
                transaction,
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    protected MvTableBindings CreateBindings(IMvApplyHost host, IReadOnlyList<MvRegistryEntry> entries)
    {
        var bindings = new MvTableBindings(host.ViewName, host.ViewVersion, _options);
        foreach (var entry in entries)
        {
            bindings.RegisterTable(entry.LogicalTable, entry.PhysicalTable);
        }

        return bindings;
    }

    protected static Dictionary<string, object?> ToParameterDictionary(IReadOnlyList<MvParam> parameters)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            values[parameter.Name] = MvParamConverter.ToClrValue(parameter);
        }

        return values;
    }

    private static SortableUniqueId CreateSafeThreshold(int safeWindowMs) =>
        new(SortableUniqueId.Generate(DateTime.UtcNow.AddMilliseconds(-safeWindowMs), Guid.Empty));

    private static async Task<MvCheckpointTruth> CaptureTargetCheckpointFromStoreAsync(IEventStore eventStore)
    {
        try
        {
            var result = await eventStore.GetLatestSortableUniqueIdAsync().ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable);
            }

            var latestSortableUniqueId = result.GetValue();
            if (string.IsNullOrWhiteSpace(latestSortableUniqueId))
            {
                return MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AuthoritativeTargetCapture());
            }

            return MvCheckpointTruth.Known(
                new SortableUniqueId(latestSortableUniqueId),
                MvCheckpointProvenance.AuthoritativeTargetCapture());
        }
        catch (MvCheckpointMalformedException)
        {
            return MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.Malformed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable);
        }
    }
}

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
public abstract class MvExecutorBase<TConnection> : IMvExecutor, IMvActivationExecutor, IMvInitializationVerifier
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

    /// <summary>Provider executors override this so policy decisions carry the concrete database type.</summary>
    protected virtual MvDbType DatabaseType => MvDbType.Postgres;

    private bool IsVerifyOnly => _options.InitializationMode == MvInitializationMode.VerifyOnly;

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

    public async Task<MvSchemaVerificationResult> VerifyInitializationAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ValidateServiceIdAtBoundary(serviceId);
        var bindings = new MvTableBindings(host.ViewName, host.ViewVersion, _options);
        return await VerifyOnlyCoreAsync(host, exactServiceId, bindings, cancellationToken).ConfigureAwait(false);
    }

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
        await ReadRegistryEntriesAtOperationBoundaryAsync(
                host,
                exactServiceId,
                cancellationToken)
            .ConfigureAwait(false);

        var target = await CaptureTargetCheckpointFromStoreAsync(SelectEventStoreForService(exactServiceId))
            .ConfigureAwait(false);
        if (!IsVerifyOnly)
        {
            await _registryStore.SetTargetCheckpointAsync(
                    exactServiceId,
                    host.ViewName,
                    host.ViewVersion,
                    target,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        return target;
    }

    public async Task<MvActivationResult> TryActivateAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ValidateServiceIdAtBoundary(serviceId);
        var entries = await ReadRegistryEntriesAtOperationBoundaryAsync(
                host,
                exactServiceId,
                cancellationToken,
                initializeWhenEmpty: false)
            .ConfigureAwait(false);
        var active = await ReadActiveAsync(exactServiceId, host.ViewName, cancellationToken).ConfigureAwait(false);
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

        if (IsVerifyOnly)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ProviderFailure,
                "Verify-only infrastructure mode never mutates materialized-view registry state.");
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
        var bindings = new MvTableBindings(host.ViewName, host.ViewVersion, _options);
        if (_options.InitializationMode == MvInitializationMode.VerifyOnly)
        {
            var verification = await VerifyOnlyCoreAsync(host, serviceId, bindings, cancellationToken).ConfigureAwait(false);
            ThrowIfInitializationVerificationFailed(verification);
            return;
        }

        var statements = await host.InitializeAsync(bindings, cancellationToken).ConfigureAwait(false);
        await AuthorizeStatementsAsync(
                serviceId,
                host,
                bindings,
                statements,
                MvSqlStatementPhase.Initialization,
                cancellationToken,
                MvSqlStatementOrigin.ProjectorInitialize)
            .ConfigureAwait(false);

        await _registryStore.EnsureInfrastructureAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<MvSchemaVerificationResult> VerifyOnlyCoreAsync(
        IMvApplyHost host,
        string serviceId,
        MvTableBindings bindings,
        CancellationToken cancellationToken)
    {
        if (_registryStore is not IMvReadOnlyMvInspector inspector)
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.UnsupportedProviderCapability,
                "The configured materialized-view provider does not expose a dedicated read-only inspector.");
        }

        // The declarative schema provider is the only binding source in VerifyOnly. Calling projector initialization
        // here would execute arbitrary projector code and would reintroduce the DDL/host side-effect boundary.
        var contract = host.GetSchemaContract(bindings);
        if (contract is not null && contract.FormatVersion != MvSchemaContract.CurrentFormatVersion)
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.SchemaContractUnavailable,
                $"Materialized-view schema contract format version '{contract.FormatVersion}' is not supported.");
        }

        var requirements = contract?.Tables ?? host.GetSchemaRequirements(bindings);
        if (contract is null && bindings.Tables.Count > 0 && requirements.Count == 0)
        {
            return MvSchemaVerificationResult.Failed(
                MvInitializationFailureReason.SchemaContractUnavailable,
                "Verify-only initialization requires a declarative materialized-view schema contract.");
        }

        var contractResult = MvSchemaRequirements.ValidateContract(bindings.Tables, requirements);
        if (!contractResult.IsCompatible)
        {
            return contractResult;
        }

        var verificationResult = await inspector.VerifySchemaAsync(
                [.. MvSchemaRequirements.RegistryTables(), .. requirements],
                cancellationToken)
            .ConfigureAwait(false);
        if (!verificationResult.IsCompatible)
        {
            return verificationResult;
        }

        var existingEntries = await inspector.ReadRegistryEntriesAsync(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var expectedTables = bindings.Tables.ToDictionary(table => table.LogicalName, StringComparer.Ordinal);
        var observedLogicalTables = new HashSet<string>(StringComparer.Ordinal);
        var registryMismatches = new List<MvSchemaMismatch>();
        foreach (var entry in existingEntries)
        {
            if (!expectedTables.TryGetValue(entry.LogicalTable, out var expectedTable) ||
                !string.Equals(entry.PhysicalTable, expectedTable.PhysicalName, StringComparison.Ordinal) ||
                !observedLogicalTables.Add(entry.LogicalTable))
            {
                registryMismatches.Add(
                    new MvSchemaMismatch(
                        MvSchemaMismatchCode.BindingMismatch,
                        $"The existing materialized-view registry binding for logical table '{entry.LogicalTable}' is incompatible with the projector contract.",
                        entry.LogicalTable,
                        entry.PhysicalTable));
            }
        }

        foreach (var table in bindings.Tables.Where(table => !observedLogicalTables.Contains(table.LogicalName)))
        {
            registryMismatches.Add(
                new MvSchemaMismatch(
                    MvSchemaMismatchCode.BindingMismatch,
                    $"The materialized-view registry has no binding for logical table '{table.LogicalName}'.",
                    table.LogicalName,
                    table.PhysicalName));
        }

        return registryMismatches.Count == 0
            ? MvSchemaVerificationResult.Compatible()
            : MvSchemaVerificationResult.FailedWithMismatches(
                MvInitializationFailureReason.MissingSchemaContract,
                registryMismatches);
    }

    private async Task AuthorizeStatementsAsync(
        string serviceId,
        IMvApplyHost host,
        MvTableBindings bindings,
        IReadOnlyList<MvSqlStatementDto> statements,
        MvSqlStatementPhase phase,
        CancellationToken cancellationToken,
        MvSqlStatementOrigin? origin = null)
    {
        var policy = _options.SqlStatementPolicyMode == MvSqlStatementPolicyMode.Enforced
            ? _options.SqlStatementPolicy
            : _options.SqlStatementPolicy ?? MvAllowAllSqlStatementPolicy.Instance;
        var tables = bindings.Tables.ToList();
        var batch = statements.ToList();
        for (var statementIndex = 0; statementIndex < batch.Count; statementIndex++)
        {
            var statement = batch[statementIndex];
            await MvSqlPolicyEvaluator.AuthorizeAsync(
                    policy,
                    new MvSqlStatementContext(
                        serviceId,
                        host.ViewName,
                        host.ViewVersion,
                        phase,
                        tables,
                        statement.Sql,
                        statement.Parameters.Select(parameter => parameter with { ValueJson = null }).ToList())
                    {
                        DatabaseType = DatabaseType,
                        Origin = origin ?? (phase == MvSqlStatementPhase.Initialization
                            ? MvSqlStatementOrigin.ProjectorInitialize
                            : MvSqlStatementOrigin.ProjectorApply),
                        StatementIndex = statementIndex,
                        BatchSize = batch.Count
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void ThrowIfInitializationVerificationFailed(MvSchemaVerificationResult result)
    {
        if (!result.IsCompatible)
        {
            var failure = result.Failure ?? new MvInitializationFailure(
                MvInitializationFailureReason.UnsupportedProviderCapability,
                "Materialized-view schema verification failed without a typed reason.");
            if (result.Mismatches.Count > 0)
            {
                failure = failure with { Mismatches = result.Mismatches };
            }
            throw new MvInitializationException(
                failure);
        }
    }

    private Task<IReadOnlyList<MvRegistryEntry>> ReadRegistryEntriesAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        CancellationToken cancellationToken)
    {
        if (_options.InitializationMode == MvInitializationMode.VerifyOnly)
        {
            if (_registryStore is not IMvReadOnlyMvInspector inspector)
            {
                throw new MvInitializationException(
                    new MvInitializationFailure(
                        MvInitializationFailureReason.UnsupportedProviderCapability,
                        "The configured materialized-view provider does not expose a dedicated read-only inspector."));
            }

            return inspector.ReadRegistryEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);
        }

        return _registryStore.GetEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);
    }

    private Task<MvActiveEntry?> ReadActiveAsync(
        string serviceId,
        string viewName,
        CancellationToken cancellationToken)
    {
        if (IsVerifyOnly)
        {
            if (_registryStore is not IMvReadOnlyMvInspector inspector)
            {
                throw new MvInitializationException(
                    new MvInitializationFailure(
                        MvInitializationFailureReason.UnsupportedProviderCapability,
                        "The configured materialized-view provider does not expose a dedicated read-only active-pointer inspector."));
            }

            return inspector.ReadActiveAsync(serviceId, viewName, cancellationToken);
        }

        return _registryStore.GetActiveAsync(serviceId, viewName, cancellationToken);
    }

    /// <summary>
    ///     All operation paths that may discover an empty registry converge here. VerifyOnly runs the dedicated
    ///     read-only schema/contract gate before the first registry-row query, so a missing framework table produces a
    ///     typed failure instead of a provider write/read fallback. Legacy mode retains its lazy initialize behavior.
    /// </summary>
    private async Task<IReadOnlyList<MvRegistryEntry>> ReadRegistryEntriesAtOperationBoundaryAsync(
        IMvApplyHost host,
        string serviceId,
        CancellationToken cancellationToken,
        bool initializeWhenEmpty = true)
    {
        if (_options.InitializationMode == MvInitializationMode.VerifyOnly)
        {
            await InitializeCoreAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
        }

        var entries = await ReadRegistryEntriesAsync(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (entries.Count == 0 && initializeWhenEmpty && _options.InitializationMode != MvInitializationMode.VerifyOnly)
        {
            await InitializeCoreAsync(host, serviceId, cancellationToken).ConfigureAwait(false);
            entries = await ReadRegistryEntriesAsync(
                    serviceId,
                    host.ViewName,
                    host.ViewVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return entries;
    }

    protected async Task<MvProjectionStatusSnapshot> GetCurrentStatusAsync(
        IMvApplyHost host,
        string serviceId,
        CancellationToken cancellationToken)
    {
        var entries = await ReadRegistryEntriesAtOperationBoundaryAsync(
                host,
                serviceId,
                cancellationToken)
            .ConfigureAwait(false);

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
        var entries = await ReadRegistryEntriesAtOperationBoundaryAsync(
                host,
                serviceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0 || entries.All(entry =>
                entry.TargetCheckpointTruth.IsKnown &&
                entry.TargetCheckpointTruth.Provenance?.Kind == MvCheckpointProvenanceKind.AuthoritativeTargetCapture))
        {
            return;
        }

        // Verify-only may inspect an existing target, but it is never allowed to create or refresh registry truth.
        if (IsVerifyOnly)
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
        var entries = await ReadRegistryEntriesAsync(
                serviceId,
                host.ViewName,
                host.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var active = await ReadActiveAsync(serviceId, host.ViewName, cancellationToken).ConfigureAwait(false);

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

        if (IsVerifyOnly)
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
            if (!IsVerifyOnly)
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
            }
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
        var entries = await ReadRegistryEntriesAtOperationBoundaryAsync(
                host,
                serviceId,
                cancellationToken)
            .ConfigureAwait(false);

        // Verify-only is an inspection lifecycle. Once the declarative gate has succeeded, an event-apply call is
        // deliberately a no-op so it cannot open the normal target connection, create a transaction, execute
        // projector SQL, or commit a view/registry mutation.
        if (IsVerifyOnly)
        {
            return 0;
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
        if (_options.SqlStatementPolicyMode == MvSqlStatementPolicyMode.Enforced)
        {
            queryPort = new MvPolicyEnforcingQueryPort(
                queryPort,
                _options.SqlStatementPolicy,
                serviceId,
                host.ViewName,
                host.ViewVersion,
                bindings.Tables,
                DatabaseType);
        }
        IReadOnlyList<MvSqlStatementDto> statements;
        try
        {
            statements = await host.ApplyEventAsync(
                    serializableEvent,
                    bindings,
                    queryPort,
                    serializableEvent.SortableUniqueIdValue,
                    cancellationToken)
                .ConfigureAwait(false);
            await AuthorizeStatementsAsync(
                    serviceId,
                    host,
                    bindings,
                    statements,
                    MvSqlStatementPhase.Apply,
                    cancellationToken,
                    MvSqlStatementOrigin.ProjectorApply)
                .ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve the policy/fault/cancellation outcome. Disposal below still closes the transaction.
            }
            throw;
        }

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

        if (!IsVerifyOnly)
        {
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
        }
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

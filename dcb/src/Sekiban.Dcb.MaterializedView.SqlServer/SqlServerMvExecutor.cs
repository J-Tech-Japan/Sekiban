using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.SqlServer;

public sealed class SqlServerMvExecutor : MvExecutorBase<SqlConnection>, IMvExecutor
{
    private readonly IEventStoreFactory? _eventStoreFactory;
    private readonly IEventStore? _legacyEventStore;
    private readonly IServiceIdProvider? _legacyServiceIdProvider;

    /// <summary>
    /// Creates the legacy single-service compatibility executor over an aggregate event store.
    /// Multi-service hosts should use the <see cref="IEventStoreFactory"/> constructor.
    /// </summary>
    public SqlServerMvExecutor(
        IEventStore eventStore,
        IServiceIdProvider serviceIdProvider,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<SqlServerMvExecutor> logger,
        string connectionString)
        : base(registryStore, options, logger, connectionString)
    {
        _legacyEventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _legacyServiceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
    }

    /// <summary>Creates an executor whose event reads use the standard service-scoped factory.</summary>
    public SqlServerMvExecutor(
        IEventStoreFactory eventStoreFactory,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<SqlServerMvExecutor> logger,
        string connectionString)
        : base(registryStore, options, logger, connectionString)
    {
        _eventStoreFactory = eventStoreFactory ?? throw new ArgumentNullException(nameof(eventStoreFactory));
    }

    public Task InitializeAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ResolveServiceId(serviceId);
        return InitializeCoreAsync(host, exactServiceId, cancellationToken);
    }

    public async Task<MvCatchUpResult> CatchUpOnceAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ResolveServiceId(serviceId);
        var currentPosition = await GetCurrentPositionAsync(host, exactServiceId, cancellationToken).ConfigureAwait(false);
        IEventStore eventStore;
        if (_eventStoreFactory is not null)
        {
            eventStore = _eventStoreFactory.CreateForService(exactServiceId) ??
                throw new InvalidOperationException($"The event-store factory returned null for ServiceId '{exactServiceId}'.");
        }
        else
        {
            eventStore = _legacyEventStore ??
                throw new InvalidOperationException("No legacy event store is registered for materialized views.");
        }

        var readResult = await eventStore.ReadAllSerializableEventsAsync(
                SortableUniqueId.NullableValue(currentPosition),
                Options.BatchSize)
            .ConfigureAwait(false);
        return await CompleteCatchUpAsync(host, exactServiceId, readResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ApplySerializableEventsAsync(
        IMvApplyHost host,
        IReadOnlyList<SerializableEvent> events,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ResolveServiceId(serviceId);
        if (events.Count == 0)
        {
            return 0;
        }

        return await ApplySerializableEventsCoreAsync(
                host,
                events,
                exactServiceId,
                MvApplySource.Stream,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string ResolveServiceId(string? requestedServiceId) =>
        MvServiceIdValidation.Validate(
            requestedServiceId,
            Options,
            _eventStoreFactory is null ? _legacyServiceIdProvider : null,
            nameof(SqlServerMvExecutor));

    protected override async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    protected override IMvApplyQueryPort CreateQueryPort(
        SqlConnection connection,
        IDbTransaction transaction) =>
        new SqlServerMvApplyQueryPort(connection, transaction);

    protected override Task<int> ExecuteSqlAsync(
        SqlConnection connection,
        string sql,
        IReadOnlyList<MvParam> parameters,
        IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                ToParameterDictionary(parameters),
                transaction,
                cancellationToken: cancellationToken));
}

using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.MySql;

public sealed class MySqlMvExecutor : MvExecutorBase<MySqlConnection>, IMvExecutor
{
    private readonly IEventStoreFactory? _eventStoreFactory;
    private readonly IEventStore? _legacyEventStore;
    private readonly IServiceIdProvider? _legacyServiceIdProvider;

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
        : base(registryStore, options, logger, connectionString)
    {
        (_legacyEventStore, _legacyServiceIdProvider) =
            RequireLegacyCompatibilityDependencies(eventStore, serviceIdProvider);
    }

    /// <summary>Creates an executor whose event reads use the standard service-scoped factory.</summary>
    public MySqlMvExecutor(
        IEventStoreFactory eventStoreFactory,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<MySqlMvExecutor> logger,
        string connectionString)
        : base(registryStore, options, logger, connectionString)
    {
        _eventStoreFactory = RequireEventStoreFactory(eventStoreFactory);
    }

    public Task InitializeAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
        => InitializeCoreAsync(host, ResolveServiceId(serviceId), cancellationToken);

    public async Task<MvCatchUpResult> CatchUpOnceAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ResolveServiceId(serviceId);
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

        return await CatchUpFromStoreAsync(host, exactServiceId, eventStore, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> ApplySerializableEventsAsync(
        IMvApplyHost host,
        IReadOnlyList<SerializableEvent> events,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
        => ApplyStreamEventsAtBoundaryAsync(host, events, ResolveServiceId(serviceId), cancellationToken);

    private string ResolveServiceId(string? requestedServiceId) =>
        ValidateServiceId(
            requestedServiceId,
            _eventStoreFactory is null ? _legacyServiceIdProvider : null,
            nameof(MySqlMvExecutor));

    protected override async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    protected override IMvApplyQueryPort CreateQueryPort(
        MySqlConnection connection,
        IDbTransaction transaction) =>
        new MySqlMvApplyQueryPort(connection, transaction);

    protected override Task<int> ExecuteSqlAsync(
        MySqlConnection connection,
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

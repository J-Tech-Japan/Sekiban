using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.Sqlite;

public sealed class SqliteMvExecutor : MvExecutorBase<SqliteConnection>, IMvExecutor
{
    private readonly IEventStoreFactory? _eventStoreFactory;
    private readonly IEventStore? _legacyEventStore;
    private readonly IServiceIdProvider? _legacyServiceIdProvider;

    /// <summary>
    /// Creates the legacy single-service compatibility executor over an aggregate event store.
    /// Multi-service hosts should use the <see cref="IEventStoreFactory"/> constructor.
    /// </summary>
    public SqliteMvExecutor(
        IEventStore eventStore,
        IServiceIdProvider serviceIdProvider,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<SqliteMvExecutor> logger,
        string connectionString)
        : base(registryStore, options, logger, connectionString, serviceIdProvider)
    {
        (_legacyEventStore, _legacyServiceIdProvider) =
            RequireLegacyCompatibilityDependencies(eventStore, serviceIdProvider);
    }

    /// <summary>Creates an executor whose event reads use the standard service-scoped factory.</summary>
    public SqliteMvExecutor(
        IEventStoreFactory eventStoreFactory,
        IMvRegistryStore registryStore,
        IOptions<MvOptions> options,
        ILogger<SqliteMvExecutor> logger,
        string connectionString)
        : base(registryStore, options, logger, connectionString) =>
        _eventStoreFactory = RequireEventStoreFactory(eventStoreFactory);

    public override async Task<MvCatchUpResult> CatchUpOnceAsync(
        IMvApplyHost host,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        var exactServiceId = ValidateServiceIdAtBoundary(serviceId);
        return await CatchUpFromStoreAsync(
                host,
                exactServiceId,
                _eventStoreFactory is null ? _legacyEventStore : _eventStoreFactory!.CreateForService(exactServiceId),
                _eventStoreFactory is not null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    protected override async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
                new CommandDefinition("PRAGMA synchronous=NORMAL;", cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return connection;
    }

    protected override IMvApplyQueryPort CreateQueryPort(
        SqliteConnection connection,
        IDbTransaction transaction) =>
        new SqliteMvApplyQueryPort(connection, transaction);

    protected override Task<int> ExecuteSqlAsync(
        SqliteConnection connection,
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

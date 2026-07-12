using Microsoft.Azure.Cosmos;
using System.Net;

namespace Sekiban.Dcb.Tests.Cosmos;

/// <summary>
///     A CosmosClient whose database hands out <see cref="InMemoryCosmosContainer" />s, so the real
///     <c>CosmosDbContext</c> — and therefore the real event store, repair service, and sweep — can be
///     constructed and driven without an emulator.
/// </summary>
public sealed class InMemoryCosmosClient : CosmosClient
{
    private readonly InMemoryCosmosDatabase _database;

    public InMemoryCosmosClient() => _database = new InMemoryCosmosDatabase();

    /// <summary>The container by name, creating it on first ask, exactly as the context expects.</summary>
    public InMemoryCosmosContainer Container(string name) => _database.Container(name);

    public override Task<DatabaseResponse> CreateDatabaseIfNotExistsAsync(
        string id,
        int? throughput = null,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DatabaseResponse>(new InMemoryDatabaseResponse(_database));

    public override Task<DatabaseResponse> CreateDatabaseIfNotExistsAsync(
        string id,
        ThroughputProperties? throughputProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        CreateDatabaseIfNotExistsAsync(id, (int?)null, requestOptions, cancellationToken);

    public override Database GetDatabase(string id) => _database;

    public override Container GetContainer(string databaseId, string containerId) => _database.Container(containerId);
}

/// <summary>
///     The in-memory database: a name-to-container map, and nothing else the code under test needs.
/// </summary>
public sealed class InMemoryCosmosDatabase : NotSupportedCosmosDatabase
{
    private readonly Dictionary<string, InMemoryCosmosContainer> _containers = new(StringComparer.Ordinal);

    public override string Id => "test-db";

    public InMemoryCosmosContainer Container(string name)
    {
        if (!_containers.TryGetValue(name, out var container))
        {
            container = new InMemoryCosmosContainer(name);
            _containers[name] = container;
        }

        return container;
    }

    public override Task<ContainerResponse> CreateContainerIfNotExistsAsync(
        ContainerProperties containerProperties,
        int? throughput = null,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ContainerResponse>(new InMemoryContainerResponse(Container(containerProperties.Id)));

    public override Task<ContainerResponse> CreateContainerIfNotExistsAsync(
        ContainerProperties containerProperties,
        ThroughputProperties throughputProperties,
        RequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default) =>
        CreateContainerIfNotExistsAsync(containerProperties, (int?)null, requestOptions, cancellationToken);

    public override Container GetContainer(string id) => Container(id);
}

internal sealed class InMemoryDatabaseResponse : DatabaseResponse
{
    private readonly Headers _headers = new();

    public InMemoryDatabaseResponse(Database database) => Database = database;

    public override Database Database { get; }
    public override DatabaseProperties Resource => new();
    public override HttpStatusCode StatusCode => HttpStatusCode.OK;
    public override Headers Headers => _headers;
    public override CosmosDiagnostics Diagnostics => null!;
    public override double RequestCharge => 1.0;
    public override string ActivityId => "activity";
    public override string? ETag => null;
}

internal sealed class InMemoryContainerResponse : ContainerResponse
{
    private readonly Headers _headers = new();

    public InMemoryContainerResponse(Container container) => Container = container;

    public override Container Container { get; }
    public override ContainerProperties Resource => new();
    public override HttpStatusCode StatusCode => HttpStatusCode.OK;
    public override Headers Headers => _headers;
    public override CosmosDiagnostics Diagnostics => null!;
    public override double RequestCharge => 1.0;
    public override string ActivityId => "activity";
    public override string? ETag => null;
}

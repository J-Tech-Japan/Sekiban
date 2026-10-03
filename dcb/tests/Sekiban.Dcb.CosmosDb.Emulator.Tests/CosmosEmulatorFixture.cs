using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Azure.Cosmos;

namespace Sekiban.Dcb.CosmosDb.Emulator.Tests;

[CollectionDefinition(Name)]
public sealed class CosmosEmulatorCollection : ICollectionFixture<CosmosEmulatorFixture>
{
    public const string Name = "Cosmos emulator";
}

public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    public const string Image = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-EN20260907@sha256:2db1f9e74c506bcf6fc347aa937aea1c00fa756061296a5a9efba530ce86ec02";
    public const string Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    private IContainer? _container;
    public bool IsAvailable { get; private set; }
    public string? AvailabilityMessage { get; private set; }
    public string Endpoint { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("SEKIBAN_COSMOS_EMULATOR_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(external))
        {
            if (!Uri.TryCreate(external, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new ArgumentException("SEKIBAN_COSMOS_EMULATOR_ENDPOINT must be an absolute HTTPS endpoint.");
            Endpoint = external;
            IsAvailable = true;
            return;
        }

        try
        {
            _container = new ContainerBuilder(Image)
                .WithPortBinding(8081, true).WithPortBinding(8080, true)
                .WithEnvironment("PROTOCOL", "https")
                .WithEnvironment("ENABLE_EXPLORER", "false")
                .WithEnvironment("ENABLE_TELEMETRY", "false")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                    request => request.ForPort(8080).ForPath("/ready")))
                .Build();
            await _container.StartAsync();
            Endpoint = $"https://{_container.Hostname}:{_container.GetMappedPublicPort(8081)}/";
            IsAvailable = true;
        }
        catch (DockerUnavailableException ex) when (
            Environment.GetEnvironmentVariable("SEKIBAN_COSMOS_EMULATOR_REQUIRED") != "1")
        {
            AvailabilityMessage = $"Cosmos emulator tests require Docker: {ex.Message}";
        }
    }

    public CosmosClient CreateClient() => new(Endpoint, Key, new CosmosClientOptions
    {
        ConnectionMode = ConnectionMode.Gateway,
        LimitToEndpoint = true,
        SerializerOptions = new CosmosSerializationOptions { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase },
        HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
    });

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }
}

// Each test owns a database, including cleanup after assertion failures.
internal sealed class TestDatabase : IAsyncDisposable
{
    public CosmosClient Client { get; }
    public Database Database { get; }
    public string Name => Database.Id;
    private TestDatabase(CosmosClient client, Database database) { Client = client; Database = database; }

    public static async Task<TestDatabase> CreateAsync(CosmosEmulatorFixture fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage);
        var client = fixture.CreateClient();
        var transferred = false;
        try
        {
            var response = await client.CreateDatabaseAsync("g107-" + Guid.NewGuid().ToString("N"));
            transferred = true;
            return new TestDatabase(client, response.Database);
        }
        finally
        {
            if (!transferred) client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await Database.DeleteAsync(); }
        finally { Client.Dispose(); }
    }
}

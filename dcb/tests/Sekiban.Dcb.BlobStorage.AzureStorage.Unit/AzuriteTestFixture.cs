using Azure.Storage.Blobs;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Sekiban.Dcb.BlobStorage.AzureStorage.Unit;

/// <summary>
/// Test fixture that manages Azurite container lifecycle for tests
/// </summary>
public class AzuriteTestFixture : IAsyncLifetime
{
    private IContainer? _container;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // Prefer externally supplied connection string when available (CI can provide Azurite service)
        var externalConnection = Environment.GetEnvironmentVariable("AZURITE_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(externalConnection))
        {
            ConnectionString = externalConnection;
            await WaitForAzuriteAsync();
            return;
        }

        Console.WriteLine("Starting Azurite container using Testcontainers...");

        _container = new ContainerBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithName($"azurite-test-{Guid.NewGuid():N}")
            .WithPortBinding(10000, true)
            .WithPortBinding(10001, true)
            .WithPortBinding(10002, true)
            .WithCommand("azurite", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0", "--skipApiVersionCheck", "--loose")
            .WithEnvironment("AZURITE_ACCOUNTS", "devstoreaccount1:Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Azurite Blob service is successfully listening"))
            .Build();

        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(10000);
        Console.WriteLine($"Azurite Blob endpoint mapped to host port: {hostPort}");

        ConnectionString =
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:{hostPort}/devstoreaccount1;";

        await WaitForAzuriteAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            Console.WriteLine($"Stopping Azurite container: {_container.Name}");
            await _container.StopAsync();
            await _container.DisposeAsync();
            _container = null;
        }
    }

    private async Task WaitForAzuriteAsync()
    {
        var maxRetries = 120;
        var delay = TimeSpan.FromMilliseconds(500);

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var options = new BlobClientOptions
                {
                    Retry =
                    {
                        MaxRetries = 1,
                        Delay = TimeSpan.FromMilliseconds(100),
                        Mode = Azure.Core.RetryMode.Fixed
                    }
                };
                var client = new BlobServiceClient(ConnectionString, options);
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.GetPropertiesAsync(cancellationToken: cts.Token);
                Console.WriteLine("Azurite is ready!");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Waiting for Azurite... Attempt {i + 1}/{maxRetries}");
                if (i == maxRetries - 1)
                {
                    Console.WriteLine($"Failed to connect to Azurite: {ex.Message}");
                    throw;
                }
                await Task.Delay(delay);
            }
        }
    }
}

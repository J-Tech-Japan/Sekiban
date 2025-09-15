using Azure.Storage.Blobs;
using System.Diagnostics;
using Xunit;

namespace Sekiban.Dcb.BlobStorage.AzureStorage.Unit;

/// <summary>
/// Test fixture that manages Azurite container lifecycle for tests
/// </summary>
public class AzuriteTestFixture : IAsyncLifetime
{
    private string _containerId = string.Empty;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        // Start Azurite container
        Console.WriteLine("Starting Azurite container...");
        
        // Generate unique container name to avoid conflicts
        var containerName = $"azurite-test-{Guid.NewGuid():N}";
        
        // Start new Azurite container with random ports to avoid conflicts and skip API version check
        var result = await RunCommandAsync("docker", 
            $"run -d --name {containerName} -P mcr.microsoft.com/azure-storage/azurite azurite --skipApiVersionCheck");
        
        _containerId = result.Trim();
        Console.WriteLine($"Started Azurite container: {_containerId}");
        
        // Get the mapped port for blob service
        var portInfo = await RunCommandAsync("docker", $"port {_containerId} 10000");
        var blobPort = ExtractPort(portInfo);
        Console.WriteLine($"Blob service port: {blobPort}");
        
        // Build connection string with dynamic port
        ConnectionString = $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:{blobPort}/devstoreaccount1;";
        
        // Wait for Azurite to be ready
        await WaitForAzuriteAsync();
    }

    public async Task DisposeAsync()
    {
        if (!string.IsNullOrEmpty(_containerId))
        {
            Console.WriteLine($"Stopping Azurite container: {_containerId}");
            await RunCommandAsync("docker", $"stop {_containerId}");
            await RunCommandAsync("docker", $"rm {_containerId}");
        }
    }

    private async Task WaitForAzuriteAsync()
    {
        var maxRetries = 30;
        var delay = TimeSpan.FromSeconds(1);
        
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var client = new BlobServiceClient(ConnectionString);
                await client.GetPropertiesAsync();
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

    private string ExtractPort(string portInfo)
    {
        // Docker port command returns format like "0.0.0.0:32768" or "[::]:32768"
        var parts = portInfo.Trim().Split(':');
        if (parts.Length >= 2)
        {
            return parts[parts.Length - 1];
        }
        throw new Exception($"Unable to extract port from: {portInfo}");
    }

    private async Task<string> RunCommandAsync(string command, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        if (process.ExitCode != 0 && !arguments.Contains("2>/dev/null"))
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new Exception($"Command failed: {command} {arguments}\nError: {error}");
        }
        
        return output;
    }
}

[CollectionDefinition("AzuriteCollection")]
public class AzuriteCollection : ICollectionFixture<AzuriteTestFixture>
{
}
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     Minimal test to verify Orleans silo can start
/// </summary>
public class SimpleSiloStartupTest : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TestCluster _cluster = null!;

    public SimpleSiloStartupTest(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        try
        {
            var builder = new TestClusterBuilder();
            builder.Options.InitialSilosCount = 1;
            var uniqueId = Guid.NewGuid().ToString("N")[..8];
            builder.Options.ClusterId = $"TestCluster-{uniqueId}";
            builder.Options.ServiceId = $"TestService-{uniqueId}";
            builder.AddSiloBuilderConfigurator<MinimalSiloConfigurator>();

            _cluster = builder.Build();
            await _cluster.DeployAsync();
            _output.WriteLine("Cluster deployed successfully");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to deploy cluster: {ex}");
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    [Fact]
    public void Orleans_Silo_Should_Start()
    {
        Assert.NotNull(_cluster);
        Assert.NotNull(_cluster.Client);
    }

    private class MinimalSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .ConfigureLogging(logging => logging.AddConsole())
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("EventStreamProvider")
                .AddMemoryGrainStorage("EventStreamProvider");
        }
    }
}

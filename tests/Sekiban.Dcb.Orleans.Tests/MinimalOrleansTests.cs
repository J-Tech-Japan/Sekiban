using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.TestingHost;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
/// Minimal Orleans tests to verify Orleans integration works
/// </summary>
public class MinimalOrleansTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;
    private IClusterClient _client => _cluster.Client;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<TestClientConfigurator>();
        
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        await _cluster.StopAllSilosAsync();
        _cluster.Dispose();
    }

    [Fact]
    public async Task Orleans_TestCluster_Should_Start_Successfully()
    {
        // Assert
        _cluster.Should().NotBeNull();
        _client.Should().NotBeNull();
        _cluster.ServiceProvider.Should().NotBeNull();
    }

    [Fact]
    public async Task Orleans_Should_Activate_MultiProjectionGrain()
    {
        // Arrange & Act
        var grain = _client.GetGrain<IMultiProjectionGrain>("test-projector");
        var status = await grain.GetStatusAsync();

        // Assert
        status.Should().NotBeNull();
        status.ProjectorName.Should().Be("test-projector");
        status.EventsProcessed.Should().Be(0);
        status.IsSubscriptionActive.Should().BeFalse();
    }

    [Fact]
    public async Task Orleans_Should_Support_Multiple_Grain_Instances()
    {
        // Arrange & Act
        var grain1 = _client.GetGrain<IMultiProjectionGrain>("projector-1");
        var grain2 = _client.GetGrain<IMultiProjectionGrain>("projector-2");
        
        var status1 = await grain1.GetStatusAsync();
        var status2 = await grain2.GetStatusAsync();

        // Assert
        status1.ProjectorName.Should().Be("projector-1");
        status2.ProjectorName.Should().Be("projector-2");
    }

    [Fact]
    public async Task Orleans_Grain_Should_Manage_Subscription_State()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("subscription-test");

        // Act - Get initial status
        var initialStatus = await grain.GetStatusAsync();
        
        // Start subscription
        await grain.StartSubscriptionAsync();
        var activeStatus = await grain.GetStatusAsync();
        
        // Stop subscription
        await grain.StopSubscriptionAsync();
        var stoppedStatus = await grain.GetStatusAsync();

        // Assert
        initialStatus.IsSubscriptionActive.Should().BeFalse();
        activeStatus.IsSubscriptionActive.Should().BeTrue();
        stoppedStatus.IsSubscriptionActive.Should().BeFalse();
    }

    [Fact]
    public async Task Orleans_Grain_Should_Return_Serializable_State()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("serialization-test");

        // Act
        var stateResult = await grain.GetSerializableStateAsync(true);

        // Assert
        stateResult.Should().NotBeNull();
        stateResult.IsSuccess.Should().BeTrue();
        var state = stateResult.GetValue();
        state.ProjectorName.Should().Be("serialization-test");
    }

    [Fact]
    public async Task Orleans_Grain_Should_Handle_Persistence()
    {
        // Arrange
        var grain = _client.GetGrain<IMultiProjectionGrain>("persistence-test");

        // Act
        var persistResult = await grain.PersistStateAsync();

        // Assert
        persistResult.Should().NotBeNull();
        persistResult.IsSuccess.Should().BeTrue();
        
        // Get status to verify persistence details
        var status = await grain.GetStatusAsync();
        status.LastPersistTime.Should().NotBeNull();
    }

    private class TestSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .UseLocalhostClustering()
                .ConfigureServices(services =>
                {
                    // Minimal required services for Orleans grains
                    services.AddTransient<IMultiProjectionGrain, MultiProjectionGrain>();
                })
                .AddMemoryGrainStorageAsDefault()
                .AddMemoryGrainStorage("OrleansStorage");
        }
    }

    private class TestClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.UseLocalhostClustering();
        }
    }
}
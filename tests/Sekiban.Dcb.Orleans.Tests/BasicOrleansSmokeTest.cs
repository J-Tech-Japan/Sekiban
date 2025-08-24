using Orleans.TestingHost;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     最小構成でOrleansクラスタ起動のみを検証するスモークテスト
/// </summary>
public class BasicOrleansSmokeTest : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        builder.Options.ClusterId = $"SmokeCluster-{uniqueId}";
        builder.Options.ServiceId = $"SmokeService-{uniqueId}";
        builder.AddSiloBuilderConfigurator<BasicSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster != null)
        {
            await _cluster.StopAllSilosAsync();
            _cluster.Dispose();
        }
    }

    /// <summary>
    ///     クラスタとクライアントが取得できることを確認
    /// </summary>
    [Fact]
    public void Should_Start_Cluster()
    {
        Assert.NotNull(_cluster);
        Assert.NotNull(_cluster.Client);
    }

    private class BasicSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddMemoryGrainStorageAsDefault();
        }
    }
}

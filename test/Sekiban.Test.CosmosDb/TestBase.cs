using FeatureCheck.Domain.Shared;
using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Snapshot.BackgroundServices;
using Sekiban.Testing.Story;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb;

[Collection("Sequential")]
public class TestBase : IClassFixture<TestBase.SekibanTestFixture>, IDisposable
{
    protected readonly SekibanTestFixture _sekibanTestFixture;
    protected readonly IServiceProvider _serviceProvider;

    public TestBase(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper output,
        ISekibanServiceProviderGenerator providerGenerator,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType =
            ServiceCollectionExtensions.MultiProjectionType.MemoryCache)
    {
        sekibanTestFixture.TestOutputHelper = output;
        _sekibanTestFixture = sekibanTestFixture;
        _serviceProvider = providerGenerator.Generate(sekibanTestFixture, new FeatureCheckDependency(), null, null);
        var backgroundService = _serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
        backgroundService.ServiceProvider = _serviceProvider;
        Task.Run(() => backgroundService.StartAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
        }
    }

    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn is null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }

    public class SekibanTestFixture : ISekibanTestFixture
    {
        public SekibanTestFixture()
        {
            var builder = new ConfigurationBuilder().SetBasePath(ApplicationEnvironment.ApplicationBasePath)
                .AddJsonFile("appsettings.json", false, false)
                .AddEnvironmentVariables()
                .AddUserSecrets(Assembly.GetExecutingAssembly());
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }
        public ITestOutputHelper? TestOutputHelper { get; set; }
    }
}

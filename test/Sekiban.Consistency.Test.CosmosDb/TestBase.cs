using System;
using System.Reflection;
using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Testing.Story;
using Xunit;
using Xunit.Abstractions;

namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class TestBase : IClassFixture<TestBase.SekibanTestFixture>, IDisposable
{
    protected readonly SekibanTestFixture _sekibanTestFixture;
    protected readonly ServiceProvider _serviceProvider;

    public TestBase(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper output,
        bool inMemory = false,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType =
            ServiceCollectionExtensions.MultiProjectionType.MemoryCache)
    {
        sekibanTestFixture.TestOutputHelper = output;
        _sekibanTestFixture = sekibanTestFixture;
        _serviceProvider =
            DependencyHelper.CreateDefaultProvider(sekibanTestFixture, inMemory, null, multiProjectionType);
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
        if (toreturn is null) throw new Exception("オブジェクトが登録されていません。" + typeof(T));
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
        public ITestOutputHelper TestOutputHelper { get; set; }
    }
}

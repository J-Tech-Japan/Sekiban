using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.PlatformAbstractions;
using Sekiban.Core.Dependency;
using Sekiban.Testing.Story;
using System;
using System.Reflection;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class TestBase : IClassFixture<TestBase.SekibanTestFixture>, IDisposable
{
    protected readonly ServiceProvider _serviceProvider;
    protected readonly SekibanTestFixture _sekibanTestFixture;

    public class SekibanTestFixture : ISekibanTestFixture
    {
        public SekibanTestFixture()
        {
            var builder = new ConfigurationBuilder().SetBasePath(PlatformServices.Default.Application.ApplicationBasePath)
                .AddJsonFile("appsettings.json", false, false)
                .AddEnvironmentVariables()
                .AddUserSecrets(Assembly.GetExecutingAssembly());
            Configuration = builder.Build();
        }
        public IConfigurationRoot Configuration { get; set; }
    }

    public TestBase(
        SekibanTestFixture sekibanTestFixture,
        bool inMemory = false,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        _sekibanTestFixture = sekibanTestFixture;
        _serviceProvider = DependencyHelper.CreateDefaultProvider(sekibanTestFixture, inMemory, null, multipleProjectionType);
    }
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing) { }
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
}

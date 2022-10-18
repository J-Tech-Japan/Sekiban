using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Testing.Story;
using System;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class TestBase : IClassFixture<SekibanTestFixture>, IDisposable
{
    protected readonly ServiceProvider _serviceProvider;
    protected readonly SekibanTestFixture SekibanTestFixture;

    public TestBase(
        SekibanTestFixture sekibanTestFixture,
        bool inMemory = false,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        SekibanTestFixture = sekibanTestFixture;
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

using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing;
using Sekiban.EventSourcing.TestHelpers;
using System;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class TestBase : IClassFixture<TestFixture>, IDisposable
{
    protected readonly ServiceProvider _serviceProvider;
    protected readonly TestFixture _testFixture;

    public TestBase(
        TestFixture testFixture,
        bool inMemory = false,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        _testFixture = testFixture;
        _serviceProvider = DependencyHelper.CreateDefaultProvider(testFixture, inMemory, null, multipleProjectionType);
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

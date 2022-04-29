using Microsoft.Extensions.DependencyInjection;
using System;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class TestBase : IClassFixture<TestFixture>, IDisposable
{
    protected readonly ServiceProvider _serviceProvider;
    protected readonly TestFixture _testFixture;
    public TestBase(TestFixture testFixture)
    {
        _testFixture = testFixture;
        _serviceProvider = DependencyHelper.CreateDefaultProvider(testFixture);
    }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing) { }
    }
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn == null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }
}

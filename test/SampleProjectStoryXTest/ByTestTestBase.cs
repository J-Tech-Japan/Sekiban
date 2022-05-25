using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.TestHelpers;
using System;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class ByTestTestBase : IDisposable
{
    protected readonly ServiceProvider _serviceProvider;
    public ByTestTestBase()
    {
        var testFixture = new TestFixture();
        _serviceProvider = DependencyHelper.CreateDefaultProvider(testFixture, true);
    }
    public void Dispose() { }
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

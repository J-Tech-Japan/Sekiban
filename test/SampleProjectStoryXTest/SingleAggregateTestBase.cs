using ESSampleProjectDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class SingleAggregateTestBase : IDisposable
{
    protected readonly ServiceProvider _serviceProvider;
    public SingleAggregateTestBase()
    {
        var testFixture = new TestFixture();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(testFixture.Configuration);

        Dependency.RegisterForAggregateTest(services);

        _serviceProvider = services.BuildServiceProvider();
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

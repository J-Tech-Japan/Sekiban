using ESSampleProjectDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers;
using System;
using Xunit;
namespace SampleProjectStoryXTest;

[Collection("Sequential")]
public class SingleAggregateTestBase<TAggregate, TDto> : IDisposable where TAggregate : TransferableAggregateBase<TDto> where TDto : AggregateDtoBase
{
    protected readonly AggregateTestHelper<TAggregate, TDto> _helper;
    protected readonly ServiceProvider _serviceProvider;
    public SingleAggregateTestBase()
    {
        var testFixture = new TestFixture();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(testFixture.Configuration);

        Dependency.RegisterForAggregateTest(services);

        _serviceProvider = services.BuildServiceProvider();

        _helper = new AggregateTestHelper<TAggregate, TDto>(_serviceProvider);
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

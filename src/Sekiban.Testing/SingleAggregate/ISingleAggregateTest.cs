using System;
namespace Sekiban.Testing.SingleAggregate;

public abstract class SingleAggregateTestBase
{
    protected readonly IServiceProvider _serviceProvider;
    public Guid AggregateId { get; set; } = Guid.Empty;
    public SingleAggregateTestBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    public void SetAggregateId(Guid id)
    {
        AggregateId = id;
    }
}
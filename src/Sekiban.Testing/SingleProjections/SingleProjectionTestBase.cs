namespace Sekiban.Testing.SingleProjections;

public abstract class SingleProjectionTestBase
{
    protected readonly IServiceProvider _serviceProvider;
    public SingleProjectionTestBase(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;
    public Guid AggregateId { get; set; } = Guid.Empty;
    public void SetAggregateId(Guid id)
    {
        AggregateId = id;
    }
}

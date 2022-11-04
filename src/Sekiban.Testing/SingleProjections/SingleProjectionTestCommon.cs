namespace Sekiban.Testing.SingleProjections;

public abstract class SingleProjectionTestCommon
{
    protected readonly IServiceProvider _serviceProvider;
    public SingleProjectionTestCommon(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;
    public Guid AggregateId { get; set; } = Guid.Empty;
    public void SetAggregateId(Guid id)
    {
        AggregateId = id;
    }
}

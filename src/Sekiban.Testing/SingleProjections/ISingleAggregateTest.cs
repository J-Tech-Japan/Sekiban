namespace Sekiban.Testing.SingleProjections;

public abstract class AggregateTestBase
{
    protected readonly IServiceProvider _serviceProvider;
    public AggregateTestBase(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;
    public Guid AggregateId { get; set; } = Guid.Empty;
    public void SetAggregateId(Guid id)
    {
        AggregateId = id;
    }
}

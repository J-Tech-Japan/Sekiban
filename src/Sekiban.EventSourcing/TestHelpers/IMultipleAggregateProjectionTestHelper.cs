using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public interface IMultipleAggregateProjectionTestHelper<TProjection>
    where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
{
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(IEnumerable<IAggregateEvent> events);
    public IMultipleAggregateProjector<TProjection> GivenEvents(params IAggregateEvent[] definitions);
    public IMultipleAggregateProjectionTestHelper<TProjection> WhenProjection();
    public IMultipleAggregateProjectionTestHelper<TProjection> ThenNotThrowsAnException();
    public IMultipleAggregateProjectionTestHelper<TProjection> ThenDto(TProjection dto);
}

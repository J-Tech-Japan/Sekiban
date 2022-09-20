using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public interface IMultipleAggregateProjectionTestHelper<TProjection>
    where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
{
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(IEnumerable<IAggregateEvent> events);
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(params IAggregateEvent[] definitions);
    public IMultipleAggregateProjectionTestHelper<TProjection> GivenEvents(string jsonEvents);
    public Task<IMultipleAggregateProjectionTestHelper<TProjection>> GivenEventsFromFileAsync(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection> WhenProjection();
    public IMultipleAggregateProjectionTestHelper<TProjection> ThenNotThrowsAnException();
    public IMultipleAggregateProjectionTestHelper<TProjection> ThenDto(TProjection dto);
    public Task<IMultipleAggregateProjectionTestHelper<TProjection>> ThenDtoFileAsync(string filename);
    public Task<IMultipleAggregateProjectionTestHelper<TProjection>> WriteProjectionToFileAsync(string filename);
}

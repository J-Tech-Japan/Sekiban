using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public interface IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>
    where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
{
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(IEnumerable<IAggregateEvent> events);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(params IAggregateEvent[] definitions);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(string jsonEvents);
    public Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> GivenEventsFromFileAsync(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WhenProjection();
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenNotThrowsAnException();
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenDto(
        MultipleAggregateProjectionContentsDto<TProjectionContents> dto);
    public Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> ThenDtoFileAsync(string filename);
    public Task<IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>> WriteProjectionToFileAsync(string filename);
}

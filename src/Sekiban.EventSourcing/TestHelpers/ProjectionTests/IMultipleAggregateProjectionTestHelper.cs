using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.TestHelpers.Helpers;
using Sekiban.EventSourcing.TestHelpers.QueryFilters;
namespace Sekiban.EventSourcing.TestHelpers.ProjectionTests;

public interface IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>
    where TProjection : IMultipleAggregateProjector<TProjectionContents>, new()
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
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenDtoFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WriteProjectionToFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEventsFromFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenQueryFilterChecker(
        IQueryFilterChecker<MultipleAggregateProjectionContentsDto<TProjectionContents>> checker);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenScenario(Action initialAction);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenContents(TProjectionContents contents);
    public Guid RunCreateCommand<TAggregate>(ICreateAggregateCommand<TAggregate> command, Guid? injectingAggregateId = null)
        where TAggregate : AggregateBase, new();
    public void RunChangeCommand<TAggregate>(ChangeAggregateCommandBase<TAggregate> command) where TAggregate : AggregateBase, new();
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenCommandExecutorAction(
        Action<AggregateTestCommandExecutor> action);
    public AggregateDto<TEnvironmentAggregateContents> GetAggregateDto<TEnvironmentAggregate, TEnvironmentAggregateContents>(Guid aggregateId)
        where TEnvironmentAggregate : TransferableAggregateBase<TEnvironmentAggregateContents>, new()
        where TEnvironmentAggregateContents : IAggregateContents, new();
    public IReadOnlyCollection<IAggregateEvent> GetLatestEvents();
}

using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Testing.Command;
using Sekiban.Testing.QueryFilter;
namespace Sekiban.Testing.Projection;

public interface IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents>
    where TProjection : IMultipleAggregateProjector<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
{


    #region When
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WhenProjection();
    #endregion
    #region Given
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(IEnumerable<IAggregateEvent> events);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(params IAggregateEvent[] definitions);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEventsFromJson(string jsonEvents);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEventsFromFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenEvents(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenQueryFilterChecker(
        IQueryFilterChecker<MultipleAggregateProjectionContentsDto<TProjectionContents>> checker);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenScenario(Action initialAction);
    public Guid RunCreateCommand<TAggregate>(ICreateAggregateCommand<TAggregate> command, Guid? injectingAggregateId = null)
        where TAggregate : AggregateCommonBase, new();
    public void RunChangeCommand<TAggregate>(ChangeAggregateCommandBase<TAggregate> command) where TAggregate : AggregateCommonBase, new();
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> GivenCommandExecutorAction(
        Action<AggregateTestCommandExecutor> action);
    #endregion

    #region Then
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenNotThrowsAnException();
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenDtoIs(
        MultipleAggregateProjectionContentsDto<TProjectionContents> dto);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenGetDto(
        Action<MultipleAggregateProjectionContentsDto<TProjectionContents>> dtoAction);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenDtoIsFromFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WriteProjectionToFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenContentsIs(TProjectionContents contents);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenContentsIsFromFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> ThenGetContents(Action<TProjectionContents> contentsAction);
    #endregion
    #region Get
    public AggregateState<TEnvironmentAggregateContents> GetAggregateDto<TEnvironmentAggregate, TEnvironmentAggregateContents>(Guid aggregateId)
        where TEnvironmentAggregate : Aggregate<TEnvironmentAggregateContents>, new()
        where TEnvironmentAggregateContents : IAggregatePayload, new();
    public IReadOnlyCollection<IAggregateEvent> GetLatestEvents();
    #endregion
}

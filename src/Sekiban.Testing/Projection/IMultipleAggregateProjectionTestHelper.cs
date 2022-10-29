using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Testing.Command;
using Sekiban.Testing.QueryFilter;
namespace Sekiban.Testing.Projection;

public interface IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload>
    where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
    where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
{


    #region When
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> WhenProjection();
    #endregion
    #region Given
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(IEnumerable<IAggregateEvent> events);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(params IAggregateEvent[] definitions);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEventsFromJson(string jsonEvents);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEventsFromFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenQueryFilterChecker(
        IQueryFilterChecker<MultipleAggregateProjectionState<TProjectionPayload>> checker);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenScenario(Action initialAction);
    public Guid RunCreateCommand<TAggregatePayload>(ICreateAggregateCommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayload, new();
    public void RunChangeCommand<TAggregatePayload>(ChangeAggregateCommandBase<TAggregatePayload> command)
        where TAggregatePayload : IAggregatePayload, new();
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> GivenCommandExecutorAction(
        Action<AggregateTestCommandExecutor> action);
    #endregion

    #region Then
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenNotThrowsAnException();
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenThrowsAnException();
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenStateIs(
        MultipleAggregateProjectionState<TProjectionPayload> state);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenGetState(
        Action<MultipleAggregateProjectionState<TProjectionPayload>> stateAction);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenStateIsFromFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> WriteProjectionToFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenPayloadIs(TProjectionPayload payload);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenPayloadIsFromFile(string filename);
    public IMultipleAggregateProjectionTestHelper<TProjection, TProjectionPayload> ThenGetPayload(Action<TProjectionPayload> payloadAction);
    #endregion
    #region Get
    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new();
    public IReadOnlyCollection<IAggregateEvent> GetLatestEvents();
    #endregion
}

using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Testing.Command;
using Sekiban.Testing.Queries;
namespace Sekiban.Testing.Projection;

public interface IMultiProjectionTestHelper<TProjection, TProjectionPayload>
    where TProjection : IMultiProjector<TProjectionPayload>, new()
    where TProjectionPayload : IMultiProjectionPayload, new()
{


    #region When
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> WhenProjection();
    #endregion
    #region Given
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(IEnumerable<IAggregateEvent> events);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(params IAggregateEvent[] definitions);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenEventsFromJson(string jsonEvents);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenEventsFromFile(string filename);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(
        params (Guid aggregateId, Type aggregateType, IEventPayload payload)[] eventTouples);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenEvents(
        params (Guid aggregateId, IEventPayload payload)[] eventTouples);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenQueryChecker(
        IQueryChecker<MultiProjectionState<TProjectionPayload>> checker);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenScenario(Action initialAction);
    public Guid RunCreateCommand<TAggregatePayload>(ICreateAggregateCommand<TAggregatePayload> command, Guid? injectingAggregateId = null)
        where TAggregatePayload : IAggregatePayload, new();
    public void RunChangeCommand<TAggregatePayload>(ChangeAggregateCommandBase<TAggregatePayload> command)
        where TAggregatePayload : IAggregatePayload, new();
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> GivenCommandExecutorAction(
        Action<AggregateTestCommandExecutor> action);
    #endregion

    #region Then
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> ThenNotThrowsAnException();
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> ThenThrowsAnException();
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> ThenStateIs(
        MultiProjectionState<TProjectionPayload> state);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> ThenGetState(
        Action<MultiProjectionState<TProjectionPayload>> stateAction);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> ThenStateIsFromFile(string filename);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> WriteProjectionToFile(string filename);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> ThenPayloadIs(TProjectionPayload payload);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> ThenPayloadIsFromFile(string filename);
    public IMultiProjectionTestHelper<TProjection, TProjectionPayload> ThenGetPayload(Action<TProjectionPayload> payloadAction);
    #endregion
    #region Get
    public AggregateState<TEnvironmentAggregatePayload> GetAggregateState<TEnvironmentAggregatePayload>(Guid aggregateId)
        where TEnvironmentAggregatePayload : IAggregatePayload, new();
    public IReadOnlyCollection<IAggregateEvent> GetLatestEvents();
    #endregion
}

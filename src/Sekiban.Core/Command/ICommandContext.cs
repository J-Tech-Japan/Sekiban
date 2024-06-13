using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Context has feature to Get Current Aggregate State
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface ICommandContext<TAggregatePayload> where TAggregatePayload : IAggregatePayloadCommon
{
    /// <summary>
    ///     Get current Aggregate State
    ///     "Current" meaning if you have already yield return event, this function will return the state after adding yield
    ///     returned events.
    /// </summary>
    /// <returns>Current Aggregate State</returns>
    public AggregateState<TAggregatePayload> GetState();

    public ResultBox<T1> GetRequiredService<T1>() where T1 : class;
    public ResultBox<TwoValues<T1, T2>> GetRequiredService<T1, T2>() where T1 : class where T2 : class;
    public ResultBox<ThreeValues<T1, T2, T3>> GetRequiredService<T1, T2, T3>() where T1 : class where T2 : class where T3 : class;
    public ResultBox<FourValues<T1, T2, T3, T4>> GetRequiredService<T1, T2, T3, T4>()
        where T1 : class where T2 : class where T3 : class where T4 : class;

    public ResultBox<UnitValue> AppendEvent(IEventPayloadApplicableTo<TAggregatePayload> eventPayload);
}

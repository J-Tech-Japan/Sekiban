using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Context has feature to Get Current Aggregate State
/// </summary>
/// <typeparam name="TAggregate"></typeparam>
public interface ICommandContext<TAggregate> where TAggregate : IAggregatePayloadCommon
{
    /// <summary>
    ///     Get current Aggregate State
    ///     "Current" meaning if you have already yield return event, this function will return the state after adding yield
    ///     returned events.
    /// </summary>
    /// <returns>Current Aggregate State</returns>
    public AggregateState<TAggregate> GetState();
}
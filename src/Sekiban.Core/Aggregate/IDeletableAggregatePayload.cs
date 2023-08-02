namespace Sekiban.Core.Aggregate;

/// <summary>
///     Defines the Aggregate Payload State.
///     Application developer can implement this interface to define the state of your aggregate.
///     If you want to make your aggregate NOT deletable, please use <see cref="IAggregatePayload" />.
/// </summary>
public interface IDeletableAggregatePayload : IAggregatePayload, IDeletable;

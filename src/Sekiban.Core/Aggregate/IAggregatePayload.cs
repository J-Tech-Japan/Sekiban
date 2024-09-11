namespace Sekiban.Core.Aggregate;

/// <summary>
///     Common Aggregate Interface
///     You can implement this for your aggregate payload class.
///     If you want to make your aggregate deletable, please use
///     <see cref="IDeletableAggregatePayload{TAggregatePayload}" />.
/// </summary>
public interface IAggregatePayload<TAggregatePayload> : IAggregatePayloadGeneratable<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>, IEquatable<TAggregatePayload>;

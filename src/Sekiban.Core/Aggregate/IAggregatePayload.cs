namespace Sekiban.Core.Aggregate;

/// <summary>
///     Defines the Aggregate Payload State.
///     Application developer can implement this interface to define the state of your aggregate.
///     If you want to make your aggregate deletable, please use <see cref="IDeletableAggregatePayload" />.
/// </summary>
public interface IAggregatePayload
{
    /// <summary>
    ///     Aggregate Payload Version:
    ///     This version will be used to identify snapshot type.
    ///     If you update Payload Version, old snapshot will not be used.
    ///     e.g.
    ///     public string GetPayloadVersionIdentifier() => "20230101 1.0.0";
    /// </summary>
    /// <returns>Payload Version</returns>
    public string GetPayloadVersionIdentifier() => "initial";
}

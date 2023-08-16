namespace Sekiban.Core.Aggregate;

/// <summary>
///     Defines the Aggregate Payload State.
///     Note: Developer does not need to implement this interface.
///     It will be implement when you implement <see cref="IAggregatePayload" />.
/// </summary>
public interface IAggregatePayloadCommon : IAggregatePayloadCommonBase
{
    public static abstract IAggregatePayloadCommon CreateInitialPayload();
}
public interface IAggregatePayloadCommonBase
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

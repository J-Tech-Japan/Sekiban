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
namespace Sekiban.Core.Events;

/// <summary>
///     Internal interface to access event payload.
///     App Developer usually doesn't need to use this class.
/// </summary>
public interface IEventPayloadAccessor
{
    /// <summary>
    ///     Get Payload common interface
    /// </summary>
    /// <returns></returns>
    public IEventPayloadCommon GetPayload();
    /// <summary>
    ///     Get Typed Payload
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T? GetPayload<T>() where T : class, IEventPayloadCommon;
}

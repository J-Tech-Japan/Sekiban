namespace Sekiban.Core.Events;

/// <summary>
///     Event Version converter. App Developer will implement this when Event can be converted to new version.
/// </summary>
/// <typeparam name="TNewEventPayload"></typeparam>
public interface IEventPayloadConvertingTo<out TNewEventPayload> where TNewEventPayload : IEventPayloadCommon
{
    /// <summary>
    ///     Converting Old events to new events.
    /// </summary>
    /// <returns></returns>
    TNewEventPayload ConvertTo();
}

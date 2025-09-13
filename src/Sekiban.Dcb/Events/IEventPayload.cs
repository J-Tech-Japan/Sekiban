namespace Sekiban.Dcb.Events;

public interface IEventPayload
{
    // Empty interface for event payload marker
    T? As<T>() => this is T t ? t : default;
}

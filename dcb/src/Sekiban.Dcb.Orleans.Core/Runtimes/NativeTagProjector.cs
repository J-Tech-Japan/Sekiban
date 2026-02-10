using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation of ITagProjector.
///     Wraps a Func{ITagStatePayload, Event, ITagStatePayload} as an ITagProjector.
/// </summary>
public class NativeTagProjector : ITagProjector
{
    private readonly Func<ITagStatePayload, Event, ITagStatePayload> _projectFunc;

    public NativeTagProjector(Func<ITagStatePayload, Event, ITagStatePayload> projectFunc)
    {
        _projectFunc = projectFunc;
    }

    public ITagStatePayload Apply(ITagStatePayload? currentState, Event ev)
    {
        var state = currentState ?? new EmptyTagStatePayload();
        return _projectFunc(state, ev);
    }
}

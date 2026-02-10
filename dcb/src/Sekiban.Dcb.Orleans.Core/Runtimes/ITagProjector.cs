using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Individual tag projector instance (non-generic, instance-based).
///     Distinct from Tags.ITagProjector{T} which uses static abstract members.
///     C# implementation wraps Func{ITagStatePayload, Event, ITagStatePayload}.
///     WASM implementation calls into the WASM module.
/// </summary>
public interface ITagProjector
{
    ITagStatePayload Apply(ITagStatePayload? currentState, Event ev);
}

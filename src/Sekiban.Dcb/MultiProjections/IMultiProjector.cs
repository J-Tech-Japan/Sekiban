using ResultBoxes;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
/// Common multi projector interface.
/// </summary>
/// <summary>
///     Non-generic common metadata for multi projectors.
/// </summary>
public interface IMultiProjectorCommon
{
    string GetVersion();
}

/// <summary>
/// Generic specialisation of multi projector with a fixed payload type.
/// </summary>
public interface IMultiProjector<T> : IMultiProjectorCommon where T : notnull
{
    ResultBox<T> Project(T payload, Event ev);
    static abstract T GenerateInitialPayload();
    static abstract string GetMultiProjectorName();
}

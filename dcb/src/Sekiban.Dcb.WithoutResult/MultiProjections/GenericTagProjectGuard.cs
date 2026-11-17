using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Shared by the generic tag multi-projectors' public <c>Project</c>.
/// </summary>
internal static class GenericTagProjectGuard
{
    /// <summary>
    ///     The failure for a null payload on the path that would otherwise return it unchanged (the event carries no
    ///     tag of the projector's tag group). Annotated with the same boundary the projector's <c>Project</c> used in
    ///     10.x.
    /// </summary>
    internal static InvalidOperationException NoPayload(string projectorName, Event? ev) =>
        BoundaryGuard.Annotated(
            new InvalidOperationException(
                $"{projectorName}.Project produced no payload: the payload passed in was null and the event carried " +
                "no tag of the projector's tag group."),
            new BoundaryContext($"{projectorName}.Project", ev?.EventType));
}

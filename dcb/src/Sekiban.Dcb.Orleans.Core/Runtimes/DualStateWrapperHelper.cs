using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Encapsulates operations on DualStateProjectionWrapper{T}.
///     Uses IDualStateAccessor for type-safe access.
///     Construction is delegated to DualStateProjectionWrapperFactory in Sekiban.Dcb.Core.
/// </summary>
internal static class DualStateWrapperHelper
{
    public static IMultiProjectionPayload? CreateWrapper(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        DcbDomainTypes domainTypes,
        bool isRestoredFromSnapshot = false) =>
        DualStateProjectionWrapperFactory.Create(
            payload,
            projectorName,
            multiProjectorTypes,
            domainTypes.JsonSerializerOptions,
            isRestoredFromSnapshot);

    public static ResultBox<IProjectionState> ApplyEvent(
        IDualStateAccessor accessor,
        Event ev,
        string safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        var threshold = new SortableUniqueId(safeWindowThreshold);
        var updated = accessor.ProcessEventAs(ev, threshold, domainTypes);

        return ResultBox.FromValue<IProjectionState>(
            NativeProjectionState.FromDualStateAccessor(updated));
    }
}

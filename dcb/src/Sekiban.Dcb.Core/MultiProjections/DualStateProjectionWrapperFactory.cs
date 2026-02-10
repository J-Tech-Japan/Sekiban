using Sekiban.Dcb.Domains;
using System.Text.Json;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Factory for creating DualStateProjectionWrapper instances when the generic
///     type parameter T is not known at compile time.
///     Co-located with DualStateProjectionWrapper so constructor changes are easy to track.
/// </summary>
public static class DualStateProjectionWrapperFactory
{
    public static IMultiProjectionPayload? Create(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        JsonSerializerOptions jsonOptions,
        bool isRestoredFromSnapshot = false,
        int initialVersion = 0,
        Guid initialLastEventId = default,
        string? initialLastSortableUniqueId = null)
    {
        var wrapperType = typeof(DualStateProjectionWrapper<>).MakeGenericType(payload.GetType());

        if (isRestoredFromSnapshot)
        {
            return Activator.CreateInstance(
                wrapperType,
                payload,
                projectorName,
                multiProjectorTypes,
                jsonOptions,
                initialVersion,
                initialLastEventId,
                initialLastSortableUniqueId,
                true) as IMultiProjectionPayload;
        }

        return Activator.CreateInstance(
            wrapperType,
            payload,
            projectorName,
            multiProjectorTypes,
            jsonOptions,
            initialVersion,
            initialLastEventId,
            initialLastSortableUniqueId) as IMultiProjectionPayload;
    }
}

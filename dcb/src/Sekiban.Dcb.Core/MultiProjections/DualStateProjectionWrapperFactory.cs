using Sekiban.Dcb.Domains;
using System.Reflection;
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
        int initialVersion = 0,
        Guid initialLastEventId = default,
        string? initialLastSortableUniqueId = null)
        => CreateCore(
            payload,
            projectorName,
            multiProjectorTypes,
            jsonOptions,
            initialVersion,
            initialLastEventId,
            initialLastSortableUniqueId,
            false);

    public static IMultiProjectionPayload? CreateFromRestoredSnapshot(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        JsonSerializerOptions jsonOptions,
        int initialVersion = 0,
        Guid initialLastEventId = default,
        string? initialLastSortableUniqueId = null)
        => CreateCore(
            payload,
            projectorName,
            multiProjectorTypes,
            jsonOptions,
            initialVersion,
            initialLastEventId,
            initialLastSortableUniqueId,
            true);

    private static IMultiProjectionPayload? CreateCore(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        JsonSerializerOptions jsonOptions,
        int initialVersion,
        Guid initialLastEventId,
        string? initialLastSortableUniqueId,
        bool isRestoredFromSnapshot)
    {
        var wrapperType = typeof(DualStateProjectionWrapper<>).MakeGenericType(payload.GetType());

        if (isRestoredFromSnapshot)
        {
            return Activator.CreateInstance(
                wrapperType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args:
                [
                    payload,
                    projectorName,
                    multiProjectorTypes,
                    jsonOptions,
                    initialVersion,
                    initialLastEventId,
                    initialLastSortableUniqueId,
                    true
                ],
                culture: null) as IMultiProjectionPayload;
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

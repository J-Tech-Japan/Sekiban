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
            verifySafeStateIsolation: false);

    internal static IMultiProjectionPayload? Create(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        JsonSerializerOptions jsonOptions,
        bool verifySafeStateIsolation)
        => CreateCore(
            payload,
            projectorName,
            multiProjectorTypes,
            jsonOptions,
            0,
            default,
            null,
            verifySafeStateIsolation);

    public static IMultiProjectionPayload? CreateFromRestoredSnapshot(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        int initialVersion = 0,
        Guid initialLastEventId = default,
        string? initialLastSortableUniqueId = null)
        => CreateFromRestoredSnapshotCore(
            payload,
            projectorName,
            multiProjectorTypes,
            domainTypes,
            safeWindowThreshold,
            initialVersion,
            initialLastEventId,
            initialLastSortableUniqueId,
            verifySafeStateIsolation: false);

    internal static IMultiProjectionPayload? CreateFromRestoredSnapshot(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        int initialVersion,
        Guid initialLastEventId,
        string? initialLastSortableUniqueId,
        bool verifySafeStateIsolation)
        => CreateFromRestoredSnapshotCore(
            payload,
            projectorName,
            multiProjectorTypes,
            domainTypes,
            safeWindowThreshold,
            initialVersion,
            initialLastEventId,
            initialLastSortableUniqueId,
            verifySafeStateIsolation);

    private static IMultiProjectionPayload? CreateFromRestoredSnapshotCore(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        int initialVersion,
        Guid initialLastEventId,
        string? initialLastSortableUniqueId,
        bool verifySafeStateIsolation)
    {
        var clonedPayload = ClonePayload(
            payload,
            projectorName,
            multiProjectorTypes,
            domainTypes,
            safeWindowThreshold);

        var wrapperType = typeof(DualStateProjectionWrapper<>).MakeGenericType(payload.GetType());
        return Activator.CreateInstance(
            wrapperType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                payload,
                clonedPayload,
                projectorName,
                multiProjectorTypes,
                domainTypes.JsonSerializerOptions,
                initialVersion,
                initialLastEventId,
                initialLastSortableUniqueId,
                verifySafeStateIsolation
            ],
            culture: null) as IMultiProjectionPayload;
    }

    /// <summary>
    ///     Creates the restored dual-state wrapper from independently deserialized safe and unsafe payload instances.
    ///     The streaming restore seam uses this overload after teeing the original payload stream to a temporary file and
    ///     deserializing it a second time, avoiding the legacy serialize-to-byte[] clone in
    ///     <see cref="CreateFromRestoredSnapshot(IMultiProjectionPayload,string,ICoreMultiProjectorTypes,DcbDomainTypes,string,int,Guid,string?)" />.
    /// </summary>
    internal static IMultiProjectionPayload? CreateFromRestoredSnapshot(
        IMultiProjectionPayload safePayload,
        IMultiProjectionPayload unsafePayload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        int initialVersion = 0,
        Guid initialLastEventId = default,
        string? initialLastSortableUniqueId = null,
        bool verifySafeStateIsolation = false)
    {
        if (safePayload.GetType() != unsafePayload.GetType())
        {
            throw new InvalidOperationException(
                $"Streaming restore clone type mismatch for projector '{projectorName}'.");
        }

        if (safePayload is IMutatesProjectionInput && ReferenceEquals(safePayload, unsafePayload))
        {
            unsafePayload = ClonePayload(
                safePayload,
                projectorName,
                multiProjectorTypes,
                domainTypes,
                safeWindowThreshold);
        }

        var wrapperType = typeof(DualStateProjectionWrapper<>).MakeGenericType(safePayload.GetType());
        return Activator.CreateInstance(
            wrapperType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args:
            [
                safePayload,
                unsafePayload,
                projectorName,
                multiProjectorTypes,
                domainTypes.JsonSerializerOptions,
                initialVersion,
                initialLastEventId,
                initialLastSortableUniqueId,
                verifySafeStateIsolation
            ],
            culture: null) as IMultiProjectionPayload;
    }

    private static IMultiProjectionPayload? CreateCore(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        JsonSerializerOptions jsonOptions,
        int initialVersion,
        Guid initialLastEventId,
        string? initialLastSortableUniqueId,
        bool verifySafeStateIsolation)
    {
        var wrapperType = typeof(DualStateProjectionWrapper<>).MakeGenericType(payload.GetType());

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
                verifySafeStateIsolation
            ],
            culture: null) as IMultiProjectionPayload;
    }

    internal static IMultiProjectionPayload ClonePayload(
        IMultiProjectionPayload payload,
        string projectorName,
        ICoreMultiProjectorTypes multiProjectorTypes,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold)
    {
        var serializeResult = multiProjectorTypes.Serialize(
            projectorName,
            domainTypes,
            safeWindowThreshold,
            payload);
        if (!serializeResult.IsSuccess)
        {
            throw serializeResult.GetException();
        }

        var deserializeResult = multiProjectorTypes.Deserialize(
            projectorName,
            domainTypes,
            safeWindowThreshold,
            serializeResult.GetValue().Data);
        if (!deserializeResult.IsSuccess)
        {
            throw deserializeResult.GetException();
        }

        return deserializeResult.GetValue();
    }
}

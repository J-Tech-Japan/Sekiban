namespace Sekiban.Dcb.Storage;

/// <summary>
///     Factory for creating ServiceId-scoped event stores.
/// </summary>
public interface IEventStoreFactory
{
    /// <summary>
    ///     Creates an <see cref="IEventStore"/> scoped to the specified ServiceId.
    /// </summary>
    IEventStore CreateForService(string serviceId);
}

/// <summary>
///     Factory for creating ServiceId-scoped multi-projection state stores.
/// </summary>
public interface IMultiProjectionStateStoreFactory
{
    /// <summary>
    ///     Creates an <see cref="IMultiProjectionStateStore"/> scoped to the specified ServiceId.
    /// </summary>
    IMultiProjectionStateStore CreateForService(string serviceId);
}

namespace Sekiban.Dcb.Primitives;

/// <summary>
///     Builds primitive projection instance keys for a given service and projector.
/// </summary>
public interface IPrimitiveProjectionKeyFactory
{
    /// <summary>
    ///     Build a key for a multi-projection instance.
    /// </summary>
    string GetMultiProjectionKey(string projectorName, string serviceId);

    /// <summary>
    ///     Build a key for a tag state instance.
    /// </summary>
    string GetTagStateKey(string tagStateId, string serviceId);
}

namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Interface for MultiProjectionPayload
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
public interface IMultiProjectionPayloadGeneratePayload<TProjectionPayload> : IMultiProjectionPayloadCommon
    where TProjectionPayload : IMultiProjectionPayloadCommon
{
    /// <summary>
    ///     Generate Default Payload in Static.
    /// </summary>
    /// <returns></returns>
    public static abstract TProjectionPayload CreateInitialPayload();
}

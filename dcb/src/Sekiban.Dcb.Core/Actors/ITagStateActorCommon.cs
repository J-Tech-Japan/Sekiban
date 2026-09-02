using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

public interface ITagStateActorCommon
{
    Task<SerializableTagState> GetStateAsync();

    /// <summary>
    ///     Additive cancellation entry point. Existing actor implementations retain the token-less behavior until they
    ///     opt in by overriding this member.
    /// </summary>
    Task<SerializableTagState> GetStateAsync(CancellationToken cancellationToken) => GetStateAsync();

    Task<string> GetTagStateActorIdAsync();
}

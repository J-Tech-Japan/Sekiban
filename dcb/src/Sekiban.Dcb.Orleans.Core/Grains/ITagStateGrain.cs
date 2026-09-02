using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans grain interface for tag state management
///     Matches the interface of GeneralTagStateActor
/// </summary>
public interface ITagStateGrain : IGrainWithStringKey
{
    /// <summary>
    ///     Gets the tag state actor ID
    /// </summary>
    Task<string> GetTagStateActorIdAsync();

    /// <summary>
    ///     Get the current state
    /// </summary>
    Task<SerializableTagState> GetStateAsync();

    /// <summary>
    ///     Get the current state while propagating cancellation to the grain implementation.
    /// </summary>
    Task<SerializableTagState> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Get the tag state
    /// </summary>
    Task<TagState> GetTagStateAsync();

    /// <summary>
    ///     Get the tag state while propagating cancellation to the grain implementation.
    /// </summary>
    Task<TagState> GetTagStateAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Update the state
    /// </summary>
    Task UpdateStateAsync(TagState newState);

    /// <summary>
    ///     Clear the cache
    /// </summary>
    Task ClearCacheAsync();
}

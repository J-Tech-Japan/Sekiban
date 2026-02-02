using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Tag state actor abstraction used by Orleans grains.
/// </summary>
public interface ITagStateActor : ITagStateActorCommon
{
    Task<TagState> GetTagStateAsync();

    Task UpdateStateAsync(TagState newState);

    Task ClearCacheAsync();
}

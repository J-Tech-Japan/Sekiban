using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans-specific implementation of ITagStatePersistent using grain state
/// </summary>
internal class OrleansTagStatePersistent : ITagStatePersistent
{
    private readonly IPersistentState<TagStateCacheState> _cache;

    public OrleansTagStatePersistent(IPersistentState<TagStateCacheState> cache) => _cache = cache;

    public Task<TagState?> LoadStateAsync()
    {
        if (_cache.State?.CachedState != null)
        {
            return Task.FromResult<TagState?>(_cache.State.CachedState);
        }
        return Task.FromResult<TagState?>(null);
    }

    public async Task SaveStateAsync(TagState state)
    {
        _cache.State = new TagStateCacheState { CachedState = state };
        await _cache.WriteStateAsync();
    }

    public async Task ClearStateAsync()
    {
        _cache.State = new TagStateCacheState();
        await _cache.WriteStateAsync();
    }
}

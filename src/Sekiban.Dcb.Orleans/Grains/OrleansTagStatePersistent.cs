using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Domains;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans-specific implementation of ITagStatePersistent using grain state
///     Converts between TagState and SerializableTagState for storage
/// </summary>
internal class OrleansTagStatePersistent : ITagStatePersistent
{
    private readonly IPersistentState<TagStateCacheState> _cache;
    private readonly ITagStatePayloadTypes _payloadTypes;

    public OrleansTagStatePersistent(
        IPersistentState<TagStateCacheState> cache,
        ITagStatePayloadTypes payloadTypes)
    {
        _cache = cache;
        _payloadTypes = payloadTypes;
    }

    public Task<TagState?> LoadStateAsync()
    {
        if (_cache.State?.CachedState != null)
        {
            var serializable = _cache.State.CachedState;
            
            // Deserialize payload from SerializableTagState
            var deserializeResult = _payloadTypes.DeserializePayload(
                serializable.TagPayloadName,
                serializable.Payload);
            
            if (!deserializeResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize payload: {deserializeResult.GetException().Message}");
            }
            
            var tagState = new TagState(
                deserializeResult.GetValue(),
                serializable.Version,
                serializable.LastSortedUniqueId,
                serializable.TagGroup,
                serializable.TagContent,
                serializable.TagProjector,
                serializable.ProjectorVersion);
            
            return Task.FromResult<TagState?>(tagState);
        }
        return Task.FromResult<TagState?>(null);
    }

    public async Task SaveStateAsync(TagState state)
    {
        // Convert TagState to SerializableTagState
        var serializeResult = _payloadTypes.SerializePayload(state.Payload);
        if (!serializeResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to serialize payload: {serializeResult.GetException().Message}");
        }
        
        var serializable = new SerializableTagState(
            serializeResult.GetValue(),
            state.Version,
            state.LastSortedUniqueId,
            state.TagGroup,
            state.TagContent,
            state.TagProjector,
            state.Payload.GetType().Name,
            state.ProjectorVersion);
        
        _cache.State = new TagStateCacheState { CachedState = serializable };
        await _cache.WriteStateAsync();
    }

    public async Task ClearStateAsync()
    {
        _cache.State = new TagStateCacheState();
        await _cache.WriteStateAsync();
    }
}

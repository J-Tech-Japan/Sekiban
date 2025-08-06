using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Orleans;

/// <summary>
/// Orleans-specific implementation of IActorObjectAccessor
/// Manages Orleans grains for tag consistency and state management
/// </summary>
public class OrleansActorObjectAccessor : IActorObjectAccessor
{
    private readonly IClusterClient _clusterClient;
    private readonly IEventStore _eventStore;
    private readonly DcbDomainTypes _domainTypes;
    
    public OrleansActorObjectAccessor(IClusterClient clusterClient, IEventStore eventStore, DcbDomainTypes domainTypes)
    {
        _clusterClient = clusterClient;
        _eventStore = eventStore;
        _domainTypes = domainTypes;
    }
    
    public async Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
    {
        if (string.IsNullOrEmpty(actorId))
        {
            return ResultBox.Error<T>(new ArgumentException("Actor ID cannot be empty"));
        }
        
        try
        {
            var actorType = typeof(T);
            
            if (actorType == typeof(ITagConsistentActorCommon))
            {
                var grain = _clusterClient.GetGrain<Grains.ITagConsistentGrain>(actorId);
                var wrapper = new TagConsistentGrainWrapper(grain);
                return ResultBox.FromValue((T)(object)wrapper);
            }
            else if (actorType == typeof(ITagStateActorCommon))
            {
                var grain = _clusterClient.GetGrain<Grains.ITagStateGrain>(actorId);
                var wrapper = new TagStateGrainWrapper(grain);
                return ResultBox.FromValue((T)(object)wrapper);
            }
            else
            {
                return ResultBox.Error<T>(new NotSupportedException($"Actor type {actorType.Name} is not supported"));
            }
        }
        catch (Exception ex)
        {
            return ResultBox.Error<T>(ex);
        }
    }
    
    public async Task<bool> ActorExistsAsync(string actorId)
    {
        // In Orleans, grains are created on demand, so we check if the tag exists in the event store
        var tag = ParseTagFromActorId(actorId);
        if (tag == null)
        {
            return false;
        }
        
        var result = await _eventStore.TagExistsAsync(tag);
        return result.IsSuccess && result.GetValue();
    }
    
    private ITag? ParseTagFromActorId(string actorId)
    {
        if (string.IsNullOrEmpty(actorId))
        {
            return null;
        }
        
        return new SimpleTag(actorId);
    }
    
    /// <summary>
    /// Simple tag implementation for wrapping actor IDs
    /// </summary>
    private record SimpleTag(string TagValue) : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTag() => TagValue;
        public string GetTagGroup() => TagValue.Split(':')[0];
    }
    
    /// <summary>
    /// Wrapper for TagConsistentGrain to implement ITagConsistentActorCommon
    /// </summary>
    private class TagConsistentGrainWrapper : ITagConsistentActorCommon
    {
        private readonly Grains.ITagConsistentGrain _grain;
        
        public TagConsistentGrainWrapper(Grains.ITagConsistentGrain grain)
        {
            _grain = grain;
        }
        
        public Task<string> GetTagActorIdAsync()
        {
            return _grain.GetTagActorIdAsync();
        }
        
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
        {
            return _grain.GetLatestSortableUniqueIdAsync();
        }
        
        public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId)
        {
            return _grain.MakeReservationAsync(lastSortableUniqueId);
        }
        
        public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation)
        {
            return _grain.ConfirmReservationAsync(reservation);
        }
        
        public Task<bool> CancelReservationAsync(TagWriteReservation reservation)
        {
            return _grain.CancelReservationAsync(reservation);
        }
    }
    
    /// <summary>
    /// Wrapper for TagStateGrain to implement ITagStateActorCommon
    /// </summary>
    private class TagStateGrainWrapper : ITagStateActorCommon
    {
        private readonly Grains.ITagStateGrain _grain;
        
        public TagStateGrainWrapper(Grains.ITagStateGrain grain)
        {
            _grain = grain;
        }
        
        public Task<SerializableTagState> GetStateAsync()
        {
            return _grain.GetStateAsync();
        }
        
        public Task<string> GetTagStateActorIdAsync()
        {
            return _grain.GetTagStateActorIdAsync();
        }
    }
}
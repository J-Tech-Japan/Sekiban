using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Orleans-specific implementation of IActorObjectAccessor
///     Manages Orleans grains for tag consistency and state management
/// </summary>
public class OrleansActorObjectAccessor : IActorObjectAccessor
{
    private readonly IClusterClient _clusterClient;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;

    public OrleansActorObjectAccessor(IClusterClient clusterClient, IEventStore eventStore, DcbDomainTypes domainTypes)
    {
        _clusterClient = clusterClient;
        _eventStore = eventStore;
        _domainTypes = domainTypes;
    }

    public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
    {
        if (string.IsNullOrEmpty(actorId))
        {
            return Task.FromResult(ResultBox.Error<T>(new ArgumentException("Actor ID cannot be empty")));
        }

        try
        {
            var actorType = typeof(T);

            if (actorType == typeof(ITagConsistentActorCommon))
            {
                var grain = _clusterClient.GetGrain<ITagConsistentGrain>(actorId);
                var wrapper = new TagConsistentGrainWrapper(grain);
                return Task.FromResult(ResultBox.FromValue((T)(object)wrapper));
            }
            if (actorType == typeof(ITagStateActorCommon))
            {
                var grain = _clusterClient.GetGrain<ITagStateGrain>(actorId);
                var wrapper = new TagStateGrainWrapper(grain);
                return Task.FromResult(ResultBox.FromValue((T)(object)wrapper));
            }
            return Task.FromResult(
                ResultBox.Error<T>(new NotSupportedException($"Actor type {actorType.Name} is not supported")));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<T>(ex));
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
    ///     Simple tag implementation for wrapping actor IDs
    /// </summary>
    private record SimpleTag(string TagValue) : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTagContent() =>
            TagValue.Split(':').Length > 1 ? string.Join(':', TagValue.Split(':').Skip(1)) : "";
        public string GetTagGroup() => TagValue.Split(':')[0];
    }

    /// <summary>
    ///     Wrapper for TagConsistentGrain to implement ITagConsistentActorCommon
    /// </summary>
    private class TagConsistentGrainWrapper : ITagConsistentActorCommon
    {
        private readonly ITagConsistentGrain _grain;

        public TagConsistentGrainWrapper(ITagConsistentGrain grain) => _grain = grain;

        public Task<string> GetTagActorIdAsync() => _grain.GetTagActorIdAsync();

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _grain.GetLatestSortableUniqueIdAsync();

        public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId) =>
            _grain.MakeReservationAsync(lastSortableUniqueId);

        public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) =>
            _grain.ConfirmReservationAsync(reservation);

        public Task<bool> CancelReservationAsync(TagWriteReservation reservation) =>
            _grain.CancelReservationAsync(reservation);

        public Task NotifyEventWrittenAsync() => _grain.NotifyEventWrittenAsync();
    }

    /// <summary>
    ///     Wrapper for TagStateGrain to implement ITagStateActorCommon
    /// </summary>
    private class TagStateGrainWrapper : ITagStateActorCommon
    {
        private readonly ITagStateGrain _grain;

        public TagStateGrainWrapper(ITagStateGrain grain) => _grain = grain;

        public Task<SerializableTagState> GetStateAsync() => _grain.GetStateAsync();

        public Task<string> GetTagStateActorIdAsync() => _grain.GetTagStateActorIdAsync();
    }
}

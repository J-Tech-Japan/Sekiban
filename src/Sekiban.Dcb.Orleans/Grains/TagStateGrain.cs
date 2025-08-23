using Orleans;
using Orleans.Runtime;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans grain implementation for tag state management
///     Delegates to GeneralTagStateActor for actual functionality
/// </summary>
public class TagStateGrain : Grain, ITagStateGrain
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private GeneralTagStateActor? _actor;
    private readonly IPersistentState<TagStateCacheState> _cache;

    public TagStateGrain(
        IEventStore eventStore, 
        DcbDomainTypes domainTypes, 
        IActorObjectAccessor actorAccessor,
        [PersistentState("tagStateCache", "OrleansStorage")] IPersistentState<TagStateCacheState> cache)
    {
        _eventStore = eventStore;
        _domainTypes = domainTypes;
        _actorAccessor = actorAccessor;
        _cache = cache;
    }

    public Task<string> GetTagStateActorIdAsync()
    {
        if (_actor == null)
        {
            return Task.FromResult(string.Empty);
        }

        return _actor.GetTagStateActorIdAsync();
    }

    public Task<SerializableTagState> GetStateAsync()
    {
        if (_actor == null)
        {
            // Return empty serializable state
            return Task.FromResult(
                new SerializableTagState(
                    Array.Empty<byte>(),
                    0,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    nameof(EmptyTagStatePayload),
                    string.Empty));
        }

        return _actor.GetStateAsync();
    }

    public Task<TagState> GetTagStateAsync()
    {
        if (_actor == null)
        {
            // Return empty tag state
            return Task.FromResult(
                new TagState(
                    new EmptyTagStatePayload(),
                    0,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty));
        }

        return _actor.GetTagStateAsync();
    }

    public Task UpdateStateAsync(TagState newState)
    {
        if (_actor == null)
        {
            return Task.CompletedTask;
        }

        return _actor.UpdateStateAsync(newState);
    }

    public Task ClearCacheAsync()
    {
        if (_actor == null)
        {
            return Task.CompletedTask;
        }

        return _actor.ClearCacheAsync();
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Extract tag state ID from grain key
        var tagStateId = this.GetPrimaryKeyString();

        // Create the actor instance with Orleans-specific cache persistence
        var tagStatePersistent = new OrleansTagStatePersistent(_cache);
        _actor = new GeneralTagStateActor(
            tagStateId, 
            _eventStore, 
            _domainTypes, 
            new TagStateOptions(),
            _actorAccessor,
            tagStatePersistent);

        return base.OnActivateAsync(cancellationToken);
    }
}

/// <summary>
/// Orleans-specific implementation of ITagStatePersistent using grain state
/// </summary>
internal class OrleansTagStatePersistent : ITagStatePersistent
{
    private readonly IPersistentState<TagStateCacheState> _cache;

    public OrleansTagStatePersistent(IPersistentState<TagStateCacheState> cache)
    {
        _cache = cache;
    }

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

/// <summary>
/// State object for caching tag state in Orleans grain storage
/// </summary>
[GenerateSerializer]
public class TagStateCacheState
{
    [Id(0)]
    public TagState? CachedState { get; set; }
}

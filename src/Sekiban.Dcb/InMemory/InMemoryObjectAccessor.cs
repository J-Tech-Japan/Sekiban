using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Storage;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of IActorObjectAccessor
///     Manages and provides access to actor instances
/// </summary>
public class InMemoryObjectAccessor : IActorObjectAccessor
{
    private readonly ConcurrentDictionary<string, object> _actors = new();
    private readonly object _creationLock = new();
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;

    /// <summary>
    ///     Gets the count of currently cached actors
    /// </summary>
    public int ActorCount => _actors.Count;

    public InMemoryObjectAccessor(IEventStore eventStore, DcbDomainTypes domainTypes)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
    }

    public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Task.FromResult(
                ResultBox.Error<T>(new ArgumentException("Actor ID cannot be null or empty", nameof(actorId))));
        }

        try
        {
            // Check if actor already exists
            if (_actors.TryGetValue(actorId, out var existingActor))
            {
                if (existingActor is T typedActor)
                {
                    return Task.FromResult(ResultBox.FromValue(typedActor));
                }

                return Task.FromResult(
                    ResultBox.Error<T>(
                        new InvalidCastException($"Actor {actorId} exists but is not of type {typeof(T).Name}")));
            }

            // Create new actor based on the requested type
            lock (_creationLock)
            {
                // Double-check in case another thread created it
                if (_actors.TryGetValue(actorId, out existingActor))
                {
                    if (existingActor is T typedActor)
                    {
                        return Task.FromResult(ResultBox.FromValue(typedActor));
                    }

                    return Task.FromResult(
                        ResultBox.Error<T>(
                            new InvalidCastException($"Actor {actorId} exists but is not of type {typeof(T).Name}")));
                }

                // Create the appropriate actor
                var newActor = CreateActor<T>(actorId);
                if (newActor == null)
                {
                    return Task.FromResult(
                        ResultBox.Error<T>(new NotSupportedException($"Actor type {typeof(T).Name} is not supported")));
                }

                _actors[actorId] = newActor;
                return Task.FromResult(ResultBox.FromValue(newActor));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ResultBox.Error<T>(ex));
        }
    }

    public Task<bool> ActorExistsAsync(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_actors.ContainsKey(actorId));
    }

    private T? CreateActor<T>(string actorId) where T : class
    {
        // Parse the actor ID to determine the type
        var parts = actorId.Split(':');

        // Create TagConsistentActor
        if (typeof(T) == typeof(ITagConsistentActorCommon) && parts.Length >= 2)
        {
            // Format: "TagGroup:TagContent"
            var tagName = actorId;
            return new GeneralTagConsistentActor(
                tagName,
                _eventStore,
                new TagConsistentActorOptions(),
                _domainTypes) as T;
        }

        // Create TagStateActor
        if (typeof(T) == typeof(ITagStateActorCommon) && parts.Length >= 3)
        {
            // Format: "TagGroup:TagContent:TagProjectorName"
            return new GeneralTagStateActor(actorId, _eventStore, _domainTypes, this) as T;
        }

        return null;
    }

    /// <summary>
    ///     Clears all cached actors
    ///     Useful for testing and cleanup
    /// </summary>
    public void ClearAllActors()
    {
        _actors.Clear();
    }

    /// <summary>
    ///     Removes a specific actor from the cache
    /// </summary>
    public bool RemoveActor(string actorId) => _actors.TryRemove(actorId, out _);
}

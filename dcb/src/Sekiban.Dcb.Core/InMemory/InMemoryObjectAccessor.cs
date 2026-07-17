using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using System.Collections.Concurrent;
using System.Linq;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of IActorObjectAccessor
///     Manages and provides access to actor instances
/// </summary>
[Obsolete(
    "Moved to Sekiban.Dcb.Core.Testing (namespace Sekiban.Dcb.Testing). This type is volatile/in-process and is for tests only; it lives in a production package for historical reasons, which is how it reached production once. Behaviour is unchanged and it will not be removed before the next major version.")]
public class InMemoryObjectAccessor : IActorObjectAccessor, IServiceProvider, IExecutorRuntimeDescriptorProvider
{
    /// <summary>
    ///     Actors are objects in a dictionary in this process. No cluster, no placement, no coordination with anything
    ///     else — which is exactly what a unit test wants, and exactly what production must not have.
    /// </summary>
    public ExecutorRuntimeDescriptor DescribeRuntime() =>
        new(ExecutorRuntimeKind.TestingInProcess, "InMemory (in-process actors)");

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

    public DcbDomainTypes DomainTypes => _domainTypes;

    /// <summary>
    /// Simple IServiceProvider implementation. For now we don't resolve external services,
    /// but query execution path requires a non-null provider; return null for any service type.
    /// </summary>
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(DcbDomainTypes))
        {
            return _domainTypes;
        }

        if (serviceType == typeof(IEventTypes))
        {
            return _domainTypes.EventTypes;
        }

        if (serviceType == typeof(ITagProjectorTypes))
        {
            return _domainTypes.TagProjectorTypes;
        }

        if (serviceType == typeof(ITagTypes))
        {
            return _domainTypes.TagTypes;
        }

        if (serviceType == typeof(ITagStatePayloadTypes))
        {
            return _domainTypes.TagStatePayloadTypes;
        }

        return null;
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
                _domainTypes.TagTypes) as T;
        }

        // Create TagStateActor
        if (typeof(T) == typeof(ITagStateActorCommon) && parts.Length >= 3)
        {
            // Format: "TagGroup:TagContent:TagProjectorName"
            return new GeneralTagStateActor(
                actorId,
                _eventStore,
                _domainTypes.EventTypes,
                _domainTypes.TagProjectorTypes,
                _domainTypes.TagTypes,
                _domainTypes.TagStatePayloadTypes,
                this) as T;
        }

        // Create MultiProjectionActor (projectorName passed as actorId)
        // Create MultiProjectionActor (projectorName passed as actorId)
        // Use IsAssignableFrom plus name match fallback to avoid edge cases with type identity (e.g. test shadow copies)
        if (typeof(T) == typeof(GeneralMultiProjectionActor) ||
            typeof(GeneralMultiProjectionActor).IsAssignableFrom(typeof(T)) ||
            typeof(T).Name == nameof(GeneralMultiProjectionActor))
        {
            var projectorName = actorId; // actorId is the projector name
            var actor = new GeneralMultiProjectionActor(_domainTypes, projectorName);

            // Initial catch-up: feed all existing events. Failures used to be swallowed here, which made a projection
            // that could NOT be built present as an empty, successful one — the exact silence issue #1075 was about.
            // Now a failed read and a failed fold both propagate: GetActorAsync's outer catch turns whatever is thrown
            // into ResultBox.Error<T>, so the original exception reaches that boundary and the query fails instead of
            // returning empty. GetAwaiter().GetResult() is used over Wait() so the fold's own exception surfaces, not
            // the AggregateException Wait() would wrap it in.
            var read = _eventStore.ReadAllSerializableEventsAsync().GetAwaiter().GetResult();
            if (!read.IsSuccess)
            {
                throw read.GetException();
            }

            var events = read.GetValue().ToList();
            if (events.Count > 0)
            {
                actor.AddSerializableEventsAsync(events, finishedCatchUp: true).GetAwaiter().GetResult();
            }

            return actor as T;
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

    internal IReadOnlyList<GeneralMultiProjectionActor> GetMultiProjectionActorsSnapshot() =>
        _actors.Values.OfType<GeneralMultiProjectionActor>().ToList();
}

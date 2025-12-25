using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     Minimal in-memory bridge that forwards newly persisted events to cached multi-projection actors.
///     Only actors that were previously requested are updated; new actors still rely on catch-up replay.
/// </summary>
public sealed class InMemoryMultiProjectionEventPublisher : IEventPublisher
{
    private readonly InMemoryObjectAccessor _objectAccessor;

    public InMemoryMultiProjectionEventPublisher(InMemoryObjectAccessor objectAccessor)
    {
        _objectAccessor = objectAccessor ?? throw new ArgumentNullException(nameof(objectAccessor));
    }

    public Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        CancellationToken cancellationToken = default)
    {
        if (events == null || events.Count == 0)
        {
            return Task.CompletedTask;
        }

        var actors = _objectAccessor.GetMultiProjectionActorsSnapshot();
        if (actors.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Process synchronously to keep tests deterministic and avoid fire-and-forget failures.
        var eventBatch = events.Select(tuple => tuple.Event).ToList();
        foreach (var actor in actors)
        {
            try
            {
                actor.AddEventsAsync(eventBatch, finishedCatchUp: true, EventSource.Stream)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // In-memory publisher is best-effort; swallow exceptions to match fire-and-forget behavior.
            }
        }

        return Task.CompletedTask;
    }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Actors;

/// <summary>
/// No-op publisher that intentionally does nothing when asked to publish events.
/// </summary>
public sealed class NonEventPublisher : IEventPublisher
{
    /// <summary>
    /// Does nothing and completes immediately.
    /// </summary>
    public Task PublishAsync(IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITagCommon> Tags)> events, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

using System;
using System.Collections.Generic;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Default resolver that routes all events to Orleans provider "EventStreamProvider",
/// namespace "AllEvents" and id Guid.Empty, mirroring AggregateEventHandlerGrain.
/// </summary>
public class DefaultOrleansStreamDestinationResolver : IStreamDestinationResolver
{
    private readonly string _providerName;
    private readonly string _namespace;
    private readonly Guid _streamId;

    public DefaultOrleansStreamDestinationResolver(
        string providerName = "EventStreamProvider",
        string @namespace = "AllEvents",
        Guid? streamId = null)
    {
        _providerName = providerName;
        _namespace = @namespace;
        _streamId = streamId ?? Guid.Empty;
    }

    public IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags)
    {
        yield return new OrleansSekibanStream(_providerName, _namespace, _streamId);
    }
}

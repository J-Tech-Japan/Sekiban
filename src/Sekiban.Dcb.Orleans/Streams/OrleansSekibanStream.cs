using System;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Orleans stream descriptor holding provider, namespace, and id.
/// </summary>
public class OrleansSekibanStream : ISekibanStream
{
    public string ProviderName { get; }
    public string StreamNamespace { get; }
    public Guid StreamId { get; }

    public OrleansSekibanStream(string providerName, string streamNamespace, Guid streamId)
    {
        ProviderName = providerName;
        StreamNamespace = streamNamespace;
        StreamId = streamId;
    }

    public string GetTopic(Event evt) => StreamNamespace;
}

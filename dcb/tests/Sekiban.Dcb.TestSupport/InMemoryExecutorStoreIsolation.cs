using System.Reflection;
using System.Text;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     Shared check for the built-in store of the obsolete parameterless-store <c>InMemoryDcbExecutor</c> constructors:
///     the stored history keeps its own copy of every payload and tag list, so a caller changing an event it wrote or
///     read never changes what the store returns later.
/// </summary>
public static class InMemoryExecutorStoreIsolation
{
    public static async Task AssertStoredHistoryIsIsolatedAsync(object executor)
    {
        var store = (IEventStore)executor.GetType()
            .GetField("_eventStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(executor)!;
        var original = Encoding.UTF8.GetBytes("{\"value\":\"original\"}");
        var written = new SerializableEvent(
            (byte[])original.Clone(),
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            Guid.NewGuid(),
            new EventMetadata("causation", "correlation", "user"),
            ["Isolation:1"],
            "IsolationEvent");

        var result = await store.WriteSerializableEventsAsync([written]);
        result.Events[0].Payload[0] = (byte)'X';
        written.Tags.Add("Isolation:mutated");

        var read = Assert.Single(await store.ReadAllSerializableEventsAsync());
        Assert.Equal(original, read.Payload);
        Assert.Equal(["Isolation:1"], read.Tags);

        read.Payload[0] = (byte)'Z';
        read.Tags.Clear();
        var again = Assert.IsType<SerializableEvent>(await store.ReadSerializableEventAsync(written.Id));
        Assert.Equal(original, again.Payload);
        Assert.Equal(["Isolation:1"], again.Tags);
    }
}

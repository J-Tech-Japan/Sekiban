using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Configuration;
using Orleans.Providers.Streams.AzureQueue;
using Orleans.Streams;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public sealed class AzureQueueAdapterCharacteristicsTests
{
    [Fact]
    public async Task AzureQueueAdapter_IsNotRewindable_ForIssue1185Characterization()
    {
        // #1185 characterization: this records the adapter capability only; it does not claim arbitrary subscriber replay.
        var options = new AzureQueueOptions
        {
            QueueNames = ["g74-1185-characterization"]
        };
        var factory = new AzureQueueAdapterFactory(
            "g74-1185",
            options,
            new SimpleQueueCacheOptions { CacheSize = 16 },
            new NoOpQueueDataAdapter(),
            NullLoggerFactory.Instance);

        factory.Init();
        var adapter = await factory.CreateAdapter();

        Assert.False(adapter.IsRewindable);
    }

    private sealed class NoOpQueueDataAdapter : IQueueDataAdapter<string, IBatchContainer>
    {
        public string ToQueueMessage<T>(
            global::Orleans.Runtime.StreamId streamId,
            IEnumerable<T> events,
            StreamSequenceToken? token,
            Dictionary<string, object>? requestContext) => string.Empty;

        public IBatchContainer FromQueueMessage(string queueMessage, long sequenceId) =>
            throw new NotSupportedException();
    }
}

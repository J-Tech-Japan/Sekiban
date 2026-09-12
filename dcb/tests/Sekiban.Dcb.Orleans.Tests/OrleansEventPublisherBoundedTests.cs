using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
/// Deterministic publisher scheduler proofs. The sender seam is internal and only replaces the transport in these
/// tests; serialization, resolver admission and payload identity still use the production publisher path.
/// </summary>
public sealed class OrleansEventPublisherBoundedTests
{
    [Fact]
    public void Options_ValidateEveryBoundaryAndExposeDefaults()
    {
        var defaults = new OrleansEventPublisherOptions();
        Assert.Equal(5, defaults.MaxPublishAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(100), defaults.BaseRetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), defaults.MaxRetryDelay);
        Assert.Equal(1024, defaults.MaxQueuedItemsPerDestination);
        Assert.Equal(16_384, defaults.MaxQueuedItemsTotal);
        Assert.Equal(20, defaults.TerminalSampleCount);
        Assert.Equal(1024, defaults.MaxDiagnosticDestinations);
        defaults.Validate();

        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxPublishAttempts), () =>
            new OrleansEventPublisherOptions { MaxPublishAttempts = 0 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.BaseRetryDelay), () =>
            new OrleansEventPublisherOptions { BaseRetryDelay = TimeSpan.Zero }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxRetryDelay), () =>
            new OrleansEventPublisherOptions
            {
                BaseRetryDelay = TimeSpan.FromSeconds(2),
                MaxRetryDelay = TimeSpan.FromSeconds(1)
            }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxQueuedItemsPerDestination), () =>
            new OrleansEventPublisherOptions { MaxQueuedItemsPerDestination = 0 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxQueuedItemsTotal), () =>
            new OrleansEventPublisherOptions
            {
                MaxQueuedItemsPerDestination = 2,
                MaxQueuedItemsTotal = 1
            }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.TerminalSampleCount), () =>
            new OrleansEventPublisherOptions { TerminalSampleCount = 0 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxDiagnosticDestinations), () =>
            new OrleansEventPublisherOptions { MaxDiagnosticDestinations = 0 }.Validate());
    }

    [Fact]
    public async Task PermanentFailure_StopsAtFiveAttempts_ReleasesPayload_AndBoundsDiagnostics()
    {
        var sender = new RecordingSender { FailuresBeforeSuccess = 100 };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxPublishAttempts = 5,
                BaseRetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(1),
                TerminalSampleCount = 20
            });

        await publisher.PublishAsync(new[] { CreatePublication("permanent") });
        await WaitUntilAsync(() => sender.Attempts == 5);
        await Task.Delay(20);

        Assert.Equal(5, sender.Attempts);
        Assert.NotNull(sender.LastPayload);
        Assert.Equal(0, sender.LastPayload!.ReferenceCount);
        var snapshot = publisher.GetSnapshot();
        Assert.Equal(5, snapshot.TotalAttemptCount);
        Assert.Equal(1, snapshot.TotalTerminalCount);
        Assert.Equal(1, snapshot.ReasonCounts["transport-attempts-exhausted"]);
        Assert.Single(snapshot.Destinations);
        Assert.Equal(5, snapshot.Destinations[0].AttemptCount);
        Assert.Single(snapshot.Destinations[0].Samples);
    }

    [Fact]
    public async Task Retry_ReusesSerializedEventAndResolverResult_AndPreservesFifo()
    {
        var resolver = new SequenceResolver(
            new OrleansSekibanStream("provider", "ordered", Guid.Parse("00000000-0000-0000-0000-000000000001")));
        var sender = new RecordingSender { FailuresBeforeSuccess = 1 };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxPublishAttempts = 5,
                BaseRetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(1)
            },
            resolver);

        await publisher.PublishAsync(new[] { CreatePublication("identity") });
        await WaitUntilAsync(() => sender.Successes == 1);

        Assert.Equal(1, resolver.Calls);
        Assert.Equal(2, sender.Attempts);
        Assert.Equal(2, sender.ReceivedPayloads.Count);
        Assert.Same(sender.ReceivedPayloads[0], sender.ReceivedPayloads[1]);
        Assert.Same(sender.ReceivedPayloadObjects[0], sender.ReceivedPayloadObjects[1]);
        Assert.Equal("writer-completed", Assert.Single(publisher.GetSnapshot().Destinations).Samples[0].ReasonCode);
    }

    [Fact]
    public async Task DestinationCapacity_RejectsOnlyNewItems_AndOtherDestinationProgresses()
    {
        var resolver = new SequenceResolver(
            new OrleansSekibanStream("provider", "capacity", Guid.Parse("00000000-0000-0000-0000-000000000010")));
        var firstSendEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingSender
        {
            BeforeSend = (_, attempt, _) =>
            {
                if (attempt == 1)
                {
                    firstSendEntered.TrySetResult(true);
                    return releaseFirst.Task;
                }

                return Task.CompletedTask;
            }
        };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxQueuedItemsPerDestination = 1,
                MaxQueuedItemsTotal = 1,
                MaxPublishAttempts = 1
            },
            resolver);

        await publisher.PublishAsync(new[] { CreatePublication("accepted") });
        await firstSendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await publisher.PublishAsync(new[] { CreatePublication("rejected-1") });
        await publisher.PublishAsync(new[] { CreatePublication("rejected-2") });
        releaseFirst.TrySetResult(true);
        await WaitUntilAsync(() => sender.Successes == 1);

        var snapshot = publisher.GetSnapshot();
        Assert.Equal(2, snapshot.ReasonCounts["admission-capacity"]);
        Assert.Equal(1, snapshot.ReasonCounts["writer-completed"]);
        Assert.Equal(1, sender.Successes);
    }

    [Fact]
    public async Task DestinationCapacity_RejectsExactlyFiveAfter1024Accepted_AndOtherDestinationProgresses()
    {
        var resolver = new RouteResolver();
        var firstSendEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingSender
        {
            BeforeSend = (destination, attempt, _) =>
            {
                if (destination.StreamNamespace == "capacity-a" && attempt == 1)
                {
                    firstSendEntered.TrySetResult(true);
                    return releaseFirst.Task;
                }

                return Task.CompletedTask;
            }
        };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxQueuedItemsPerDestination = 1_024,
                MaxQueuedItemsTotal = 16_384,
                MaxPublishAttempts = 1
            },
            resolver);

        var firstA = CreatePublication("A1");
        await publisher.PublishAsync(new[] { firstA });
        await firstSendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var acceptedA = Enumerable.Range(2, 1_023).Select(value => CreatePublication($"A{value}")).ToArray();
        var rejectedA = Enumerable.Range(1_025, 5).Select(value => CreatePublication($"A{value}")).ToArray();
        var bEvents = Enumerable.Range(1, 5).Select(value => CreatePublication($"B{value}")).ToArray();
        await publisher.PublishAsync(acceptedA);
        await publisher.PublishAsync(rejectedA);
        await publisher.PublishAsync(bEvents);

        await WaitUntilAsync(() => sender.Successes == 5);
        Assert.All(bEvents, item => Assert.Contains(item.Event.Id, sender.SuccessfulEventIds));
        Assert.All(rejectedA, item => Assert.DoesNotContain(item.Event.Id, sender.SuccessfulEventIds));
        Assert.Equal(5, publisher.GetSnapshot().ReasonCounts["admission-capacity"]);

        releaseFirst.TrySetResult(true);
        await WaitUntilAsync(() => sender.Successes == 1_029);
        Assert.All(acceptedA.Append(firstA), item => Assert.Contains(item.Event.Id, sender.SuccessfulEventIds));
        var acceptedIds = acceptedA.Append(firstA).Select(item => item.Event.Id).ToHashSet();
        Assert.Equal(
            new[] { firstA }.Concat(acceptedA).Select(item => item.Event.Id),
            sender.SuccessfulEventIds.Where(acceptedIds.Contains));
    }

    [Fact]
    public async Task FanOut_UsesOneSharedPayloadUntilEveryDestinationSettles()
    {
        var sender = new RecordingSender();
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
            new SequenceResolver(
                new OrleansSekibanStream("provider", "fanout-a", Guid.Parse("00000000-0000-0000-0000-00000000000a")),
                new OrleansSekibanStream("provider", "fanout-b", Guid.Parse("00000000-0000-0000-0000-00000000000b"))));

        var publication = CreatePublication("fanout");
        await publisher.PublishAsync(new[] { publication });
        await WaitUntilAsync(() => sender.Successes == 2);
        Assert.Equal(2, sender.ReceivedPayloadObjects.Count);
        Assert.Same(sender.ReceivedPayloadObjects[0], sender.ReceivedPayloadObjects[1]);
        Assert.Equal(0, sender.LastPayload!.ReferenceCount);
    }

    [Fact]
    public async Task Diagnostics_RetainTwentySamples_AndEvictBeyond1024Destinations()
    {
        var resolver = new ManyDestinationResolver();
        var sender = new RecordingSender { FailuresBeforeSuccess = 100_000 };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxPublishAttempts = 1,
                MaxDiagnosticDestinations = 1_024,
                TerminalSampleCount = 20,
                MaxQueuedItemsTotal = 16_384
            },
            resolver);

        var publications = Enumerable.Range(0, 20)
            .Select(value => CreatePublication("sample"))
            .Concat(Enumerable.Range(0, 9_980).Select(value => CreatePublication($"unique-{value}")))
            .ToArray();
        await publisher.PublishAsync(publications);
        await WaitUntilAsync(() => publisher.GetSnapshot().TotalTerminalCount == 10_000);

        var snapshot = publisher.GetSnapshot();
        Assert.Equal(10_000, snapshot.TotalAttemptCount);
        Assert.Equal(10_000, snapshot.TotalTerminalCount);
        Assert.Equal(10_000, snapshot.ReasonCounts["transport-attempts-exhausted"]);
        Assert.Equal(1_024, snapshot.Destinations.Count);
        Assert.True(snapshot.EvictedDestinationCount > 0);
        var sampleDestination = Assert.Single(snapshot.Destinations, value => value.StreamId == ManyDestinationResolver.SampleStreamId);
        Assert.Equal(20, sampleDestination.Samples.Count);
    }

    [Fact]
    public async Task AdmissionFailures_ExposeSerializeAndResolveReasons()
    {
        var throwingEventTypes = DcbDomainTypesExtensions.Simple(builder =>
            builder.EventTypes.RegisterEventType<PublisherEvent>()) with
        {
            EventTypes = new ThrowingEventTypes()
        };
        await using (var serializationPublisher = CreatePublisher(
                         new RecordingSender(),
                         new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
                         domainTypes: throwingEventTypes))
        {
            await serializationPublisher.PublishAsync(new[] { CreatePublication("unregistered") });
            Assert.Equal(1, serializationPublisher.GetSnapshot().ReasonCounts["admission-serialize-failed"]);
        }

        await using var resolvePublisher = CreatePublisher(
            new RecordingSender(),
            new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
            new ThrowingResolver());
        await resolvePublisher.PublishAsync(new[] { CreatePublication("unresolved") });
        Assert.Equal(1, resolvePublisher.GetSnapshot().ReasonCounts["admission-resolve-failed"]);
    }

    [Fact]
    public void PublicSurfaceAndOptionsRegistration_AreAdditiveAndExact()
    {
        var optionsProperties = typeof(OrleansEventPublisherOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "BaseRetryDelay",
                "MaxDiagnosticDestinations",
                "MaxPublishAttempts",
                "MaxQueuedItemsPerDestination",
                "MaxQueuedItemsTotal",
                "MaxRetryDelay",
                "TerminalSampleCount"
            },
            optionsProperties);

        var publicConstructors = typeof(OrleansEventPublisher).GetConstructors();
        Assert.Contains(publicConstructors, constructor => constructor.GetParameters().Length == 4);
        Assert.Contains(publicConstructors, constructor => constructor.GetParameters().Length == 5);
        Assert.Contains(publicConstructors, constructor =>
            constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(OrleansEventPublisherOptions)));

        Assert.Equal(
            new[]
            {
                "Destinations",
                "EvictedDestinationCount",
                "ReasonCounts",
                "TotalAttemptCount",
                "TotalTerminalCount"
            },
            typeof(OrleansPublisherDiagnosticsSnapshot)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray());

        var services = new ServiceCollection();
        services.AddSekibanDcbOrleansEventPublisher();
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(OrleansEventPublisher));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IEventPublisher));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOrleansPublisherDiagnostics));
        Assert.Equal(
            5,
            Assert.IsType<OrleansEventPublisherOptions>(
                Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(OrleansEventPublisherOptions)))
                    .ImplementationInstance).MaxPublishAttempts);
    }

    [Fact]
    public async Task PumpFault_RejectsLaterAdmissionWithoutUnboundedContinuation()
    {
        var sender = new RecordingSender { FailuresBeforeSuccess = 1 };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions { MaxPublishAttempts = 2 },
            delayAsync: (_, _) => throw new InvalidOperationException("test pump fault"));

        await publisher.PublishAsync(new[] { CreatePublication("fault") });
        await WaitUntilAsync(() => publisher.GetSnapshot().ReasonCounts.ContainsKey("pump-faulted"));
        var before = sender.Attempts;
        await publisher.PublishAsync(new[] { CreatePublication("after-fault") });
        await Task.Delay(20);
        Assert.Equal(before, sender.Attempts);
        Assert.True(publisher.GetSnapshot().ReasonCounts["pump-faulted"] >= 1);
    }

    [Fact]
    public async Task Disposal_CancelsBlockedSenderAndLeavesNoLivePayload()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingSender
        {
            BeforeSend = (_, _, cancellationToken) =>
            {
                entered.TrySetResult(true);
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var publisher = CreatePublisher(sender, new OrleansEventPublisherOptions { MaxPublishAttempts = 5 });
        await publisher.PublishAsync(new[] { CreatePublication("dispose") });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await publisher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(sender.LastPayload);
        Assert.Equal(0, sender.LastPayload!.ReferenceCount);
    }

    private static OrleansEventPublisher CreatePublisher(
        RecordingSender sender,
        OrleansEventPublisherOptions options,
        IStreamDestinationResolver? resolver = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        DcbDomainTypes? domainTypes = null)
    {
        return new OrleansEventPublisher(
            clusterClient: null!,
            resolver ?? new SequenceResolver(
                new OrleansSekibanStream("provider", "tests", Guid.Parse("00000000-0000-0000-0000-000000000002"))),
            domainTypes ?? DcbDomainTypesExtensions.Simple(builder => builder.EventTypes.RegisterEventType<PublisherEvent>()),
            NullLogger<OrleansEventPublisher>.Instance,
            options,
            new DefaultServiceIdProvider(),
            sender,
            delayAsync);
    }

    private static (Event Event, IReadOnlyCollection<ITag> Tags) CreatePublication(string value, Guid? eventId = null)
    {
        var payload = new PublisherEvent(value);
        return (
            new Event(
                payload,
                SortableUniqueId.GenerateNew(),
                nameof(PublisherEvent),
                eventId ?? Guid.NewGuid(),
                new EventMetadata("cause", "correlation", "publisher-test"),
                []),
            Array.Empty<ITag>());
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            if (predicate())
                return;
            await Task.Delay(10);
        }

        Assert.True(predicate(), "Timed out waiting for the deterministic publisher observation.");
    }

    private static void AssertInvalid(string property, Action action)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(property, exception.ParamName);
    }

    private sealed record PublisherEvent(string Value) : IEventPayload;

    private sealed class SequenceResolver(params OrleansSekibanStream[] streams) : IStreamDestinationResolver
    {
        private readonly OrleansSekibanStream[] _streams = streams;
        public int Calls { get; private set; }

        public IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags)
        {
            Calls++;
            return _streams;
        }
    }

    private sealed class RouteResolver : IStreamDestinationResolver
    {
        private static readonly OrleansSekibanStream A = new("provider", "capacity-a", Guid.Parse("00000000-0000-0000-0000-00000000000a"));
        private static readonly OrleansSekibanStream B = new("provider", "capacity-b", Guid.Parse("00000000-0000-0000-0000-00000000000b"));

        public IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags) =>
            evt.Payload is PublisherEvent { Value: var value } && value.StartsWith("B", StringComparison.Ordinal)
                ? [B]
                : [A];
    }

    private sealed class ManyDestinationResolver : IStreamDestinationResolver
    {
        public static readonly Guid SampleStreamId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        public IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags)
        {
            var streamId = evt.Payload is PublisherEvent { Value: "sample" }
                ? SampleStreamId
                : evt.Id;
            return [new OrleansSekibanStream("provider", "many", streamId)];
        }
    }

    private sealed class ThrowingResolver : IStreamDestinationResolver
    {
        public IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags) =>
            throw new InvalidOperationException("deterministic resolver failure");
    }

    private sealed class ThrowingEventTypes : IEventTypes
    {
        public string SerializeEventPayload(IEventPayload payload) =>
            throw new InvalidOperationException("deterministic serialization failure");

        public IEventPayload? DeserializeEventPayload(string eventTypeName, string json) => null;

        public Type? GetEventType(string eventTypeName) => null;
    }

    private sealed class RecordingSender : IOrleansStreamSender
    {
        private int _attempts;
        private int _successes;
        public int FailuresBeforeSuccess { get; set; }
        public Func<OrleansPublishDestination, int, CancellationToken, Task>? BeforeSend { get; set; }
        public int Attempts => Volatile.Read(ref _attempts);
        public int Successes => Volatile.Read(ref _successes);
        public SharedPublishPayload? LastPayload { get; private set; }
        public List<SerializableEvent> ReceivedPayloads { get; } = [];
        public List<SharedPublishPayload> ReceivedPayloadObjects { get; } = [];
        public List<Guid> SuccessfulEventIds { get; } = [];

        public async Task SendAsync(
            OrleansPublishDestination destination,
            SharedPublishPayload payload,
            CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            LastPayload = payload;
            lock (ReceivedPayloads)
            {
                ReceivedPayloads.Add(payload.Event);
                ReceivedPayloadObjects.Add(payload);
            }
            if (BeforeSend is not null)
                await BeforeSend(destination, attempt, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt <= FailuresBeforeSuccess)
                throw new InvalidOperationException("deterministic sender failure");
            lock (ReceivedPayloads)
                SuccessfulEventIds.Add(payload.Event.Id);
            Interlocked.Increment(ref _successes);
        }
    }
}

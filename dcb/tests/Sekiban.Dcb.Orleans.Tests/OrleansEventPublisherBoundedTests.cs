using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
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

        new OrleansEventPublisherOptions
        {
            MaxPublishAttempts = 64,
            MaxRetryDelay = TimeSpan.FromMinutes(5),
            MaxQueuedItemsPerDestination = 65_536,
            MaxQueuedItemsTotal = 1_048_576,
            TerminalSampleCount = 1_000,
            MaxDiagnosticDestinations = 65_536
        }.Validate();

        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxPublishAttempts), () =>
            new OrleansEventPublisherOptions { MaxPublishAttempts = 0 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxPublishAttempts), () =>
            new OrleansEventPublisherOptions { MaxPublishAttempts = 65 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.BaseRetryDelay), () =>
            new OrleansEventPublisherOptions { BaseRetryDelay = TimeSpan.Zero }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxRetryDelay), () =>
            new OrleansEventPublisherOptions
            {
                BaseRetryDelay = TimeSpan.FromSeconds(2),
                MaxRetryDelay = TimeSpan.FromSeconds(1)
            }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxRetryDelay), () =>
            new OrleansEventPublisherOptions { MaxRetryDelay = TimeSpan.FromMinutes(5) + TimeSpan.FromMilliseconds(1) }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxQueuedItemsPerDestination), () =>
            new OrleansEventPublisherOptions { MaxQueuedItemsPerDestination = 0 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxQueuedItemsPerDestination), () =>
            new OrleansEventPublisherOptions { MaxQueuedItemsPerDestination = 65_537 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxQueuedItemsTotal), () =>
            new OrleansEventPublisherOptions
            {
                MaxQueuedItemsPerDestination = 2,
                MaxQueuedItemsTotal = 1
            }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxQueuedItemsTotal), () =>
            new OrleansEventPublisherOptions { MaxQueuedItemsTotal = 1_048_577 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.TerminalSampleCount), () =>
            new OrleansEventPublisherOptions { TerminalSampleCount = 0 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.TerminalSampleCount), () =>
            new OrleansEventPublisherOptions { TerminalSampleCount = 1_001 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxDiagnosticDestinations), () =>
            new OrleansEventPublisherOptions { MaxDiagnosticDestinations = 0 }.Validate());
        AssertInvalid(nameof(OrleansEventPublisherOptions.MaxDiagnosticDestinations), () =>
            new OrleansEventPublisherOptions { MaxDiagnosticDestinations = 65_537 }.Validate());
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
        var snapshot = publisher.GetSnapshot();
        Assert.Equal(2, snapshot.TotalAttemptCount);
        Assert.Equal(0, snapshot.TotalTerminalCount);
        Assert.Empty(snapshot.ReasonCounts);
        var destination = Assert.Single(snapshot.Destinations);
        Assert.Equal(2, destination.AttemptCount);
        Assert.Equal(0, destination.TerminalCount);
        Assert.Empty(destination.ReasonCounts);
    }

    [Fact]
    public async Task PlannedPublication_UsesOneCompletePlanPerDestination_AndNeverReSerializes()
    {
        var baseDomain = DcbDomainTypesExtensions.Simple(builder =>
            builder.EventTypes.RegisterEventType<PublisherEvent>());
        var countingTypes = new CountingEventTypes(baseDomain.EventTypes);
        var domain = baseDomain with { EventTypes = countingTypes };
        var capture = new RecordingMeasurementCapture();
        var sender = new RecordingSender
        {
            FailAttempt = (destination, attempt) =>
                destination.StreamNamespace == "planned-a" && attempt == 1,
            ObserveRequestContext = true
        };
        var capturedContext = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["g77-context"] = "captured-once"
        };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxPublishAttempts = 2,
                BaseRetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(1)
            },
            new SequenceResolver(
                new OrleansSekibanStream("provider", "planned-a", Guid.Parse("00000000-0000-0000-0000-0000000000a1")),
                new OrleansSekibanStream("provider", "planned-b", Guid.Parse("00000000-0000-0000-0000-0000000000b1"))),
            domainTypes: domain,
            captureRequestContext: () => capturedContext,
            measurementCaptures: [capture]);

        var publication = CreatePublication("planned");
        RequestContext.Set("g77-context", "caller-sentinel");
        var plan = publisher.CaptureDestinationPlan(publication.Event, publication.Tags, "service-77");

        Assert.NotNull(plan);
        Assert.Equal(1, countingTypes.SerializeCalls);
        var states = Assert.IsAssignableFrom<IReadOnlyList<OrleansDestinationPlanState>>(plan.ProviderState);
        Assert.Equal(2, states.Count);
        Assert.All(states, state =>
        {
            Assert.Same(capturedContext, state.RequestContext);
            Assert.NotNull(state.PreparedTarget);
            Assert.NotNull(state.MeasurementState);
            Assert.Equal("service-77", state.ServiceId);
        });
        Assert.Equal(2, sender.PrepareCalls);
        Assert.Equal(2, capture.CaptureCalls);

        RequestContext.Set("g77-context", "caller-sentinel");
        await ((IExecutorSizeDestinationPublisher)publisher).PublishAsync(
            new[] { publication },
            new Dictionary<Guid, ExecutorSizeDestinationPlan> { [publication.Event.Id] = plan! });
        await WaitUntilAsync(() => sender.Successes == 2 && sender.LastPayload?.ReferenceCount == 0);

        Assert.Equal(1, countingTypes.SerializeCalls);
        Assert.Equal(2, sender.PrepareCalls);
        Assert.Equal(3, sender.Attempts);
        Assert.Single(sender.ReceivedPayloadObjects.Distinct());
        Assert.Equal(2, sender.ReceivedTargets.Distinct().Count());
        Assert.Equal(3, sender.ObservedRequestContexts.Count);
        Assert.All(sender.ObservedRequestContexts, value => Assert.Equal("captured-once", value));
        Assert.Equal("caller-sentinel", RequestContext.Get("g77-context"));
        Assert.Equal(0, sender.LastPayload!.ReferenceCount);

        var incompletePlan = plan! with { PreparedEvent = null };
        var beforeIncomplete = countingTypes.SerializeCalls;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((IExecutorSizeDestinationPublisher)publisher).PublishAsync(
                new[] { publication },
                new Dictionary<Guid, ExecutorSizeDestinationPlan>
                {
                    [publication.Event.Id] = incompletePlan
                }));
        Assert.Equal(beforeIncomplete, countingTypes.SerializeCalls);
        Assert.Equal(3, sender.Attempts);
    }

    [Fact]
    public async Task RetryScheduler_IsolatesBackoffAndPreservesSameDestinationFifo()
    {
        var a1 = CreatePublication("A1");
        var a2 = CreatePublication("A2");
        var b1 = CreatePublication("B1");
        var b2 = CreatePublication("B2");
        var aFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelay = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingSender
        {
            FailFirstEventId = a1.Event.Id,
            BeforeSend = (destination, attempt, _) =>
            {
                if (destination.StreamNamespace == "capacity-a" && attempt == 1)
                    aFirst.TrySetResult(true);
                return Task.CompletedTask;
            }
        };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxPublishAttempts = 2,
                BaseRetryDelay = TimeSpan.FromMilliseconds(1),
                MaxRetryDelay = TimeSpan.FromMilliseconds(1)
            },
            new RouteResolver(),
            delayAsync: (_, cancellationToken) =>
            {
                delayEntered.TrySetResult(true);
                return releaseDelay.Task.WaitAsync(cancellationToken);
            });

        await publisher.PublishAsync(new[] { a1 });
        await aFirst.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await publisher.PublishAsync(new[] { a2, b1, b2 });
        await WaitUntilAsync(() =>
            sender.SuccessfulEventIds.Contains(b1.Event.Id) &&
            sender.SuccessfulEventIds.Contains(b2.Event.Id));
        Assert.DoesNotContain(a2.Event.Id, sender.AttemptRecords.Select(record => record.EventId));

        releaseDelay.TrySetResult(true);
        await WaitUntilAsync(() => sender.Successes == 4);
        var aAttempts = sender.AttemptRecords
            .Where(record => record.Destination == "capacity-a")
            .Select(record => record.EventId)
            .ToArray();
        Assert.Equal(new[] { a1.Event.Id, a1.Event.Id, a2.Event.Id }, aAttempts);
        Assert.Equal(
            new[] { b1.Event.Id, b2.Event.Id },
            sender.AttemptRecords
                .Where(record => record.Destination == "capacity-b")
                .Select(record => record.EventId));
        Assert.True(
            sender.AttemptRecords.FindIndex(record => record.EventId == b1.Event.Id) <
            sender.AttemptRecords.FindIndex(record => record.EventId == a1.Event.Id && record.Attempt > 1));
        Assert.True(
            sender.AttemptRecords.FindIndex(record => record.EventId == b2.Event.Id) <
            sender.AttemptRecords.FindIndex(record => record.EventId == a1.Event.Id && record.Attempt > 1));
    }

    [Fact]
    public async Task GlobalAdmissionCapacityRejectsNewWorkWithoutPerDestinationSaturation()
    {
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingSender
        {
            BeforeSend = (destination, attempt, _) =>
            {
                if (destination.StreamNamespace == "capacity-a" && attempt == 1)
                {
                    firstEntered.TrySetResult(true);
                    return releaseFirst.Task;
                }

                if (destination.StreamNamespace == "capacity-b" && attempt == 1)
                {
                    secondEntered.TrySetResult(true);
                    return releaseSecond.Task;
                }

                return Task.CompletedTask;
            }
        };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxQueuedItemsPerDestination = 2,
                MaxQueuedItemsTotal = 2,
                MaxPublishAttempts = 1
            },
            new RouteResolver());

        var a1 = CreatePublication("A-global-1");
        var b1 = CreatePublication("B-global-1");
        var a2 = CreatePublication("A-global-2");
        await publisher.PublishAsync(new[] { a1 });
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await publisher.PublishAsync(new[] { b1 });
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await publisher.PublishAsync(new[] { a2 });
        Assert.Equal(1, publisher.GetSnapshot().ReasonCounts["admission-capacity"]);
        Assert.DoesNotContain(a2.Event.Id, sender.SuccessfulEventIds);

        releaseFirst.TrySetResult(true);
        releaseSecond.TrySetResult(true);
        await WaitUntilAsync(() => sender.Successes == 2 && publisher.DestinationStateCount == 0);
        Assert.Contains(a1.Event.Id, sender.SuccessfulEventIds);
        Assert.Contains(b1.Event.Id, sender.SuccessfulEventIds);
    }

    [Fact]
    public async Task GlobalCapacityRejectsTenThousandUniqueDestinationsWithoutRetainingEmptyStates()
    {
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingSender
        {
            BeforeSend = (_, attempt, _) =>
            {
                if (attempt == 1)
                {
                    firstEntered.TrySetResult(true);
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
            new ManyDestinationResolver());

        await publisher.PublishAsync(new[] { CreatePublication("state-anchor") });
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = Enumerable.Range(0, 10_000)
            .Select(value => CreatePublication($"unique-rejection-{value}"))
            .ToArray();
        await publisher.PublishAsync(rejected);

        Assert.Equal(10_000, publisher.GetSnapshot().ReasonCounts["admission-capacity"]);
        Assert.Equal(1, publisher.DestinationStateCount);

        releaseFirst.TrySetResult(true);
        await WaitUntilAsync(() => sender.Successes == 1 && publisher.DestinationStateCount == 0);
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
        Assert.DoesNotContain("writer-completed", snapshot.ReasonCounts.Keys);
        Assert.Equal(1, sender.Successes);
    }

    [Fact]
    public async Task CompletedWriterRejectsWithoutSendingAndRecordsWriterCompleted()
    {
        var streamId = Guid.Parse("00000000-0000-0000-0000-000000000021");
        var payloadReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingSender();
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
            new SequenceResolver(new OrleansSekibanStream("provider", "completed-writer", streamId)),
            payloadReleased: () => payloadReleased.TrySetResult(true));

        var destination = new OrleansPublishDestination(
            "default",
            "provider",
            "completed-writer",
            streamId);
        publisher.CompleteDestinationWriterForTesting(destination.DestinationKey);

        await publisher.PublishAsync(new[] { CreatePublication("completed-writer") });

        await WaitUntilAsync(() => publisher.GetSnapshot().ReasonCounts.ContainsKey("writer-completed"));
        await payloadReleased.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var snapshot = publisher.GetSnapshot();
        Assert.Equal(1, snapshot.ReasonCounts["writer-completed"]);
        Assert.Equal(1, snapshot.TotalTerminalCount);
        Assert.DoesNotContain(snapshot.ReasonCounts.Keys, reason => reason == "admission-capacity");
        Assert.Equal(0, sender.Attempts);
        Assert.Equal(0, publisher.DestinationStateCount);
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
        var aEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new RecordingSender
        {
            BeforeSend = (destination, _, cancellationToken) => destination.StreamNamespace switch
            {
                "fanout-a" => WaitAndSignalAsync(aEntered, releaseA, cancellationToken),
                "fanout-b" => WaitAndSignalAsync(bEntered, releaseB, cancellationToken),
                _ => Task.CompletedTask
            }
        };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
            new SequenceResolver(
                new OrleansSekibanStream("provider", "fanout-a", Guid.Parse("00000000-0000-0000-0000-00000000000a")),
                new OrleansSekibanStream("provider", "fanout-b", Guid.Parse("00000000-0000-0000-0000-00000000000b"))));

        var publication = CreatePublication("fanout");
        await publisher.PublishAsync(new[] { publication });
        await Task.WhenAll(
            aEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            bEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(2, sender.ReceivedPayloadObjects.Count);
        Assert.Same(sender.ReceivedPayloadObjects[0], sender.ReceivedPayloadObjects[1]);
        Assert.Equal(2, sender.ReceivedPayloadObjects[0].ReferenceCount);

        releaseA.TrySetResult(true);
        await WaitUntilAsync(() => sender.ReceivedPayloadObjects[0].ReferenceCount == 1);
        Assert.Equal(1, sender.ReceivedPayloadObjects[0].ReferenceCount);
        Assert.Equal(1, sender.Successes);

        releaseB.TrySetResult(true);
        await WaitUntilAsync(() => sender.Successes == 2 && sender.LastPayload?.ReferenceCount == 0);
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
    public async Task PlannedCaptureWithoutDeepCopierFailsClosedBeforeAdmission()
    {
        var domain = DcbDomainTypesExtensions.Simple(builder =>
            builder.EventTypes.RegisterEventType<PublisherEvent>());
        var cluster = DispatchProxy.Create<IClusterClient, ServiceProviderClusterClientProxy>();
        await using var publisher = new OrleansEventPublisher(
            cluster,
            new SequenceResolver(new OrleansSekibanStream(
                "provider",
                "missing-deep-copier",
                Guid.Parse("00000000-0000-0000-0000-000000000031"))),
            domain,
            NullLogger<OrleansEventPublisher>.Instance,
            new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
            new DefaultServiceIdProvider());

        var publication = CreatePublication("missing-deep-copier");
        var plan = publisher.CaptureDestinationPlan(publication.Event, publication.Tags, "default");
        var state = Assert.Single((IReadOnlyList<OrleansDestinationPlanState>)plan!.ProviderState!);
        Assert.Contains("capability is unavailable", state.FailureReason, StringComparison.Ordinal);
        Assert.Null(state.PreparedTarget);

        await ((IExecutorSizeDestinationPublisher)publisher).PublishAsync(
            new[] { publication },
            new Dictionary<Guid, ExecutorSizeDestinationPlan> { [publication.Event.Id] = plan });

        Assert.Equal(1, publisher.GetSnapshot().ReasonCounts["admission-resolve-failed"]);
        Assert.Equal(0, publisher.GetSnapshot().TotalAttemptCount);
    }

    [Fact]
    public async Task DirectContextCaptureFailureIsAnAdmissionFailureAndLaterEventsContinue()
    {
        var first = CreatePublication("context-failure-first");
        var second = CreatePublication("context-failure-second");
        var sender = new RecordingSender();
        var captureAttempts = 0;
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
            captureRequestContext: () =>
            {
                if (Interlocked.Increment(ref captureAttempts) == 1)
                    throw new InvalidOperationException("deterministic context export failure");
                return new Dictionary<string, object>();
            });

        await publisher.PublishAsync(new[] { first, second });
        await WaitUntilAsync(() => sender.Successes == 1);

        var snapshot = publisher.GetSnapshot();
        Assert.Equal(1, snapshot.ReasonCounts["admission-resolve-failed"]);
        Assert.DoesNotContain(first.Event.Id, sender.SuccessfulEventIds);
        Assert.Contains(second.Event.Id, sender.SuccessfulEventIds);
        Assert.Equal(1, snapshot.TotalTerminalCount);
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
        var orderedConstructors = publicConstructors
            .OrderBy(constructor => constructor.GetParameters().Length)
            .ThenBy(constructor => string.Join(",", constructor.GetParameters().Select(parameter => parameter.ParameterType.FullName)))
            .ToArray();
        Assert.Collection(
            orderedConstructors,
            constructor => AssertConstructorShape(
                constructor,
                new[]
                {
                    typeof(IClusterClient),
                    typeof(IStreamDestinationResolver),
                    typeof(DcbDomainTypes),
                    typeof(ILogger<OrleansEventPublisher>)
                },
                new[] { "clusterClient", "resolver", "domainTypes", "logger" },
                new[] { false, false, false, false }),
            constructor => AssertConstructorShape(
                constructor,
                new[]
                {
                    typeof(IClusterClient),
                    typeof(IStreamDestinationResolver),
                    typeof(DcbDomainTypes),
                    typeof(ILogger<OrleansEventPublisher>),
                    typeof(IServiceIdProvider)
                },
                new[] { "clusterClient", "resolver", "domainTypes", "logger", "serviceIdProvider" },
                new[] { false, false, false, false, true }),
            constructor => AssertConstructorShape(
                constructor,
                new[]
                {
                    typeof(IClusterClient),
                    typeof(IStreamDestinationResolver),
                    typeof(DcbDomainTypes),
                    typeof(ILogger<OrleansEventPublisher>),
                    typeof(IServiceIdProvider),
                    typeof(OrleansEventPublisherOptions)
                },
                new[] { "clusterClient", "resolver", "domainTypes", "logger", "serviceIdProvider", "options" },
                new[] { false, false, false, false, false, false }));

        Assert.Equal(
            new Dictionary<string, Type>
            {
                [nameof(OrleansEventPublisherOptions.MaxPublishAttempts)] = typeof(int),
                [nameof(OrleansEventPublisherOptions.BaseRetryDelay)] = typeof(TimeSpan),
                [nameof(OrleansEventPublisherOptions.MaxRetryDelay)] = typeof(TimeSpan),
                [nameof(OrleansEventPublisherOptions.MaxQueuedItemsPerDestination)] = typeof(int),
                [nameof(OrleansEventPublisherOptions.MaxQueuedItemsTotal)] = typeof(int),
                [nameof(OrleansEventPublisherOptions.TerminalSampleCount)] = typeof(int),
                [nameof(OrleansEventPublisherOptions.MaxDiagnosticDestinations)] = typeof(int)
            },
            GetPublicPropertyTypes(typeof(OrleansEventPublisherOptions)));

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

        Assert.Equal(typeof(IReadOnlyList<OrleansPublisherDestinationDiagnostic>),
            typeof(OrleansPublisherDiagnosticsSnapshot).GetProperty(nameof(OrleansPublisherDiagnosticsSnapshot.Destinations))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyDictionary<string, long>),
            typeof(OrleansPublisherDiagnosticsSnapshot).GetProperty(nameof(OrleansPublisherDiagnosticsSnapshot.ReasonCounts))!.PropertyType);
        Assert.Equal(
            new Dictionary<string, Type>
            {
                [nameof(OrleansPublisherDiagnosticsSnapshot.Destinations)] =
                    typeof(IReadOnlyList<OrleansPublisherDestinationDiagnostic>),
                [nameof(OrleansPublisherDiagnosticsSnapshot.EvictedDestinationCount)] = typeof(long),
                [nameof(OrleansPublisherDiagnosticsSnapshot.ReasonCounts)] = typeof(IReadOnlyDictionary<string, long>),
                [nameof(OrleansPublisherDiagnosticsSnapshot.TotalAttemptCount)] = typeof(long),
                [nameof(OrleansPublisherDiagnosticsSnapshot.TotalTerminalCount)] = typeof(long)
            },
            GetPublicPropertyTypes(typeof(OrleansPublisherDiagnosticsSnapshot)));
        Assert.Equal(
            new[]
            {
                "AttemptCount", "DestinationKey", "Namespace", "Provider", "ReasonCounts", "Samples", "ServiceId",
                "StreamId", "TerminalCount"
            },
            typeof(OrleansPublisherDestinationDiagnostic).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name).OrderBy(name => name).ToArray());
        Assert.Equal(
            new[] { "Attempts", "EventId", "OccurredAtUtc", "ReasonCode" },
            typeof(OrleansPublisherTerminalSample).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name).OrderBy(name => name).ToArray());

        Assert.Equal(
            new Dictionary<string, Type>
            {
                [nameof(OrleansPublisherDestinationDiagnostic.AttemptCount)] = typeof(long),
                [nameof(OrleansPublisherDestinationDiagnostic.DestinationKey)] = typeof(string),
                [nameof(OrleansPublisherDestinationDiagnostic.Namespace)] = typeof(string),
                [nameof(OrleansPublisherDestinationDiagnostic.Provider)] = typeof(string),
                [nameof(OrleansPublisherDestinationDiagnostic.ReasonCounts)] = typeof(IReadOnlyDictionary<string, long>),
                [nameof(OrleansPublisherDestinationDiagnostic.Samples)] = typeof(IReadOnlyList<OrleansPublisherTerminalSample>),
                [nameof(OrleansPublisherDestinationDiagnostic.ServiceId)] = typeof(string),
                [nameof(OrleansPublisherDestinationDiagnostic.StreamId)] = typeof(Guid),
                [nameof(OrleansPublisherDestinationDiagnostic.TerminalCount)] = typeof(long)
            },
            GetPublicPropertyTypes(typeof(OrleansPublisherDestinationDiagnostic)));
        Assert.Equal(
            new Dictionary<string, Type>
            {
                [nameof(OrleansPublisherTerminalSample.Attempts)] = typeof(int),
                [nameof(OrleansPublisherTerminalSample.EventId)] = typeof(Guid),
                [nameof(OrleansPublisherTerminalSample.OccurredAtUtc)] = typeof(DateTimeOffset),
                [nameof(OrleansPublisherTerminalSample.ReasonCode)] = typeof(string)
            },
            GetPublicPropertyTypes(typeof(OrleansPublisherTerminalSample)));
        Assert.Equal(
            new[] { $"GetSnapshot():{typeof(OrleansPublisherDiagnosticsSnapshot).FullName}" },
            GetDeclaredPublicMethodSignatures(typeof(IOrleansPublisherDiagnostics)));
        Assert.Empty(typeof(IOrleansPublisherDiagnostics).GetProperties(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(GetDeclaredPublicMethodSignatures(typeof(OrleansPublisherDiagnosticsSnapshot)));
        Assert.Empty(GetDeclaredPublicMethodSignatures(typeof(OrleansPublisherDestinationDiagnostic)));
        Assert.Empty(GetDeclaredPublicMethodSignatures(typeof(OrleansPublisherTerminalSample)));

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

    private static Dictionary<string, Type> GetPublicPropertyTypes(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

    private static void AssertConstructorShape(
        ConstructorInfo constructor,
        IReadOnlyList<Type> expectedTypes,
        IReadOnlyList<string> expectedNames,
        IReadOnlyList<bool> expectedOptional)
    {
        var parameters = constructor.GetParameters();
        Assert.Equal(expectedTypes, parameters.Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal(expectedNames, parameters.Select(parameter => parameter.Name!).ToArray());
        Assert.Equal(expectedOptional, parameters.Select(parameter => parameter.IsOptional).ToArray());
        Assert.All(parameters.Where(parameter => parameter.IsOptional), parameter => Assert.Null(parameter.DefaultValue));
    }

    private static string[] GetDeclaredPublicMethodSignatures(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method =>
                $"{method.Name}({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName))}):{method.ReturnType.FullName}")
            .OrderBy(signature => signature)
            .ToArray();

    [Fact]
    public async Task LegacyOptionsFreeRegistrationResolvesWithDefaultFiveAttemptConfiguration()
    {
        var domain = DcbDomainTypesExtensions.Simple(builder =>
            builder.EventTypes.RegisterEventType<PublisherEvent>());
        var stream = DispatchProxy.Create<IAsyncStream<SerializableEvent>, FailingStreamProxy>();
        var streamControl = (FailingStreamProxy)(object)stream;
        var streamProvider = DispatchProxy.Create<IStreamProvider, FailingStreamProviderProxy>();
        var streamProviderControl = (FailingStreamProviderProxy)(object)streamProvider;
        streamProviderControl.Stream = stream;
        var cluster = DispatchProxy.Create<IClusterClient, FailingClusterClientProxy>();
        var clusterControl = (FailingClusterClientProxy)(object)cluster;
        clusterControl.StreamProvider = streamProvider;
        using var clusterServiceProvider = new ServiceCollection()
            .AddKeyedSingleton<IStreamProvider>("provider", streamProvider)
            .BuildServiceProvider();
        clusterControl.ServiceProviderOverride = clusterServiceProvider;
        var logger = new RecordingLogger();
        var services = new ServiceCollection();
        services.AddSingleton<IClusterClient>(cluster);
        services.AddSingleton<IStreamDestinationResolver>(
            new SequenceResolver(new OrleansSekibanStream("provider", "legacy", Guid.NewGuid())));
        services.AddSingleton(domain);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<OrleansEventPublisher>>(
            logger);
        services.AddSingleton<IEventPublisher, OrleansEventPublisher>();

        await using var serviceProvider = services.BuildServiceProvider();
        var publisher = serviceProvider.GetRequiredService<IEventPublisher>();
        Assert.IsType<OrleansEventPublisher>(publisher);
        Assert.Equal(5, new OrleansEventPublisherOptions().MaxPublishAttempts);

        await publisher.PublishAsync(new[] { CreatePublication("legacy-options-free") });
        var diagnostics = ((IOrleansPublisherDiagnostics)publisher).GetSnapshot();
        await WaitUntilAsync(
            () =>
                streamControl.Attempts == 5 &&
                ((IOrleansPublisherDiagnostics)publisher).GetSnapshot().TotalTerminalCount == 1,
            () =>
                $"stream attempts={streamControl.Attempts}, diagnostics attempts={diagnostics.TotalAttemptCount}, " +
                $"reasons={string.Join(",", diagnostics.ReasonCounts.Select(pair => $"{pair.Key}:{pair.Value}"))}, " +
                $"clusterCalls={string.Join("|", clusterControl.Calls)}, " +
                $"providerCalls={string.Join("|", streamProviderControl.Calls)}, " +
                $"logs={string.Join("|", logger.Messages)}");
        diagnostics = ((IOrleansPublisherDiagnostics)publisher).GetSnapshot();
        Assert.Equal(5, diagnostics.TotalAttemptCount);
        Assert.Equal(1, diagnostics.ReasonCounts["transport-attempts-exhausted"]);
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

    [Fact]
    public async Task Disposal_RaceAfterAdmissionStartsOwnedWorkerWithNoneAndReleasesQueuedPayload()
    {
        var workerGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var payloadReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedStartToken = default(CancellationToken);
        var sender = new RecordingSender();
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
            startWorkerAsync: (work, cancellationToken) =>
            {
                observedStartToken = cancellationToken;
                return Task.Run(
                    async () =>
                    {
                        await workerGate.Task;
                        if (!cancellationToken.IsCancellationRequested)
                            await work();
                    },
                    CancellationToken.None);
            },
            payloadReleased: () => payloadReleased.TrySetResult(true));

        await publisher.PublishAsync(new[] { CreatePublication("start-dispose-race") });
        var disposeTask = publisher.DisposeAsync().AsTask();
        await Task.Delay(20);
        Assert.False(disposeTask.IsCompleted);
        Assert.False(observedStartToken.IsCancellationRequested);

        workerGate.TrySetResult(true);
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));
        await payloadReleased.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(sender.LastPayload);
    }

    [Fact]
    public async Task Disposal_AwaitsTheRealNonCancellableTransportTask_AndObservesFaults()
    {
        await AssertTransportSettlesBeforeDisposeAsync(completeSuccessfully: true);
        await AssertTransportSettlesBeforeDisposeAsync(completeSuccessfully: false);
    }

    private static async Task AssertTransportSettlesBeforeDisposeAsync(bool completeSuccessfully)
    {
        var stream = DispatchProxy.Create<IAsyncStream<SerializableEvent>, ControlledStreamProxy>();
        var streamControl = (ControlledStreamProxy)(object)stream;
        var target = OrleansEventPublisher.CreatePreparedStreamTargetForTesting(stream);
        var payloadReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = new FixedTargetSender(target);
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions { MaxPublishAttempts = 1 },
            payloadReleased: () => payloadReleased.TrySetResult(true));

        await publisher.PublishAsync(new[] { CreatePublication(completeSuccessfully ? "transport-complete" : "transport-fault") });
        await streamControl.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposeTask = publisher.DisposeAsync().AsTask();
        Assert.False(disposeTask.IsCompleted);

        if (completeSuccessfully)
            streamControl.SendCompletion.TrySetResult(true);
        else
            streamControl.SendCompletion.TrySetException(new InvalidOperationException("deterministic transport fault"));

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));
        await payloadReleased.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Disposal_DrainsInFlightQueuedAndRetryDelayWorkWithoutDetachedFaults()
    {
        var delayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCount = 0;
        var first = CreatePublication("dispose-delay-1");
        var second = CreatePublication("dispose-delay-2");
        var sender = new RecordingSender { FailFirstEventId = first.Event.Id };
        await using var publisher = CreatePublisher(
            sender,
            new OrleansEventPublisherOptions
            {
                MaxPublishAttempts = 3,
                MaxQueuedItemsPerDestination = 10,
                MaxQueuedItemsTotal = 10,
                BaseRetryDelay = TimeSpan.FromSeconds(1),
                MaxRetryDelay = TimeSpan.FromSeconds(1)
            },
            delayAsync: (_, cancellationToken) =>
            {
                delayEntered.TrySetResult(true);
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            payloadReleased: () => Interlocked.Increment(ref releaseCount));

        await publisher.PublishAsync(new[] { first });
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await publisher.PublishAsync(new[] { second });
        await publisher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, releaseCount);
        Assert.Equal(0, publisher.GetSnapshot().ReasonCounts.GetValueOrDefault("pump-faulted"));
    }

    private static OrleansEventPublisher CreatePublisher(
        IOrleansStreamSender sender,
        OrleansEventPublisherOptions options,
        IStreamDestinationResolver? resolver = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        DcbDomainTypes? domainTypes = null,
        Func<Func<Task>, CancellationToken, Task>? startWorkerAsync = null,
        Func<Dictionary<string, object>?>? captureRequestContext = null,
        IReadOnlyList<IOrleansDestinationMeasurementCapture>? measurementCaptures = null,
        Action? payloadReleased = null)
    {
        return new OrleansEventPublisher(
            clusterClient: null!,
            resolver ?? new SequenceResolver(
                new OrleansSekibanStream("provider", "tests", Guid.Parse("00000000-0000-0000-0000-000000000002"))),
            domainTypes ?? DcbDomainTypesExtensions.Simple(builder => builder.EventTypes.RegisterEventType<PublisherEvent>()),
            NullLogger<OrleansEventPublisher>.Instance,
            options,
            new DefaultServiceIdProvider(),
            new OrleansEventPublisherTestHooks
            {
                Sender = sender,
                DelayAsync = delayAsync,
                StartWorkerAsync = startWorkerAsync,
                CaptureRequestContext = captureRequestContext,
                MeasurementCaptures = measurementCaptures,
                PayloadReleased = payloadReleased
            });
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

    private static async Task WaitUntilAsync(Func<bool> predicate, Func<string>? diagnostic = null)
    {
        for (var i = 0; i < 200; i++)
        {
            if (predicate())
                return;
            await Task.Delay(10);
        }

        Assert.True(
            predicate(),
            diagnostic?.Invoke() ?? "Timed out waiting for the deterministic publisher observation.");
    }

    private static async Task WaitAndSignalAsync(
        TaskCompletionSource<bool> entered,
        TaskCompletionSource<bool> release,
        CancellationToken cancellationToken)
    {
        entered.TrySetResult(true);
        await release.Task.WaitAsync(cancellationToken);
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

    private sealed class CountingEventTypes(IEventTypes inner) : IEventTypes
    {
        public int SerializeCalls { get; private set; }

        public string SerializeEventPayload(IEventPayload payload)
        {
            SerializeCalls++;
            return inner.SerializeEventPayload(payload);
        }

        public IEventPayload? DeserializeEventPayload(string eventTypeName, string json) =>
            inner.DeserializeEventPayload(eventTypeName, json);

        public Type? GetEventType(string eventTypeName) => inner.GetEventType(eventTypeName);
    }

    private sealed class RecordingMeasurementCapture : IOrleansDestinationMeasurementCapture
    {
        public int CaptureCalls { get; private set; }
        public List<object> States { get; } = [];

        public bool Matches(string providerName) => providerName == "provider";

        public object? Capture(
            string providerName,
            string streamNamespace,
            Guid streamId,
            SerializableEvent serializedEvent,
            IReadOnlyDictionary<string, object>? requestContext,
            out string? failureReason)
        {
            failureReason = null;
            CaptureCalls++;
            var state = new object();
            States.Add(state);
            return state;
        }
    }

    private class ServiceProviderClusterClientProxy : DispatchProxy
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "get_ServiceProvider"
                ? _serviceProvider
                : throw new InvalidOperationException($"Unexpected Orleans client call: {targetMethod?.Name}");
    }

    private sealed class RecordingLogger : ILogger<OrleansEventPublisher>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : $" [{exception.GetType().Name}: {exception.Message}]"));
        }
    }

    private class FailingClusterClientProxy : DispatchProxy
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().BuildServiceProvider();
        public IStreamProvider? StreamProvider { get; set; }
        public IServiceProvider? ServiceProviderOverride { get; set; }
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod?.Name ?? "<null>");
            return targetMethod?.Name switch
            {
                "get_ServiceProvider" => ServiceProviderOverride ?? _serviceProvider,
                "GetStreamProvider" => StreamProvider,
                _ => throw new InvalidOperationException($"Unexpected Orleans client call: {targetMethod?.Name}")
            };
        }
    }

    private class FailingStreamProviderProxy : DispatchProxy
    {
        public IAsyncStream<SerializableEvent>? Stream { get; set; }
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod?.Name ?? "<null>");
            return targetMethod?.Name == "GetStream"
                ? Stream
                : throw new InvalidOperationException($"Unexpected stream provider call: {targetMethod?.Name}");
        }
    }

    private class FailingStreamProxy : DispatchProxy
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IAsyncStream<SerializableEvent>.OnNextAsync))
            {
                Interlocked.Increment(ref _attempts);
                return Task.FromException(new InvalidOperationException("deterministic legacy transport failure"));
            }

            throw new InvalidOperationException($"Unexpected async stream call: {targetMethod?.Name}");
        }
    }

    private class ControlledStreamProxy : DispatchProxy
    {
        public TaskCompletionSource<bool> SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> SendCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IAsyncStream<SerializableEvent>.OnNextAsync))
            {
                SendStarted.TrySetResult(true);
                return SendCompletion.Task;
            }

            throw new InvalidOperationException($"Unexpected async stream call: {targetMethod?.Name}");
        }
    }

    private sealed class FixedTargetSender(IOrleansPreparedStreamTarget target) : IOrleansStreamSender
    {
        public IOrleansPreparedStreamTarget PrepareDestination(OrleansPublishDestination destination) => target;
    }

    private sealed class RecordingSender : IOrleansStreamSender
    {
        private readonly object _gate = new();
        private readonly Dictionary<RecordingTarget, int> _attemptsByTarget = [];
        private int _attempts;
        private int _successes;
        public int FailuresBeforeSuccess { get; set; }
        public Guid? FailFirstEventId { get; set; }
        public Func<OrleansPublishDestination, int, bool>? FailAttempt { get; set; }
        public Func<OrleansPublishDestination, int, CancellationToken, Task>? BeforeSend { get; set; }
        public bool ObserveRequestContext { get; set; }
        public int PrepareCalls { get; private set; }
        public int Attempts => Volatile.Read(ref _attempts);
        public int Successes => Volatile.Read(ref _successes);
        public SharedPublishPayload? LastPayload { get; private set; }
        public List<SerializableEvent> ReceivedPayloads { get; } = [];
        public List<SharedPublishPayload> ReceivedPayloadObjects { get; } = [];
        public List<IOrleansPreparedStreamTarget> ReceivedTargets { get; } = [];
        public List<Guid> SuccessfulEventIds { get; } = [];
        public List<AttemptRecord> AttemptRecords { get; } = [];
        public List<string?> ObservedRequestContexts { get; } = [];

        public IOrleansPreparedStreamTarget PrepareDestination(OrleansPublishDestination destination)
        {
            var target = new RecordingTarget(this, destination);
            lock (_gate)
            {
                PrepareCalls++;
                _attemptsByTarget[target] = 0;
            }

            return target;
        }

        private async Task SendAsync(
            RecordingTarget target,
            SharedPublishPayload payload,
            CancellationToken cancellationToken)
        {
            int attempt;
            lock (_gate)
            {
                attempt = ++_attemptsByTarget[target];
                _attempts++;
            }

            LastPayload = payload;
            lock (ReceivedPayloads)
            {
                ReceivedPayloads.Add(payload.Event);
                ReceivedPayloadObjects.Add(payload);
                ReceivedTargets.Add(target);
                AttemptRecords.Add(new AttemptRecord(
                    payload.Event.Id,
                    target.Destination.StreamNamespace,
                    attempt,
                    target));
            }

            if (ObserveRequestContext)
            {
                var prior = RequestContext.Get("g77-context");
                var captured = payload.RequestContext is not null &&
                               payload.RequestContext.TryGetValue("g77-context", out var value)
                    ? value as string
                    : null;
                lock (ReceivedPayloads)
                    ObservedRequestContexts.Add(captured);
                RequestContext.Set("g77-context", captured);
                try
                {
                    await SendCoreAsync(target, payload, attempt, cancellationToken);
                }
                finally
                {
                    if (prior is null)
                        RequestContext.Clear();
                    else
                        RequestContext.Set("g77-context", prior);
                }
                return;
            }

            await SendCoreAsync(target, payload, attempt, cancellationToken);
        }

        private async Task SendCoreAsync(
            RecordingTarget target,
            SharedPublishPayload payload,
            int attempt,
            CancellationToken cancellationToken)
        {
            if (BeforeSend is not null)
                await BeforeSend(target.Destination, attempt, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if ((FailAttempt?.Invoke(target.Destination, attempt) ?? false) ||
                (FailFirstEventId == payload.Event.Id && attempt == 1) ||
                attempt <= FailuresBeforeSuccess)
                throw new InvalidOperationException("deterministic sender failure");
            lock (ReceivedPayloads)
                SuccessfulEventIds.Add(payload.Event.Id);
            Interlocked.Increment(ref _successes);
        }

        private sealed class RecordingTarget(
            RecordingSender owner,
            OrleansPublishDestination destination) : IOrleansPreparedStreamTarget
        {
            public OrleansPublishDestination Destination { get; } = destination;

            public Task SendAsync(SharedPublishPayload payload, CancellationToken cancellationToken) =>
                owner.SendAsync(this, payload, cancellationToken);
        }

        public sealed record AttemptRecord(
            Guid EventId,
            string Destination,
            int Attempt,
            IOrleansPreparedStreamTarget Target);
    }
}

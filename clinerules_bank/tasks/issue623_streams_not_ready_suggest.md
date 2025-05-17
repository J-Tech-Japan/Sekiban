Below is a concise playbook that works as-is on Orleans 9.
The key change from older guidance is that the grain-lifecycle constants have been trimmed in v9 (only First / SetupState / Activate / Last remain), so the familiar AfterRuntimeGrainServices stage no longer exists  ￼ ￼.
That means we must either ① move the subscription logic out of OnActivateAsync entirely or ② guard it with a short, bounded retry.  Both approaches are shown.

⸻

1  Why the race still happens in v9
	•	GetStreamProvider(name) throws KeyNotFoundException until the provider finishes registering itself in the silo’s runtime component registry  ￼ ￼.
	•	Provider registration completes asynchronously during silo start-up, so a grain that activates early can hit the gap.
	•	In Orleans 9 the Activate stage of GrainLifecycleStage is exactly when OnActivateAsync runs  ￼—there is no later “safe” grain stage built-in.

⸻

2  Option A – Use the grain lifecycle properly

Instead of subscribing inside OnActivateAsync, have the grain participate in its own lifecycle and run the subscription slightly after Activate:

public class MultiProjectorGrain : Grain,
                                   ILifecycleParticipant<IGrainLifecycle>
{
    private const string ProviderName = "EventStreamProvider";
    private IAsyncStream<IEvent>? _stream;
    private StreamSubscriptionHandle<IEvent>? _handle;

    public void Participate(IGrainLifecycle lifecycle)
    {
        // Run right after Activate (2000) but before Last; pick any slot > Activate.
        const int Stage = GrainLifecycleStage.Activate + 100;
        lifecycle.Subscribe<MultiProjectorGrain>(
            nameof(MultiProjectorGrain),
            Stage,
            InitStreamsAsync,
            CloseStreamsAsync);
    }

    private async Task InitStreamsAsync(CancellationToken ct)
    {
        var provider = GetStreamProvider(ProviderName);      // will succeed now
        _stream = provider.GetStream<IEvent>(
             StreamId.Create("AllEvents", Guid.Empty));

        // resume or new-subscribe…
        var handles = await _stream.GetAllSubscriptionHandles();
        _handle = handles.Count > 0
                ? await handles[0].ResumeAsync(OnNextAsync)
                : await _stream.SubscribeAsync(OnNextAsync);
    }

    private Task OnNextAsync(IEvent e, StreamSequenceToken? t) =>
        HandleEventAsync(e, t);

    private Task CloseStreamsAsync(CancellationToken _) =>
        _handle?.UnsubscribeAsync() ?? Task.CompletedTask;
}

	•	Lifecycle APIs are unchanged in Orleans 9  ￼ ￼, so this works without third-party packages.
	•	By scheduling the subscription at Activate+100 you avoid the race while still keeping startup fast.

⸻

3  Option B – Stay in OnActivateAsync, add bounded retry

If you cannot refactor the grain right now, wrap the call in a small retry helper:

public override async Task OnActivateAsync(CancellationToken token)
{
    const int maxAttempts = 6;
    const int delayMs     = 250;

    for (var attempt = 0; attempt < maxAttempts; attempt++)
    {
        try
        {
            var provider = GetStreamProvider("EventStreamProvider");
            var stream   = provider.GetStream<IEvent>(
                               StreamId.Create("AllEvents", Guid.Empty));

            await EnsureSubscribedAsync(stream);
            return;                // success
        }
        catch (KeyNotFoundException) when (attempt < maxAttempts - 1)
        {
            await Task.Delay(delayMs * (attempt + 1), token);   // back-off
        }
    }

    throw new InvalidOperationException(
        "Stream provider unavailable after retry.");
}

	•	Keeps the logic local, no lifecycle code needed.
	•	Because the provider normally registers within a few hundred ms, a capped back-off of ~2 s total is sufficient on developer hardware.

⸻

4  Option C – Guarantee provider readiness at silo start

If you control silo startup you can force the stream provider to initialize in an earlier silo-lifecycle stage, e.g.:

builder.AddMemoryStreams("EventStreamProvider", opts =>
{
    opts.ConfigureLifecycle = stage =>
        stage.AddStage(GrainLifecycleStage.SetupState - 100);   // earlier
});

This makes every provider available before the first grain activates, eliminating the race cluster-wide.  Use it only if you run a single provider; competing providers may need different timings  ￼ ￼.

⸻

5  Checklist for production

Check	Why	Docs
Provider added on both silo & client	otherwise KeyNotFoundException	￼
PubSubStore storage registered	durable subscriptions	￼
Implicit subscriptions use attribute	avoids manual subscribe logic	￼
Retry or lifecycle-stage move	eliminates race	sections 2 & 3


⸻

Take-away

In Orleans 9 the safest, idiomatic fix is to move stream subscription to a custom grain-lifecycle callback executed after the Activate stage.
If refactoring is not feasible, wrap GetStreamProvider in one or two quick retries. Both approaches remove the intermittent activation failures you observed.
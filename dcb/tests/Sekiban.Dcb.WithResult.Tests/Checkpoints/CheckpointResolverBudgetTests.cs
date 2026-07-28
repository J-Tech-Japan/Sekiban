using System.Diagnostics;
using ResultBoxes;
using Sekiban.Dcb.Storage.Checkpoints;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     SEK-G20 TRUE time-bounded independent verification budget. The resolver runs its re-reads on a caller-INDEPENDENT
///     CancellationTokenSource (never the caller's possibly-cancelled token, never CancellationToken.None). A hung,
///     token-observing authority is cancelled PROMPTLY at the budget and the resolver returns typed retryable
///     VerificationUnavailable InDoubt — preserving the EXACT original cause — while the provider read task exits
///     cooperatively and NO further read happens after the resolver returns. Proven for BOTH shared resolver paths.
/// </summary>
public class CheckpointResolverBudgetTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(250);

    private sealed class HungReread
    {
        public int Started;
        public int CooperativeExits;
        public readonly TaskCompletionSource FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // A read that OBSERVES the token and blocks until it is cancelled, then exits cooperatively (throws OCE).
        public async Task<ResultBox<CheckpointSlot>> ReadAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Started);
            FirstStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref CooperativeExits);
                throw;
            }
            return ResultBox.FromValue(CheckpointSlot.Absent);
        }
    }

    [Fact]
    public async Task ResolveActiveWrite_HungTokenObservingReread_PromptlyVerificationUnavailable_ExactCause_NoBackgroundWork()
    {
        var hung = new HungReread();
        var cause = new OperationCanceledException("original caller cancellation");
        var sw = Stopwatch.StartNew();
        var outcome = await CheckpointInDoubtResolver.ResolveActiveWriteAsync(
            hung.ReadAsync, resultingGeneration: 1, lastSortableUniqueId: "p", eventsProcessed: 1, cause: cause,
            verificationBudget: Budget);
        sw.Stop();

        Assert.Equal(CheckpointCasStatus.InDoubt, outcome.Status);
        Assert.Equal(CheckpointInDoubtReason.VerificationUnavailable, outcome.InDoubtReason);   // no read confirmed
        Assert.True(outcome.IsRetryable);
        Assert.Same(cause, outcome.Cause);                                                      // EXACT original cause + CT
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"resolver did not return promptly at the budget: {sw.Elapsed}");
        Assert.Equal(1, hung.Started);                                                          // exactly one attempt started
        Assert.Equal(1, hung.CooperativeExits);                                                 // the read exited cooperatively

        // ZERO background read after the resolver returned: the count is stable (the budget CTS was disposed).
        var startedAtReturn = hung.Started;
        await Task.Delay(Budget + Budget);
        Assert.Equal(startedAtReturn, hung.Started);
    }

    [Fact]
    public async Task ResolveTombstoneWrite_HungTokenObservingReread_PromptlyVerificationUnavailable_ExactCause_NoBackgroundWork()
    {
        var hung = new HungReread();
        var cause = new TimeoutException("original transport timeout");
        var sw = Stopwatch.StartNew();
        var outcome = await CheckpointInDoubtResolver.ResolveTombstoneWriteAsync(
            hung.ReadAsync, resultingGeneration: 2, resultingRevision: 3, cause: cause, verificationBudget: Budget);
        sw.Stop();

        Assert.Equal(CheckpointCasStatus.InDoubt, outcome.Status);
        Assert.Equal(CheckpointInDoubtReason.VerificationUnavailable, outcome.InDoubtReason);
        Assert.True(outcome.IsRetryable);
        Assert.Same(cause, outcome.Cause);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"resolver did not return promptly at the budget: {sw.Elapsed}");
        Assert.Equal(1, hung.Started);
        Assert.Equal(1, hung.CooperativeExits);

        var startedAtReturn = hung.Started;
        await Task.Delay(Budget + Budget);
        Assert.Equal(startedAtReturn, hung.Started);
    }

    [Fact]
    public async Task Resolver_UsesAnIndependentToken_NotTheCallersAlreadyCancelledToken()
    {
        // The caller's token is ALREADY cancelled (the lost-response cancellation). The resolver must NOT thread it into
        // the re-read — it runs on its own budget — so a read that would succeed is not pre-empted by the caller's token.
        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        var reads = 0;
        Task<ResultBox<CheckpointSlot>> Reread(CancellationToken ct)
        {
            Interlocked.Increment(ref reads);
            Assert.False(ct.IsCancellationRequested, "the resolver passed a cancelled token — it must use its own budget");
            // Confirms our own committed write on the first read.
            return Task.FromResult(ResultBox.FromValue(new CheckpointSlot(true, 1, "2", CheckpointLifecycle.Active,
                new Sekiban.Dcb.MultiProjections.MultiProjectionStateRecord(
                    "p", "1.0.0", "T", "pos", 1, false, null, null, 1, 1, "w",
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "s", "h"))));
        }
        var outcome = await CheckpointInDoubtResolver.ResolveActiveWriteAsync(
            Reread, resultingGeneration: 1, lastSortableUniqueId: "pos", eventsProcessed: 1,
            cause: new OperationCanceledException(alreadyCancelled.Token), verificationBudget: Budget);
        Assert.Equal(CheckpointCasStatus.Committed, outcome.Status);   // the independent budget let the read confirm
        Assert.Equal(1, reads);
    }
}

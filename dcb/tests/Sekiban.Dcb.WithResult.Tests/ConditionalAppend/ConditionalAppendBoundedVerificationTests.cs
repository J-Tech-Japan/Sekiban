using System.Diagnostics;
using Dcb.Domain;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 bounded, caller-independent post-ambiguity verification, exercised directly against the shared
///     <see cref="ConditionalAppendExecution.RunAsync" />. When a write is ambiguous (cancellation/timeout, or a
///     post-commit response loss) the orchestrator verifies authoritatively under an independent budget: a readback that
///     completes within the budget resolves to AlreadyCommitted on the same call; a readback that hangs past the budget
///     yields a typed <see cref="ConditionalAppendInDoubtReason.AmbiguousAfterWrite" /> PROMPTLY (not unbounded), and the
///     caller's own cancellation never cancels the verification while its exception/token are preserved as the cause.
/// </summary>
public class ConditionalAppendBoundedVerificationTests
{
    private readonly DcbDomainTypes _domain = ConditionalAppendScenarios.RegisterMarker(DomainType.GetDomainTypes());

    private ConditionalAppendRequest Request(string key) =>
        new(key, ConditionalAppendScenarios.Marker(_domain, "v"));

    private SerializableEvent Winner(string key) =>
        ConditionalAppendScenarios.Marker(_domain, "v") with
        {
            Id = ConditionalAppendIdentity.DeriveEventId("svc", OperationFingerprint.NormalizeKey(key))
        };

    // The INTERNAL provider→orchestrator post-commit marker, constructed by reflection (it is internal to Core, and only
    // a provider raises it in production). Wraps the original transport/cancellation cause.
    private static Exception PostCommitMarker(Exception originalCause)
    {
        var type = typeof(ConditionalAppendExecution).Assembly
            .GetType("Sekiban.Dcb.Storage.PostCommitResponseLostException")!;
        return (Exception)Activator.CreateInstance(type, originalCause)!;
    }

    [Fact]
    public async Task PostCommitMarker_ReadbackWithinBudget_ResolvesSameCall_ToAlreadyCommitted()
    {
        var winner = Winner("bv-ok");

        var result = await ConditionalAppendExecution.RunAsync(
            Request("bv-ok"), "svc", _domain.EventTypes, "TestProvider",
            (_, _, _) => throw PostCommitMarker(new InvalidOperationException("response lost after commit")),
            (_, _) => Task.FromResult<SerializableEvent?>(winner),
            ensureCommittedAsync: null,
            cancellationToken: CancellationToken.None,
            verificationBudget: TimeSpan.FromSeconds(5));

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, result.GetValue().Status);
        Assert.Equal(winner.Id, result.GetValue().WinnerEventId);
    }

    [Fact]
    public async Task ReadbackExceedingBudget_YieldsTypedAmbiguousInDoubt_Promptly_PreservingCauseAndToken()
    {
        using var caller = new CancellationTokenSource();
        caller.Cancel(); // the caller's own token is already cancelled — it must NOT gate the verification
        var callerCause = new OperationCanceledException("caller cancelled after possible commit", caller.Token);
        var budget = TimeSpan.FromMilliseconds(100);

        var sw = Stopwatch.StartNew();
        var result = await ConditionalAppendExecution.RunAsync(
            Request("bv-hang"), "svc", _domain.EventTypes, "TestProvider",
            (_, _, _) => throw PostCommitMarker(callerCause),
            // The readback hangs until ITS token (the independent budget token) fires — never the caller's.
            async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; },
            ensureCommittedAsync: null,
            cancellationToken: caller.Token,
            verificationBudget: budget);
        sw.Stop();

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.True(ex.IsRetryable);
        Assert.Equal(ConditionalAppendInDoubtReason.AmbiguousAfterWrite, ex.Reason);
        Assert.Same(callerCause, ex.InnerException);                              // original cause preserved
        Assert.Equal(caller.Token, ((OperationCanceledException)ex.InnerException!).CancellationToken); // exact token
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"bounded verification should return promptly, took {sw.Elapsed}");
    }
}

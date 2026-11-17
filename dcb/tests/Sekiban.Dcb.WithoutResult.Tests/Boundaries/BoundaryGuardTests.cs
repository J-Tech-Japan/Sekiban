using System.Runtime.ExceptionServices;
using Sekiban.Dcb.Boundaries;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Boundaries;

/// <summary>
///     The policy every WithoutResult boundary applies, tested at the helper that applies it: a failure is rethrown
///     as itself with the boundary recorded on <see cref="Exception.Data" />, and a missing value names the boundary
///     instead of surfacing as a bare <see cref="NullReferenceException" />.
/// </summary>
public class BoundaryGuardTests
{
    private static readonly BoundaryContext Context = new("ISekibanExecutor.QueryAsync", "GetStudentQuery");

    // Rethrows the given instance keeping its stack, the way a core call that failed earlier would surface it.
    private static T Throw<T>(Exception failure)
    {
        ExceptionDispatchInfo.Throw(failure);
        return default!;
    }

    [Fact]
    public void Success_ReturnsTheValue()
    {
        var value = BoundaryGuard.Run(() => "ok", Context);
        Assert.Equal("ok", value);
    }

    [Fact]
    public void Success_ValueType_ReturnsTheValue()
    {
        // A legitimate false must still come back as false — the guard must not turn every falsy value into a fault.
        Assert.False(BoundaryGuard.Run(() => false, Context));
        Assert.Equal(0, BoundaryGuard.Run(() => 0, Context));
    }

    [Fact]
    public void Failure_ValueType_Throws()
    {
        var failure = new InvalidOperationException("event store unreachable");

        var thrown = Assert.Throws<InvalidOperationException>(() => BoundaryGuard.Run(() => Throw<bool>(failure), Context));
        Assert.Same(failure, thrown);
    }

    [Fact]
    public void Failure_ReferenceType_RethrowsTheOriginalInstance()
    {
        var failure = new InvalidOperationException("boom");
        var thrown = Assert.Throws<InvalidOperationException>(
            () => BoundaryGuard.Run(() => Throw<string>(failure), Context));

        // Not wrapped. WithoutResult callers catch the exception they threw; changing its type would break them.
        Assert.Same(failure, thrown);
        Assert.IsNotType<SekibanBoundaryException>(thrown);
    }

    [Fact]
    public void Failure_PreservesTheOriginalStack()
    {
        Exception failure;
        try
        {
            throw new InvalidOperationException("thrown somewhere real");
        }
        catch (InvalidOperationException caught)
        {
            failure = caught;
        }

        var originalStack = failure.StackTrace;
        Assert.NotNull(originalStack);

        var thrown = Assert.Throws<InvalidOperationException>(
            () => BoundaryGuard.Run(() => Throw<string>(failure), Context));

        // ExceptionDispatchInfo appends the rethrow site; the original frames must still be there.
        Assert.Contains(originalStack!.Trim().Split('\n')[0].Trim(), thrown.StackTrace);
    }

    [Fact]
    public void Failure_RecordsTheBoundaryOnExceptionData()
    {
        var failure = new InvalidOperationException("boom");
        var thrown = Assert.Throws<InvalidOperationException>(
            () => BoundaryGuard.Run(() => Throw<string>(failure), Context));

        // The type is untouched, so the context has to live somewhere that does not change the type.
        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Data[BoundaryGuard.OperationDataKey]);
        Assert.Equal("GetStudentQuery", thrown.Data[BoundaryGuard.TargetDataKey]);
    }

    [Fact]
    public void Failure_CrossingTwoBoundaries_KeepsTheInnermostOne()
    {
        var failure = new InvalidOperationException("boom");
        var inner = new BoundaryContext("ICommandContext.TagExistsAsync", "Student:1");
        var outer = new BoundaryContext("ISekibanExecutor.ExecuteAsync", "CreateStudent");

        Assert.Throws<InvalidOperationException>(() => BoundaryGuard.Run(() => Throw<bool>(failure), inner));
        Assert.Throws<InvalidOperationException>(() => BoundaryGuard.Run(() => Throw<string>(failure), outer));

        // The boundary nearest the failure is the informative one; the outer one must not overwrite it.
        Assert.Equal("ICommandContext.TagExistsAsync", failure.Data[BoundaryGuard.OperationDataKey]);
        Assert.Equal("Student:1", failure.Data[BoundaryGuard.TargetDataKey]);
    }

    [Fact]
    public void Cancellation_IsRethrownAsItself_NotWrapped()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancellation = new OperationCanceledException("cancelled", cts.Token);

        var thrown = Assert.Throws<OperationCanceledException>(
            () => BoundaryGuard.Run(() => Throw<string>(cancellation), Context));

        // Same instance, same type, same token — catch (OperationCanceledException) must keep working, and code
        // that inspects the token must still find it.
        Assert.Same(cancellation, thrown);
        Assert.Equal(cts.Token, thrown.CancellationToken);
        Assert.IsNotType<SekibanBoundaryException>(thrown);
    }

    [Fact]
    public void Cancellation_AtAValueTypedBoundary_IsAlsoRethrown()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancellation = new OperationCanceledException("cancelled", cts.Token);

        var thrown = Assert.Throws<OperationCanceledException>(() => BoundaryGuard.Run(() => Throw<bool>(cancellation), Context));
        Assert.Same(cancellation, thrown);
        Assert.Equal(cts.Token, thrown.CancellationToken);
    }

    [Fact]
    public void NullResult_NamesTheBoundary_InsteadOfABareNullReference()
    {
        var thrown = Assert.Throws<SekibanBoundaryException>(() => BoundaryGuard.Run<string>(() => null!, Context));

        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Operation);
        Assert.Equal("GetStudentQuery", thrown.Target);
        Assert.Contains("ISekibanExecutor.QueryAsync (GetStudentQuery)", thrown.Message);
        Assert.Contains("produced no value", thrown.Message);

        // There was no failure — inventing an InnerException would be inventing a cause.
        Assert.Null(thrown.InnerException);
    }

    [Fact]
    public async Task AwaitAsync_NullTask_NamesTheBoundary()
    {
        var thrown = await Assert.ThrowsAsync<SekibanBoundaryException>(
            () => BoundaryGuard.AwaitAsync<string>(null!, Context));

        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Operation);
        Assert.Contains("null Task", thrown.Message);
    }

    [Fact]
    public async Task AwaitAsync_NullResult_NamesTheBoundary()
    {
        var thrown = await Assert.ThrowsAsync<SekibanBoundaryException>(
            () => BoundaryGuard.AwaitAsync(Task.FromResult<string>(null!), Context));

        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Operation);
        Assert.Contains("produced no value", thrown.Message);
    }

    [Fact]
    public async Task AwaitAsync_Failure_RethrowsTheOriginal()
    {
        var failure = new InvalidOperationException("boom");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BoundaryGuard.AwaitAsync(Task.FromException<string>(failure), Context));

        Assert.Same(failure, thrown);
    }
}

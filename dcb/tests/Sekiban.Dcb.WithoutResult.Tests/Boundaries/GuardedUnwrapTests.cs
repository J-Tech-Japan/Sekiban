using ResultBoxes;
using Sekiban.Dcb.Boundaries;
using System.Reflection;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Boundaries;

/// <summary>
///     The policy every WithoutResult boundary now applies, tested at the helper that applies it.
///     Each test states the behaviour BEFORE this change where it differed, because the point of the change is the
///     difference: <c>UnwrapBox()</c> swallowed value-typed failures and turned a null box into a bare
///     <see cref="NullReferenceException" />.
/// </summary>
public class GuardedUnwrapTests
{
    private static readonly BoundaryContext Context = new("ISekibanExecutor.QueryAsync", "GetStudentQuery");

    /// <summary>
    ///     Builds a box with neither a value nor an exception — the shape no factory produces, and the one an
    ///     internal path would produce if it went wrong.
    /// </summary>
    private static ResultBox<T> BoxWithNeitherValueNorException<T>() where T : class
    {
        var ctor = typeof(ResultBox<T>).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(c => c.GetParameters().Length == 2);
        return (ResultBox<T>)ctor.Invoke([null, null]);
    }

    [Fact]
    public void SuccessBox_ReturnsTheValue()
    {
        var value = GuardedUnwrap.Unwrap(ResultBox.FromValue("ok"), Context);
        Assert.Equal("ok", value);
    }

    [Fact]
    public void SuccessBox_ValueType_ReturnsTheValue()
    {
        // A legitimate false must still come back as false — the guard must not turn every falsy value into a fault.
        Assert.False(GuardedUnwrap.Unwrap(ResultBox.FromValue(false), Context));
        Assert.Equal(0, GuardedUnwrap.Unwrap(ResultBox.FromValue(0), Context));
    }

    [Fact]
    public void FailedBox_ValueType_Throws_WhereUnwrapBoxSilentlyReturnedDefault()
    {
        var failure = new InvalidOperationException("event store unreachable");
        var box = ResultBox<bool>.Error(failure);

        // The defect, stated: this is what every value-typed boundary did before this change. A storage failure
        // became a plain `false` that the caller had no way to distinguish from "no, it does not exist".
        Assert.False(box.UnwrapBox());

        var thrown = Assert.Throws<InvalidOperationException>(() => GuardedUnwrap.Unwrap(box, Context));
        Assert.Same(failure, thrown);
    }

    [Fact]
    public void FailedBox_ReferenceType_RethrowsTheOriginalInstance()
    {
        var failure = new InvalidOperationException("boom");
        var thrown = Assert.Throws<InvalidOperationException>(
            () => GuardedUnwrap.Unwrap(ResultBox<string>.Error(failure), Context));

        // Not wrapped. WithoutResult callers catch the exception they threw; changing its type would break them.
        Assert.Same(failure, thrown);
        Assert.IsNotType<SekibanBoundaryException>(thrown);
    }

    [Fact]
    public void FailedBox_PreservesTheOriginalStack()
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
            () => GuardedUnwrap.Unwrap(ResultBox<string>.Error(failure), Context));

        // ExceptionDispatchInfo appends the rethrow site; the original frames must still be there.
        Assert.Contains(originalStack!.Trim().Split('\n')[0].Trim(), thrown.StackTrace);
    }

    [Fact]
    public void FailedBox_RecordsTheBoundaryOnExceptionData()
    {
        var failure = new InvalidOperationException("boom");
        var thrown = Assert.Throws<InvalidOperationException>(
            () => GuardedUnwrap.Unwrap(ResultBox<string>.Error(failure), Context));

        // The type is untouched, so the context has to live somewhere that does not change the type.
        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Data[GuardedUnwrap.OperationDataKey]);
        Assert.Equal("GetStudentQuery", thrown.Data[GuardedUnwrap.TargetDataKey]);
    }

    [Fact]
    public void FailedBox_CrossingTwoBoundaries_KeepsTheInnermostOne()
    {
        var failure = new InvalidOperationException("boom");
        var inner = new BoundaryContext("ICommandContext.TagExistsAsync", "Student:1");
        var outer = new BoundaryContext("ISekibanExecutor.ExecuteAsync", "CreateStudent");

        Assert.Throws<InvalidOperationException>(() => GuardedUnwrap.Unwrap(ResultBox<bool>.Error(failure), inner));
        Assert.Throws<InvalidOperationException>(
            () => GuardedUnwrap.Unwrap(ResultBox<string>.Error(failure), outer));

        // The boundary nearest the failure is the informative one; the outer one must not overwrite it.
        Assert.Equal("ICommandContext.TagExistsAsync", failure.Data[GuardedUnwrap.OperationDataKey]);
        Assert.Equal("Student:1", failure.Data[GuardedUnwrap.TargetDataKey]);
    }

    [Fact]
    public void Cancellation_IsRethrownAsItself_NotWrapped()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancellation = new OperationCanceledException("cancelled", cts.Token);

        var thrown = Assert.Throws<OperationCanceledException>(
            () => GuardedUnwrap.Unwrap(ResultBox<string>.Error(cancellation), Context));

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
        var box = ResultBox<bool>.Error(cancellation);

        // Before: a cancelled value-typed boundary returned `false` and the caller kept going.
        Assert.False(box.UnwrapBox());

        var thrown = Assert.Throws<OperationCanceledException>(() => GuardedUnwrap.Unwrap(box, Context));
        Assert.Same(cancellation, thrown);
        Assert.Equal(cts.Token, thrown.CancellationToken);
    }

    [Fact]
    public void NullBox_ReferenceType_NamesTheBoundary_InsteadOfABareNullReference()
    {
        // Issue #1045: `UnwrapBox()` on a null box is a NullReferenceException with no message and no operation.
        Assert.Throws<NullReferenceException>(() => ((ResultBox<string>)null!).UnwrapBox());

        var thrown = Assert.Throws<SekibanBoundaryException>(
            () => GuardedUnwrap.Unwrap((ResultBox<string>?)null, Context));

        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Operation);
        Assert.Equal("GetStudentQuery", thrown.Target);
        Assert.Contains("ISekibanExecutor.QueryAsync (GetStudentQuery)", thrown.Message);
        Assert.Contains("null ResultBox", thrown.Message);

        // There was no carried failure — inventing an InnerException would be inventing a cause.
        Assert.Null(thrown.InnerException);
    }

    [Fact]
    public void NullBox_ValueType_NamesTheBoundaryToo()
    {
        var thrown = Assert.Throws<SekibanBoundaryException>(
            () => GuardedUnwrap.Unwrap((ResultBox<bool>?)null, new BoundaryContext("ICommandContext.TagExistsAsync")));

        Assert.Equal("ICommandContext.TagExistsAsync", thrown.Operation);
        Assert.Null(thrown.Target);
        Assert.Contains("ICommandContext.TagExistsAsync", thrown.Message);
    }

    /// <summary>
    ///     Pins the ResultBoxes invariant the guard is written against: in 0.4.0 a box holding no value is a FAILED
    ///     box carrying a synthesised "result value is null" exception — a success-with-no-value cannot exist. The
    ///     guard still has a branch for that shape, and this test is what makes it honest: if a future ResultBoxes
    ///     lets a null value read as a success, this fails, and that branch stops being dead code.
    /// </summary>
    [Fact]
    public void BoxWithNoValue_IsAFailure_NotASuccess_InResultBoxes()
    {
        string? noValue = null;
        Assert.False(ResultBox.FromValue(noValue!).IsSuccess);

        var malformed = BoxWithNeitherValueNorException<string>();
        Assert.False(malformed.IsSuccess);
        Assert.NotNull(malformed.GetException());
    }

    [Fact]
    public void MalformedBox_NoValueAndNoException_StillSurfacesAnnotated()
    {
        var box = BoxWithNeitherValueNorException<string>();

        var thrown = Assert.ThrowsAny<Exception>(() => GuardedUnwrap.Unwrap(box, Context));

        // ResultBoxes synthesises the failure ("result value is null"); we rethrow it as itself and say where it
        // crossed, rather than letting it arrive with no idea which call produced it.
        Assert.Contains("null", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Data[GuardedUnwrap.OperationDataKey]);
    }

    [Fact]
    public async Task UnwrapAsync_NullTask_NamesTheBoundary()
    {
        var thrown = await Assert.ThrowsAsync<SekibanBoundaryException>(
            () => GuardedUnwrap.UnwrapAsync<string>(null!, Context));

        Assert.Equal("ISekibanExecutor.QueryAsync", thrown.Operation);
        Assert.Contains("null Task", thrown.Message);
    }

    [Fact]
    public async Task UnwrapAsync_FailedBox_RethrowsTheOriginal()
    {
        var failure = new InvalidOperationException("boom");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GuardedUnwrap.UnwrapAsync(Task.FromResult(ResultBox<string>.Error(failure)), Context));

        Assert.Same(failure, thrown);
    }
}

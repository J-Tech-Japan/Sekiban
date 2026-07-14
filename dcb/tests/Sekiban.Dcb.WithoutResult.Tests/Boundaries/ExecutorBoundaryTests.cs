using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Validation;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests.Boundaries;

/// <summary>
///     End to end through the real executor: the guard must add context to a failure WITHOUT changing what the
///     failure is. WithoutResult's whole promise is that a validation error arrives as a
///     <see cref="SekibanValidationException" /> — if opening the box started wrapping those, every
///     <c>catch (SekibanValidationException)</c> our users have written would stop firing.
/// </summary>
public class ExecutorBoundaryTests
{
    private readonly ISekibanExecutor _executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());

    [Fact]
    public async Task ExecuteAsync_ValidationFailure_StaysAValidationException_AndNamesTheCommand()
    {
        var command = new CreateStudent(Guid.NewGuid(), "", 5);

        var thrown = await Assert.ThrowsAsync<SekibanValidationException>(() => _executor.ExecuteAsync(command));

        // The type the caller catches is unchanged...
        Assert.IsType<SekibanValidationException>(thrown);

        // ...and the boundary it crossed is now recorded on it.
        Assert.Equal("ISekibanExecutor.ExecuteAsync", thrown.Data[GuardedUnwrap.OperationDataKey]);
        Assert.Equal(nameof(CreateStudent), thrown.Data[GuardedUnwrap.TargetDataKey]);
    }

    [Fact]
    public async Task ExecuteCommandAsync_HandlerThrows_StaysTheHandlersException_AndNamesTheBoundary()
    {
        var failure = new InvalidOperationException("boom");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _executor.ExecuteCommandAsync(_ => throw failure));

        Assert.Same(failure, thrown);
        Assert.Equal("ISekibanExecutor.ExecuteCommandAsync", thrown.Data[GuardedUnwrap.OperationDataKey]);
    }

    [Fact]
    public async Task ExecuteAsync_Success_IsUntouched()
    {
        var studentId = Guid.NewGuid();

        var result = await _executor.ExecuteAsync(new CreateStudent(studentId, "Test Student", 2));

        Assert.Single(result.Events);
    }
}

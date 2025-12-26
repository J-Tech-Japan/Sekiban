using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.WithoutResult.Tests;

public class ExecuteCommandAsyncTests
{
    private readonly ISekibanExecutor _executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());

    [Fact]
    public async Task ExecuteCommandAsync_NoEvent_ReturnsEmptyExecutionResult()
    {
        var result = await _executor.ExecuteCommandAsync(_ => Task.FromResult(EventOrNone.Empty));

        Assert.Empty(result.Events);
        Assert.Equal(Guid.Empty, result.EventId);
    }

    [Fact]
    public async Task ExecuteCommandAsync_SingleEvent_ReturnsExecutionResultWithEvent()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);

        var result = await _executor.ExecuteCommandAsync(_ =>
            Task.FromResult(EventOrNone.From(new StudentCreated(studentId, "Test Student", 2), tag)));

        var ev = Assert.Single(result.Events);
        var payload = Assert.IsType<StudentCreated>(ev.Payload);
        Assert.Equal(studentId, payload.StudentId);
        Assert.Contains(tag.GetTag(), ev.Tags);
    }

    [Fact]
    public async Task ExecuteCommandAsync_HandlerThrows_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _executor.ExecuteCommandAsync(_ => throw new InvalidOperationException("boom")));
    }
}

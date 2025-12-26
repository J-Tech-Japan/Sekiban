using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class ExecuteCommandAsyncTests
{
    private readonly ISekibanExecutor _executor = new InMemoryDcbExecutor(DomainType.GetDomainTypes());

    [Fact]
    public async Task ExecuteCommandAsync_NoEvent_ReturnsEmptyExecutionResult()
    {
        var result = await _executor.ExecuteCommandAsync(_ => Task.FromResult(EventOrNone.None));

        Assert.True(result.IsSuccess);

        var execution = result.GetValue();
        Assert.Empty(execution.Events);
        Assert.Equal(Guid.Empty, execution.EventId);
    }

    [Fact]
    public async Task ExecuteCommandAsync_SingleEvent_ReturnsExecutionResultWithEvent()
    {
        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);

        var result = await _executor.ExecuteCommandAsync(_ =>
            Task.FromResult(EventOrNone.EventWithTags(new StudentCreated(studentId, "Test Student", 2), tag)));

        Assert.True(result.IsSuccess);

        var execution = result.GetValue();
        var ev = Assert.Single(execution.Events);
        var payload = Assert.IsType<StudentCreated>(ev.Payload);
        Assert.Equal(studentId, payload.StudentId);
        Assert.Contains(tag.GetTag(), ev.Tags);
    }

    [Fact]
    public async Task ExecuteCommandAsync_HandlerThrows_ReturnsError()
    {
        var result = await _executor.ExecuteCommandAsync(_ =>
            Task.FromException<ResultBox<EventOrNone>>(new InvalidOperationException("boom")));

        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("boom", exception.Message);
    }
}

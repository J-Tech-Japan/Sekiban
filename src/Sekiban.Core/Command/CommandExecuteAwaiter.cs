using System.Collections.Concurrent;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Execute Awaiter implementation.
///     Note: This class is for internal use only
/// </summary>
public class CommandExecuteAwaiter : ICommandExecuteAwaiter
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _taskCompletionSources;
    public CommandExecuteAwaiter() => _taskCompletionSources = new ConcurrentDictionary<string, TaskCompletionSource<bool>>();
    public void StartTask<TAggregatePayload>(Guid aggregateId)
    {
        _taskCompletionSources.AddOrUpdate(GetKey<TAggregatePayload>(aggregateId), new TaskCompletionSource<bool>(), (_, oldValue) => oldValue);
    }
    public async Task WaitUntilOtherThreadFinished<TAggregatePayload>(Guid aggregateId)
    {
        if (_taskCompletionSources.TryGetValue(GetKey<TAggregatePayload>(aggregateId), out var tcs))
        {
            await tcs.Task;
        }
    }
    public void EndTask<TAggregatePayload>(Guid aggregateId)
    {
        if (_taskCompletionSources.TryRemove(GetKey<TAggregatePayload>(aggregateId), out var tcs))
        {
            tcs.SetResult(true);
        }
    }

    private string GetKey<TAggregatePayload>(Guid aggregateId) => $"{typeof(TAggregatePayload).Name}_{aggregateId}";
}

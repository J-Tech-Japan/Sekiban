using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Execute Awaiter implementation.
///     Note: This class is for internal use only
/// </summary>
public class CommandExecuteAwaiter : ICommandExecuteAwaiter
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _taskCompletionSources = new();
    private readonly ILogger<CommandExecuteAwaiter> _logger;
    public CommandExecuteAwaiter(ILogger<CommandExecuteAwaiter> logger) => _logger = logger;
    public async Task WaitUntilOtherThreadFinished<TAggregatePayload>(Guid aggregateId)
    {
        if (_taskCompletionSources.TryGetValue(GetKey<TAggregatePayload>(aggregateId), out var tcs))
        {
            await tcs.Task;
        }
    }
    public async Task StartTaskAsync<TAggregatePayload>(Guid aggregateId)
    {
        await Task.CompletedTask;
        _taskCompletionSources.AddOrUpdate(GetKey<TAggregatePayload>(aggregateId), new TaskCompletionSource<bool>(), (_, oldValue) => oldValue);
    }
    public async Task EndTaskAsync<TAggregatePayload>(Guid aggregateId)
    {
        if (_taskCompletionSources.TryRemove(GetKey<TAggregatePayload>(aggregateId), out var tcs))
        {
            tcs.SetResult(true);
        }
        await Task.CompletedTask;
    }

    private string GetKey<TAggregatePayload>(Guid aggregateId) => $"{typeof(TAggregatePayload).Name}_{aggregateId}";
}

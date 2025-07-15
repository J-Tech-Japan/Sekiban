using Dapr.Actors.Runtime;

namespace DaprSample2;

/// <summary>
/// Counter Actor implementation
/// </summary>
public class CounterActor : Actor, ICounterActor
{
    private const string CounterStateName = "counter";

    public CounterActor(ActorHost host) : base(host)
    {
    }

    /// <summary>
    /// Get counter value
    /// </summary>
    public async Task<int> GetCountAsync()
    {
        Logger.LogInformation("Counter Actor {ActorId} GetCountAsync called", Id);
        var count = await StateManager.TryGetStateAsync<int>(CounterStateName);
        var result = count.HasValue ? count.Value : 0;
        Logger.LogInformation("Counter Actor {ActorId} returning count: {Count}", Id, result);
        return result;
    }

    /// <summary>
    /// Increment counter
    /// </summary>
    public async Task IncrementAsync()
    {
        var currentCount = await GetCountAsync();
        var newCount = currentCount + 1;
        await StateManager.SetStateAsync(CounterStateName, newCount);
        
        Logger.LogInformation("Counter {ActorId} incremented to {Count}", Id, newCount);
    }

    /// <summary>
    /// Reset counter
    /// </summary>
    public async Task ResetAsync()
    {
        await StateManager.SetStateAsync(CounterStateName, 0);
        Logger.LogInformation("Counter {ActorId} reset to 0", Id);
    }

    /// <summary>
    /// Processing when Actor is activated
    /// </summary>
    protected override Task OnActivateAsync()
    {
        Logger.LogInformation("Counter Actor {ActorId} activated", Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Processing when Actor is deactivated
    /// </summary>
    protected override Task OnDeactivateAsync()
    {
        Logger.LogInformation("Counter Actor {ActorId} deactivated", Id);
        return Task.CompletedTask;
    }
}

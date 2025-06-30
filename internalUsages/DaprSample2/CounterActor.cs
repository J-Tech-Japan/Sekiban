using Dapr.Actors.Runtime;

namespace DaprSample2;

/// <summary>
/// カウンターActorの実装
/// </summary>
public class CounterActor : Actor, ICounterActor
{
    private const string CounterStateName = "counter";

    public CounterActor(ActorHost host) : base(host)
    {
    }

    /// <summary>
    /// カウンターの値を取得
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
    /// カウンターをインクリメント
    /// </summary>
    public async Task IncrementAsync()
    {
        var currentCount = await GetCountAsync();
        var newCount = currentCount + 1;
        await StateManager.SetStateAsync(CounterStateName, newCount);
        
        Logger.LogInformation("Counter {ActorId} incremented to {Count}", Id, newCount);
    }

    /// <summary>
    /// カウンターをリセット
    /// </summary>
    public async Task ResetAsync()
    {
        await StateManager.SetStateAsync(CounterStateName, 0);
        Logger.LogInformation("Counter {ActorId} reset to 0", Id);
    }

    /// <summary>
    /// Actor起動時の処理
    /// </summary>
    protected override Task OnActivateAsync()
    {
        Logger.LogInformation("Counter Actor {ActorId} activated", Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Actor非活性化時の処理
    /// </summary>
    protected override Task OnDeactivateAsync()
    {
        Logger.LogInformation("Counter Actor {ActorId} deactivated", Id);
        return Task.CompletedTask;
    }
}

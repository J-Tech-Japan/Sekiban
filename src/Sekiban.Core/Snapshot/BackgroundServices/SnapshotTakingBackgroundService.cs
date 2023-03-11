using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Core.Events;
using System.Collections.Concurrent;
namespace Sekiban.Core.Snapshot.BackgroundServices;

public class SnapshotTakingBackgroundService : BackgroundService
{
    private readonly BlockingCollection<IEvent> _eventQueue = new();
    public IServiceProvider? ServiceProvider { get; set; } = null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("SnapshotTakingBackgroundService is started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            // タスクがある場合は実行する
            if (_eventQueue.TryTake(out var ev))
            {
                if (ServiceProvider is null) { return; }
                using var scope = ServiceProvider.CreateScope();
                var snapshotGenerator = scope.ServiceProvider.GetService<SnapshotGenerator>();
                if (snapshotGenerator is null) { continue; }
                await snapshotGenerator.Generate(ev);
            }
            // タスクがない場合は待機する
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }

    public void EnqueueTask(IEvent ev)
    {
        _eventQueue.Add(ev);
    }
}

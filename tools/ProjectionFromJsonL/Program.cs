// See https://aka.ms/new-console-template for more information
using BuildingAnalysis.MapScan.Domain;
using BuildingAnalysis.MapScan.Domain.Aggregates.DetectedObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Dependency;
using Sekiban.Core.Events;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.Cosmos;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
Console.WriteLine("Hello, World!");


var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly());
var configuration = builder.Build();
var serviceCollection = new ServiceCollection();
serviceCollection.AddSingleton<IConfiguration>(configuration);
SekibanAspNetCoreEventSourcingDependency.Register(serviceCollection, new DomainDependency(), SekibanSettings.FromConfiguration(configuration));
serviceCollection.AddSekibanCosmosDb(configuration);
serviceCollection.AddLogging();
// serviceCollection.AddTransient<EventsConverter>();
var ServiceProvider = serviceCollection.BuildServiceProvider();

var registeredEventTypes = ServiceProvider.GetRequiredService<RegisteredEventTypes>();
var multiProjectionSnapshotGenerator = ServiceProvider.GetRequiredService<IMultiProjectionSnapshotGenerator>() as MultiProjectionSnapshotGenerator ?? throw new ApplicationException("IMultiProjectionSnapshotGenerator is not registered");

// var file = "/Users/tomohisa/dev/cosmos-migration-tool/events.cosmos.sr.mapscan.20250422.jsonl";
// var file = "/Users/tomohisa/dev/cosmos-migration-tool/events.cosmos.sr.mapscan.20250427.sorted.jsonl";
var file = "/Users/tomohisa/dev/cosmos-migration-tool/events.cosmos.sr.mapscan.20250428.sorted.jsonl";
Console.WriteLine(file);


multiProjectionSnapshotGenerator.EventRetriverSubstition = async projector =>
{
    var typedProjector = projector as SingleProjectionListProjector<Aggregate<DetectedObject>, AggregateState<DetectedObject>,
        DefaultSingleProjector<DetectedObject>> ?? throw new ApplicationException("SingleProjectionListProjector is not registered");
    await Task.CompletedTask;

    var i = 0;
    var stopwatch = new Stopwatch();
    stopwatch.Start();
    var lastBatchTime = DateTime.Now;
    await JsonlBatchProcessor.RunAsync(file,10_000, async (batch, ct) =>
    {
        await Task.CompletedTask;
        var currentTime = DateTime.Now;
        var elapsedTime = currentTime - lastBatchTime;
        Console.WriteLine($"Batch: {i++}, Elapsed time: {elapsedTime.TotalSeconds:F2} seconds, Items per second: {batch.Count / elapsedTime.TotalSeconds:F2}");
        Console.WriteLine(batch.Count);
        lastBatchTime = currentTime;
        foreach (var evstring in batch)
        {
            var evJsonElement = SekibanJsonHelper.Deserialize<JsonElement>(evstring);
            var ev = EventProcessor.ProcessEvent(evJsonElement, registeredEventTypes);
            if (ev.AggregateType == typeof(DetectedObject).Name)
            {
                if (typedProjector.EventShouldBeApplied(ev))
                {
                    typedProjector.ApplyEvent(ev);
                }
            }
        }
    }, CancellationToken.None);

    Console.WriteLine("Done");
    Console.WriteLine($"Version: {typedProjector.Version}");
    // var state = typedProjector.ToState();
    // Console.WriteLine($"State: {state}");

    return typedProjector;
};

await multiProjectionSnapshotGenerator.GenerateMultiProjectionSnapshotAsync<SingleProjectionListProjector<Aggregate<DetectedObject>, AggregateState<DetectedObject>,
    DefaultSingleProjector<DetectedObject>>,SingleProjectionListState<AggregateState<DetectedObject>>>(3000,IMultiProjectionService.ProjectionAllRootPartitions);









//
// var aggregateProjector
//     = new SingleProjectionListProjector<Aggregate<DetectedObject>, AggregateState<DetectedObject>,
//         DefaultSingleProjector<DetectedObject>>();
// var i = 0;
// var stopwatch = new Stopwatch();
// stopwatch.Start();
// var lastBatchTime = DateTime.Now;
// JsonlBatchProcessor.RunAsync(file,10_000, async (batch, ct) =>
// {
//     await Task.CompletedTask;
//     var currentTime = DateTime.Now;
//     var elapsedTime = currentTime - lastBatchTime;
//     Console.WriteLine($"Batch: {i++}, Elapsed time: {elapsedTime.TotalSeconds:F2} seconds, Items per second: {batch.Count / elapsedTime.TotalSeconds:F2}");
//     Console.WriteLine(batch.Count);
//     lastBatchTime = currentTime;
//     foreach (var evstring in batch)
//     {
//         var evJsonElement = SekibanJsonHelper.Deserialize<JsonElement>(evstring);
//         var ev = EventProcessor.ProcessEvent(evJsonElement, registeredEventTypes);
//         if (ev.AggregateType == typeof(DetectedObject).Name)
//         {
//             aggregateProjector.ApplyEvent(ev);
//         }
//     }
// }, CancellationToken.None).Wait();
//
// Console.WriteLine("Done");
// Console.WriteLine($"Version: {aggregateProjector.Version}");
// var state = aggregateProjector.ToState();
// Console.WriteLine($"State: {state}");

using Dapr.Actors;
using Dapr.Actors.Client;
using DaprSample2;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddActors(options =>
{
    options.Actors.RegisterActor<CounterActor>();
});

var app = builder.Build();

app.MapActorsHandlers();

app.MapGet("/counter/{id}", async (string id, IActorProxyFactory actorProxyFactory) =>
{
    var actorId = new ActorId(id);
    var counterActor = actorProxyFactory.CreateActorProxy<ICounterActor>(actorId, nameof(CounterActor));
    var count = await counterActor.GetCountAsync();
    return Results.Ok(new { ActorId = id, Count = count });
});

app.MapPost("/counter/{id}/increment", async (string id, IActorProxyFactory actorProxyFactory) =>
{
    var actorId = new ActorId(id);
    var counterActor = actorProxyFactory.CreateActorProxy<ICounterActor>(actorId, nameof(CounterActor));
    await counterActor.IncrementAsync();
    var count = await counterActor.GetCountAsync();
    return Results.Ok(new { ActorId = id, Count = count, Message = "Incremented" });
});

app.MapPost("/counter/{id}/reset", async (string id, IActorProxyFactory actorProxyFactory) =>
{
    var actorId = new ActorId(id);
    var counterActor = actorProxyFactory.CreateActorProxy<ICounterActor>(actorId, nameof(CounterActor));
    await counterActor.ResetAsync();
    return Results.Ok(new { ActorId = id, Count = 0, Message = "Reset" });
});

app.MapGet("/", () => "DaprSample2 - Simple Dapr Actor Demo is running!");

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

app.Run();

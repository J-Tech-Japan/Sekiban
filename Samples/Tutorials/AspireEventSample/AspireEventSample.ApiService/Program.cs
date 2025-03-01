using AspireEventSample.ApiService;
using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Generated;
using AspireEventSample.ApiService.Projections;
using Microsoft.AspNetCore.Mvc;
using ResultBoxes;
using Scalar.AspNetCore;
using Sekiban.Pure;
using Sekiban.Pure.AspNetCore;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Orleans;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Postgres;
using Sekiban.Pure.Projectors;
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// orleans related integrations
builder.AddKeyedAzureTableClient("clustering");
builder.AddKeyedAzureBlobClient("grain-state");
builder.UseOrleans(
    config =>
    {
        config.UseDashboard(options => { });
        config.AddMemoryStreams("EventStreamProvider").AddMemoryGrainStorage("EventStreamProvider");
    });


// source generator serialization options
builder.Services.AddSingleton(
    AspireEventSampleApiServiceDomainTypes.Generate(AspireEventSampleApiServiceEventsJsonContext.Default.Options));
// general json serializer options
// builder.Services.AddSingleton(AspireEventSampleApiServiceDomainTypes.Generate());
SekibanSerializationTypesChecker.CheckDomainSerializability(AspireEventSampleApiServiceDomainTypes.Generate());

builder.Services.AddTransient<ICommandMetadataProvider, CommandMetadataProvider>();
builder.Services.AddTransient<IExecutingUserProvider, HttpExecutingUserProvider>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddTransient<SekibanOrleansExecutor>();



if (builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower() == "cosmos")
{
    // Cosmos settings
    builder.AddSekibanCosmosDb();
} else
{
    // Postgres settings
    builder.AddSekibanPostgresDb();
}
var app = builder.Build();
// Configure the HTTP request pipeline.
app.UseExceptionHandler();


var apiRoute = app
    .MapGroup("/api")
    .AddEndpointFilter<ExceptionEndpointFilter>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

string[] summaries =
    ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app
    .MapGet(
        "/weatherforecast",
        () =>
        {
            var forecast = Enumerable
                .Range(1, 5)
                .Select(
                    index =>
                        new WeatherForecast(
                            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                            Random.Shared.Next(-20, 55),
                            summaries[Random.Shared.Next(summaries.Length)]
                        ))
                .ToArray();
            return forecast;
        })
    .WithName("GetWeatherForecast");

apiRoute
    .MapGet(
        "/getMultiProjection",
        async ([FromServices] IClusterClient clusterClient, [FromServices] SekibanDomainTypes sekibanDomainTypes) =>
        {
            var multiProjectorGrain
                = clusterClient.GetGrain<IMultiProjectorGrain>(BranchMultiProjector.GetMultiProjectorName());
            var state = await multiProjectorGrain.GetStateAsync();
            return sekibanDomainTypes.MultiProjectorsType.ToTypedState(state);
        })
    .WithName("GetMultiProjection")
    .WithOpenApi();

apiRoute
    .MapGet(
        "/branchProjectionWithAggregate",
        async ([FromServices] IClusterClient clusterClient, [FromServices] SekibanDomainTypes sekibanDomainTypes) =>
        {
            var multiProjectorGrain
                = clusterClient.GetGrain<IMultiProjectorGrain>(
                    AggregateListProjector<BranchProjector>.GetMultiProjectorName());
            var state = await multiProjectorGrain.GetStateAsync();
            return sekibanDomainTypes.MultiProjectorsType.ToTypedState(state);
        })
    .WithName("branchProjectionWithAggregate")
    .WithDescription(
        "This is failing due to no serializer of ListQueryResult AggregateListProjector<BranchProjector>. Can Still use query")
    .WithOpenApi();

app.MapDefaultEndpoints();

// Add new app.MapPost() method here
apiRoute
    .MapPost(
        "/registerbranch",
        async (
            [FromBody] RegisterBranch command,
            [FromServices] SekibanOrleansExecutor executor) => await executor.CommandAsync(command).UnwrapBox())
    .WithName("RegisterBranch")
    .WithOpenApi();
apiRoute
    .MapPost(
        "/changebranchname",
        async (
            [FromBody] ChangeBranchName command,
            [FromServices] SekibanOrleansExecutor executor) => await executor.CommandAsync(command).UnwrapBox())
    .WithName("ChangeBranchName")
    .WithOpenApi();

apiRoute
    .MapGet(
        "/branch/{branchId}",
        (
            [FromRoute] Guid branchId,
            [FromServices] SekibanOrleansExecutor executor) => executor
            .LoadAggregateAsync<BranchProjector>(
                PartitionKeys<BranchProjector>.Existing(branchId))
            .Conveyor(aggregate => executor.GetDomainTypes().AggregateTypes.ToTypedPayload(aggregate))
            .UnwrapBox()
    )
    .WithName("GetBranch")
    .WithOpenApi();

apiRoute
    .MapGet(
        "/branch/{branchId}/reload",
        async (
            [FromRoute] Guid branchId,
            [FromServices] IClusterClient clusterClient,
            [FromServices] SekibanDomainTypes sekibanTypes) =>
        {
            var partitionKeyAndProjector =
                new PartitionKeysAndProjector(PartitionKeys<BranchProjector>.Existing(branchId), new BranchProjector());
            var aggregateProjectorGrain =
                clusterClient.GetGrain<IAggregateProjectorGrain>(partitionKeyAndProjector.ToProjectorGrainKey());
            var state = await aggregateProjectorGrain.RebuildStateAsync();
            return sekibanTypes.AggregateTypes.ToTypedPayload(state).UnwrapBox();
        })
    .WithName("GetBranchReload")
    .WithOpenApi();

apiRoute
    .MapGet(
        "/branchExists/{nameContains}",
        (
                [FromRoute] string nameContains,
                [FromServices] SekibanOrleansExecutor executor) =>
            executor.QueryAsync(new BranchExistsQuery(nameContains)).UnwrapBox())
    .WithName("BranchExists")
    .WithOpenApi();

apiRoute
    .MapGet(
        "/searchBranches",
        (
                [FromQuery] string nameContains,
                [FromServices] SekibanOrleansExecutor executor) =>
            executor.QueryAsync(new SimpleBranchListQuery(nameContains)).UnwrapBox())
    .WithName("SearchBranches")
    .WithOpenApi();
apiRoute
    .MapGet(
        "/searchBranches2",
        (
                [FromQuery] string nameContains,
                [FromServices] SekibanOrleansExecutor executor) =>
            executor.QueryAsync(new BranchQueryFromAggregateList(nameContains)).UnwrapBox())
    .WithName("SearchBranches2")
    .WithOpenApi();

app.Run();
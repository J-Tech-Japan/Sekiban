using AspireEventSample.ApiService.Projections;
using AspireEventSample.Domain.Aggregates.Branches;
using Microsoft.AspNetCore.Mvc;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Orleans;
using Sekiban.Pure.Orleans.Parts;
namespace AspireEventSample.ApiService.Endpoints;

public static class BranchEndpoints
{
    public static void MapBranchEndpoints(this IEndpointRouteBuilder apiRoute)
    {
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
                        new PartitionKeysAndProjector(
                            PartitionKeys<BranchProjector>.Existing(branchId),
                            new BranchProjector());
                    var aggregateProjectorGrain =
                        clusterClient.GetGrain<IAggregateProjectorGrain>(
                            partitionKeyAndProjector.ToProjectorGrainKey());
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
    }
}
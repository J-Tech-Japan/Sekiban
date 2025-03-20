using AspireEventSample.ApiService.Aggregates.Carts;
using Microsoft.AspNetCore.Mvc;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Orleans;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Projectors;

namespace AspireEventSample.ApiService.Endpoints;

public static class CartEndpoints
{
    public static void MapCartEndpoints(this IEndpointRouteBuilder apiRoute)
    {
        // Create a new shopping cart
        apiRoute
            .MapPost(
                "/cart/create",
                async (
                    [FromBody] CreateShoppingCart command,
                    [FromServices] SekibanOrleansExecutor executor) => await executor.CommandAsync(command).UnwrapBox())
            .WithName("CreateShoppingCart")
            .WithOpenApi();

        // Add an item to a shopping cart
        apiRoute
            .MapPost(
                "/cart/additem",
                async (
                    [FromBody] AddItemToShoppingCart command,
                    [FromServices] SekibanOrleansExecutor executor) => await executor.CommandAsync(command).UnwrapBox())
            .WithName("AddItemToShoppingCart")
            .WithOpenApi();

        // Process payment for a shopping cart
        apiRoute
            .MapPost(
                "/cart/processpayment",
                async (
                    [FromBody] ProcessPaymentOnShoppingCart command,
                    [FromServices] SekibanOrleansExecutor executor) => await executor.CommandAsync(command).UnwrapBox())
            .WithName("ProcessPaymentOnShoppingCart")
            .WithOpenApi();

        // Get a shopping cart by ID
        apiRoute
            .MapGet(
                "/cart/{cartId}",
                (
                    [FromRoute] Guid cartId,
                    [FromServices] SekibanOrleansExecutor executor) => executor
                    .LoadAggregateAsync<ShoppingCartProjector>(
                        PartitionKeys<ShoppingCartProjector>.Existing(cartId))
                    .Conveyor(aggregate => executor.GetDomainTypes().AggregateTypes.ToTypedPayload(aggregate))
                    .UnwrapBox()
            )
            .WithName("GetShoppingCart")
            .WithOpenApi();

        // Reload a shopping cart (rebuild state)
        apiRoute
            .MapGet(
                "/cart/{cartId}/reload",
                async (
                    [FromRoute] Guid cartId,
                    [FromServices] IClusterClient clusterClient,
                    [FromServices] SekibanDomainTypes sekibanTypes) =>
                {
                    var partitionKeyAndProjector =
                        new PartitionKeysAndProjector(PartitionKeys<ShoppingCartProjector>.Existing(cartId), new ShoppingCartProjector());
                    var aggregateProjectorGrain =
                        clusterClient.GetGrain<IAggregateProjectorGrain>(partitionKeyAndProjector.ToProjectorGrainKey());
                    var state = await aggregateProjectorGrain.RebuildStateAsync();
                    return sekibanTypes.AggregateTypes.ToTypedPayload(state).UnwrapBox();
                })
            .WithName("GetShoppingCartReload")
            .WithOpenApi();
    }
}

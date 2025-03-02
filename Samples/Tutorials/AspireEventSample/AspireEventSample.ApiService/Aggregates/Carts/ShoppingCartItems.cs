namespace AspireEventSample.ApiService.Aggregates.Carts;

using Orleans.Serialization;

[GenerateSerializer]
public record ShoppingCartItems(
    [property: Id(0)] string Name,
    [property: Id(1)] int Quantity,
    [property: Id(2)] Guid ItemId,
    [property: Id(3)] int Price);

using Sekiban.Pure.ReadModel;
namespace AspireEventSample.ReadModels;

[GenerateSerializer]
public class CartItemDbRecord : IReadModelEntity
{
    // IReadModelEntity properties
    [Id(0)]
    public Guid Id { get; set; }
    [Id(1)]
    public Guid TargetId { get; set; }
    [Id(2)]
    public string RootPartitionKey { get; set; } = string.Empty;
    [Id(3)]
    public string AggregateGroup { get; set; } = string.Empty;
    [Id(4)]
    public string LastSortableUniqueId { get; set; } = string.Empty;
    [Id(5)]
    public DateTime TimeStamp { get; set; }

    // Cart item specific properties
    [Id(6)]
    public Guid CartId { get; set; } // Reference to the parent cart
    [Id(7)]
    public string Name { get; set; } = string.Empty;
    [Id(8)]
    public int Quantity { get; set; }
    [Id(9)]
    public Guid ItemId { get; set; }
    [Id(10)]
    public int Price { get; set; }
}
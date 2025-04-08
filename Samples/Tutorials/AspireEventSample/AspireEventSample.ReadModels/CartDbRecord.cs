using Sekiban.Pure.ReadModel;
using System.Text.Json.Serialization;
namespace AspireEventSample.ReadModels;

[GenerateSerializer]
public class CartDbRecord : IReadModelEntity
{
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

    // Cart specific properties
    [Id(6)]
    public Guid UserId { get; set; }
    [Id(7)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public string Status { get; set; } = string.Empty;
    [Id(8)]
    public int TotalAmount { get; set; }

    // Items are now stored in a separate table (CartItemDbRecord)

    // Default constructor for EF Core
}
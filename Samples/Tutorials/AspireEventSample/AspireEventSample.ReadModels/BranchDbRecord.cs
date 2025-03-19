using Orleans.Serialization;
using Sekiban.Pure.Orleans.ReadModels;

namespace AspireEventSample.ReadModels;

[GenerateSerializer]
public class BranchDbRecord : IReadModelEntity
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
    [Id(6)]
    public string Name { get; set; } = string.Empty;
    [Id(7)]
    public string Country { get; set; } = string.Empty;

    // Default constructor for EF Core
}

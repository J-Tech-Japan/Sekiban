using Orleans.Serialization;

namespace AspireEventSample.ReadModels;

[GenerateSerializer]
public class BranchDbRecord
{
    public Guid Id { get; set; }
    public Guid TargetId { get; set; }
    public string RootPartitionKey { get; set; } = string.Empty;
    public string AggregateGroup { get; set; } = string.Empty;
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public DateTime TimeStamp { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    // Default constructor for EF Core

}

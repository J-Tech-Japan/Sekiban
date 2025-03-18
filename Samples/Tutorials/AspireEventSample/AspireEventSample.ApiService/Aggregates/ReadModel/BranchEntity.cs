using AspireEventSample.ReadModels;
using Sekiban.Pure.Orleans.ReadModels;
namespace AspireEventSample.ApiService.Aggregates.ReadModel;

[GenerateSerializer]
public record BranchEntity : IReadModelEntity
{
    [Id(0)]
    public required Guid Id { get; init; }
    [Id(1)]
    public required Guid TargetId { get; init; }
    [Id(2)]
    public required string RootPartitionKey { get; init; }
    [Id(3)]
    public required string AggregateGroup { get; init; }
    [Id(4)]
    public required string LastSortableUniqueId { get; init; }
    [Id(5)]
    public required DateTime TimeStamp { get; init; }
    [Id(6)]
    public required string Name { get; init; }

    // Constructor to create from BranchEntity
    public static BranchDbRecord ToDbRecord(BranchEntity entity) =>
        new()
        {
            Id = entity.Id,
            TargetId = entity.TargetId,
            RootPartitionKey = entity.RootPartitionKey,
            AggregateGroup = entity.AggregateGroup,
            LastSortableUniqueId = entity.LastSortableUniqueId,
            TimeStamp = entity.TimeStamp,
            Name = entity.Name
        };

    // Convert to BranchEntity
    public static BranchEntity FromDbRecord(BranchDbRecord record) =>
        new()
        {
            Id = record.Id,
            TargetId = record.TargetId,
            RootPartitionKey = record.RootPartitionKey,
            AggregateGroup = record.AggregateGroup,
            LastSortableUniqueId = record.LastSortableUniqueId,
            TimeStamp = record.TimeStamp,
            Name = record.Name
        };

}

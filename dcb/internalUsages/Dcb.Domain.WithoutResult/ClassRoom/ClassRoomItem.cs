using Orleans;

namespace Dcb.Domain.WithoutResult.ClassRoom;

/// <summary>
/// Unified representation of a classroom for list display
/// </summary>
[GenerateSerializer]
public record ClassRoomItem
{
    [Id(0)]
    public Guid ClassRoomId { get; init; }
    
    [Id(1)]
    public string Name { get; init; } = string.Empty;
    
    [Id(2)]
    public int MaxStudents { get; init; }
    
    [Id(3)]
    public int EnrolledCount { get; init; }
    
    [Id(4)]
    public bool IsFull { get; init; }
    
    [Id(5)]
    public int RemainingCapacity { get; init; }
}
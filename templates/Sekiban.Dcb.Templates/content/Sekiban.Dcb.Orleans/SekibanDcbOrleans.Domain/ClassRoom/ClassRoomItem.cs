namespace Dcb.Domain.ClassRoom;

/// <summary>
/// Unified representation of a classroom for list display
/// </summary>
public record ClassRoomItem
{
    public Guid ClassRoomId { get; init; }
    
    public string Name { get; init; } = string.Empty;
    
    public int MaxStudents { get; init; }
    
    public int EnrolledCount { get; init; }
    
    public bool IsFull { get; init; }
    
    public int RemainingCapacity { get; init; }
}
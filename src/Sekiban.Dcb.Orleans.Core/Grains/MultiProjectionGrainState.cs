namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Persistent state for the multi-projection grain
/// </summary>
[GenerateSerializer]
public class MultiProjectionGrainState
{
    [Id(0)]
    public string ProjectorName { get; set; } = string.Empty;
    [Id(1)]
    public string? SerializedState { get; set; }
    [Id(2)]
    public string? LastPosition { get; set; }
    [Id(3)]
    public DateTime LastPersistTime { get; set; }
    [Id(4)]
    public long EventsProcessed { get; set; }
    [Id(5)]
    public long StateSize { get; set; }
    [Id(6)]
    public string? SafeLastPosition { get; set; }
}

using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Backup of safe state with unsafe events that modified it
/// </summary>
public record SafeStateBackup<T>(T SafeState, List<Event> UnsafeEvents) where T : class;

namespace Sekiban.Core.History;

/// <summary>
///     Interface for call histories.
///     Call history can be append when event subscription happened
/// </summary>
public interface ICallHistories
{
    List<CallHistory> CallHistories { get; init; }
}

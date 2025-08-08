using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Interface for accessing the results of command execution
///     Allows the caller to check which states were accessed and which events were appended
/// </summary>
public interface ICommandContextResultAccessor
{
    /// <summary>
    ///     Gets the list of tags and their states that were accessed during command execution
    ///     This indicates which states the command depends on
    /// </summary>
    /// <returns>Dictionary of tag to TagState</returns>
    IReadOnlyDictionary<ITag, TagState> GetAccessedTagStates();

    /// <summary>
    ///     Gets all events that were appended during command execution
    /// </summary>
    /// <returns>List of events with tags</returns>
    IReadOnlyList<EventPayloadWithTags> GetAppendedEvents();

    /// <summary>
    ///     Clears all tracked state accesses and appended events
    ///     Used by the command executor after processing
    /// </summary>
    void ClearResults();
}

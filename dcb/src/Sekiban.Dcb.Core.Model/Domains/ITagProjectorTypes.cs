using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Interface for managing tag projector types in the domain
/// </summary>
public interface ITagProjectorTypes
{
    /// <summary>
    ///     Gets a tag projector function by its name
    /// </summary>
    ResultBox<Func<ITagStatePayload, Event, ITagStatePayload>> GetProjectorFunction(string tagProjectorName);

    /// <summary>
    ///     Gets the version of a projector by its name
    /// </summary>
    ResultBox<string> GetProjectorVersion(string tagProjectorName);

    /// <summary>
    ///     Gets all registered tag projector names
    /// </summary>
    IReadOnlyList<string> GetAllProjectorNames();

    /// <summary>
    ///     Tries to find a projector for the given tag group name.
    ///     First looks for exact match "{tagGroupName}Projector", then tries other conventions.
    /// </summary>
    /// <param name="tagGroupName">The tag group name (e.g., "UserMonthlyReservation")</param>
    /// <returns>The projector name if found, null otherwise</returns>
    string? TryGetProjectorForTagGroup(string tagGroupName);
}

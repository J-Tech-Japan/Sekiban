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
}

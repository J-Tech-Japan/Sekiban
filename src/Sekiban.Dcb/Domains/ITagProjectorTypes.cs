using ResultBoxes;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Interface for managing tag projector types in the domain
/// </summary>
public interface ITagProjectorTypes
{
    /// <summary>
    ///     Gets a tag projector instance by its name
    /// </summary>
    ResultBox<ITagProjector> GetTagProjector(string tagProjectorName);
    
    /// <summary>
    ///     Gets the registered name for a projector type
    /// </summary>
    /// <param name="projectorType">The type of the projector</param>
    /// <returns>The registered name of the projector, or null if not registered</returns>
    string? GetProjectorName(Type projectorType);
}

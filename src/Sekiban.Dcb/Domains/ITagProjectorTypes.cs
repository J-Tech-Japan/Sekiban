using Sekiban.Dcb.Tags;
using ResultBoxes;

namespace Sekiban.Dcb.Domains;

/// <summary>
/// Interface for managing tag projector types in the domain
/// </summary>
public interface ITagProjectorTypes
{
    /// <summary>
    /// Gets a tag projector instance by its name
    /// </summary>
    ResultBox<ITagProjector> GetTagProjector(string tagProjectorName);
}
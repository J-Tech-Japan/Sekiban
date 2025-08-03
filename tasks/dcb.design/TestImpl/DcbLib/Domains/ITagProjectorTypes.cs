using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Domains;

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
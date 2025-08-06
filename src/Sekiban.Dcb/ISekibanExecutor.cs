using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb;

/// <summary>
/// Main executor interface for Sekiban DCB
/// Combines command execution with tag state retrieval capabilities
/// </summary>
public interface ISekibanExecutor : ICommandExecutor
{
    /// <summary>
    /// Get the current state for a specific tag
    /// </summary>
    /// <param name="tag">The tag to get state for</param>
    /// <returns>ResultBox containing the tag state or error</returns>
    Task<ResultBox<TagState>> GetTagStateAsync(ITag tag);
}
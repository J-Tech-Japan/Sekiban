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
    /// Get the current state for a specific tag state
    /// </summary>
    /// <param name="tagStateId">The tag state identifier</param>
    /// <returns>ResultBox containing the tag state or error</returns>
    Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId);
}
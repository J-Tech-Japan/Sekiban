using ResultBoxes;

namespace Sekiban.Dcb.Tags;

/// <summary>
/// Interface for persisting tag state
/// Allows different implementations (InMemory, Orleans, Dapr) to share the same actor logic
/// </summary>
public interface ITagStatePersistent
{
    /// <summary>
    /// Loads the cached tag state
    /// </summary>
    /// <returns>The cached tag state, or null if not cached</returns>
    Task<TagState?> LoadStateAsync();
    
    /// <summary>
    /// Saves the tag state to cache/storage
    /// </summary>
    /// <param name="state">The state to save</param>
    Task SaveStateAsync(TagState state);
    
    /// <summary>
    /// Clears the cached state
    /// </summary>
    Task ClearStateAsync();
}
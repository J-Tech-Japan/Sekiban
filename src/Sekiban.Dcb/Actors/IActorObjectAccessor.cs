using ResultBoxes;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Interface for accessing actor objects by their ID
///     Provides a unified way to retrieve actors implementing specific interfaces
/// </summary>
public interface IActorObjectAccessor
{
    /// <summary>
    ///     Gets an actor object that implements the specified interface
    /// </summary>
    /// <typeparam name="T">The interface type the actor must implement</typeparam>
    /// <param name="actorId">The unique identifier of the actor</param>
    /// <returns>A ResultBox containing the actor object or an error</returns>
    Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class;

    /// <summary>
    ///     Checks if an actor with the specified ID exists
    /// </summary>
    /// <param name="actorId">The unique identifier of the actor</param>
    /// <returns>True if the actor exists, false otherwise</returns>
    Task<bool> ActorExistsAsync(string actorId);
}

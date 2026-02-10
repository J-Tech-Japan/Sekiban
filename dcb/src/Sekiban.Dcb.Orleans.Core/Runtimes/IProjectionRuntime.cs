using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Multi-projection execution runtime.
///     Both C# native and WASM implementations implement this interface.
/// </summary>
public interface IProjectionRuntime
{
    ResultBox<IProjectionState> GenerateInitialState(string projectorName);
    ResultBox<string> GetProjectorVersion(string projectorName);
    IReadOnlyList<string> GetAllProjectorNames();

    ResultBox<IProjectionState> ApplyEvent(
        string projectorName,
        IProjectionState currentState,
        Event ev,
        string safeWindowThreshold);

    ResultBox<IProjectionState> ApplyEvents(
        string projectorName,
        IProjectionState currentState,
        IReadOnlyList<Event> events,
        string safeWindowThreshold);

    Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider);

    Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider);

    ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state);

    ResultBox<IProjectionState> DeserializeState(
        string projectorName,
        byte[] data,
        string safeWindowThreshold);

    ResultBox<string> ResolveProjectorName(IQueryCommon query);
    ResultBox<string> ResolveProjectorName(IListQueryCommon query);
}

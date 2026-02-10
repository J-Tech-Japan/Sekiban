using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Composite IProjectionRuntime that routes projectorName through an IProjectorRuntimeResolver.
///     This allows mixing native and WASM runtimes for different projectors.
/// </summary>
public class CompositeProjectionRuntime : IProjectionRuntime
{
    private readonly IProjectorRuntimeResolver _resolver;

    public CompositeProjectionRuntime(IProjectorRuntimeResolver resolver)
    {
        _resolver = resolver;
    }

    public ResultBox<IProjectionState> GenerateInitialState(string projectorName) =>
        _resolver.Resolve(projectorName).GenerateInitialState(projectorName);

    public ResultBox<string> GetProjectorVersion(string projectorName) =>
        _resolver.Resolve(projectorName).GetProjectorVersion(projectorName);

    public IReadOnlyList<string> GetAllProjectorNames()
    {
        var allNames = new List<string>();
        foreach (var runtime in _resolver.GetAllRuntimes())
        {
            allNames.AddRange(runtime.GetAllProjectorNames());
        }
        return allNames;
    }

    public ResultBox<IProjectionState> ApplyEvent(
        string projectorName,
        IProjectionState currentState,
        Event ev,
        string safeWindowThreshold) =>
        _resolver.Resolve(projectorName)
            .ApplyEvent(projectorName, currentState, ev, safeWindowThreshold);

    public ResultBox<IProjectionState> ApplyEvents(
        string projectorName,
        IProjectionState currentState,
        IReadOnlyList<Event> events,
        string safeWindowThreshold) =>
        _resolver.Resolve(projectorName)
            .ApplyEvents(projectorName, currentState, events, safeWindowThreshold);

    public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider) =>
        _resolver.Resolve(projectorName)
            .ExecuteQueryAsync(projectorName, state, query, serviceProvider);

    public Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query,
        IServiceProvider serviceProvider) =>
        _resolver.Resolve(projectorName)
            .ExecuteListQueryAsync(projectorName, state, query, serviceProvider);

    public ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state) =>
        _resolver.Resolve(projectorName).SerializeState(projectorName, state);

    public ResultBox<IProjectionState> DeserializeState(
        string projectorName,
        byte[] data,
        string safeWindowThreshold) =>
        _resolver.Resolve(projectorName)
            .DeserializeState(projectorName, data, safeWindowThreshold);

    public ResultBox<string> ResolveProjectorName(IQueryCommon query)
    {
        foreach (var runtime in _resolver.GetAllRuntimes())
        {
            var result = runtime.ResolveProjectorName(query);
            if (result.IsSuccess) return result;
        }
        return ResultBox.Error<string>(
            new InvalidOperationException(
                $"No runtime can resolve projector for query '{query.GetType().Name}'"));
    }

    public ResultBox<string> ResolveProjectorName(IListQueryCommon query)
    {
        foreach (var runtime in _resolver.GetAllRuntimes())
        {
            var result = runtime.ResolveProjectorName(query);
            if (result.IsSuccess) return result;
        }
        return ResultBox.Error<string>(
            new InvalidOperationException(
                $"No runtime can resolve projector for list query '{query.GetType().Name}'"));
    }
}

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Default implementation of IProjectorRuntimeResolver.
///     Routes projector names to the appropriate IProjectionRuntime using a dictionary,
///     with a default runtime for unregistered projectors.
///     Immutable after construction.
/// </summary>
public class ProjectorRuntimeResolver : IProjectorRuntimeResolver
{
    private readonly IReadOnlyDictionary<string, IProjectionRuntime> _runtimeMap;
    private readonly IProjectionRuntime _defaultRuntime;

    public ProjectorRuntimeResolver(
        IProjectionRuntime defaultRuntime,
        Dictionary<string, IProjectionRuntime>? runtimeMap = null)
    {
        _defaultRuntime = defaultRuntime;
        _runtimeMap = runtimeMap != null
            ? new Dictionary<string, IProjectionRuntime>(runtimeMap)
            : new Dictionary<string, IProjectionRuntime>();
    }

    public IProjectionRuntime Resolve(string projectorName)
    {
        if (_runtimeMap.TryGetValue(projectorName, out var runtime))
        {
            return runtime;
        }
        return _defaultRuntime;
    }

    public IEnumerable<IProjectionRuntime> GetAllRuntimes()
    {
        var runtimes = new HashSet<IProjectionRuntime>(_runtimeMap.Values) { _defaultRuntime };
        return runtimes;
    }
}

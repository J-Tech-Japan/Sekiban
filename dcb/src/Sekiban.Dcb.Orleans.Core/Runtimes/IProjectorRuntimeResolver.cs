namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Routes projectorName to the appropriate IProjectionRuntime.
/// </summary>
public interface IProjectorRuntimeResolver
{
    IProjectionRuntime Resolve(string projectorName);
    IEnumerable<IProjectionRuntime> GetAllRuntimes();
}

using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Actors;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Factory for creating IProjectionActorHost instances.
///     Resolved via DI. The native factory captures DcbDomainTypes and IServiceProvider internally;
///     the Grain never sees them.
/// </summary>
public interface IProjectionActorHostFactory
{
    IProjectionActorHost Create(
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null);
}

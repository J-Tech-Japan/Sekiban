using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Actors;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# factory for creating NativeProjectionActorHost instances.
///     Captures DcbDomainTypes and IServiceProvider via DI constructor injection;
///     the Grain never sees these dependencies.
/// </summary>
public class NativeProjectionActorHostFactory : IProjectionActorHostFactory
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IServiceProvider _serviceProvider;

    public NativeProjectionActorHostFactory(
        DcbDomainTypes domainTypes,
        IServiceProvider serviceProvider)
    {
        _domainTypes = domainTypes;
        _serviceProvider = serviceProvider;
    }

    public IProjectionActorHost Create(
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null)
    {
        return new NativeProjectionActorHost(
            _domainTypes,
            _serviceProvider,
            projectorName,
            options,
            logger);
    }
}

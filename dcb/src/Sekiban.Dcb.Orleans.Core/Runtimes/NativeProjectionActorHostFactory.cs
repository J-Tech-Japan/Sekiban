using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Actors;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# factory for creating NativeProjectionActorHost instances.
///     Captures DcbDomainTypes, IServiceProvider, and NativeMultiProjectionProjectionPrimitive
///     via DI constructor injection; the Grain never sees these dependencies.
/// </summary>
public class NativeProjectionActorHostFactory : IProjectionActorHostFactory
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly NativeMultiProjectionProjectionPrimitive _primitive;

    public NativeProjectionActorHostFactory(
        DcbDomainTypes domainTypes,
        IServiceProvider serviceProvider,
        NativeMultiProjectionProjectionPrimitive primitive)
    {
        _domainTypes = domainTypes;
        _serviceProvider = serviceProvider;
        _primitive = primitive;
    }

    public IProjectionActorHost Create(
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null)
    {
        return new NativeProjectionActorHost(
            _domainTypes,
            _serviceProvider,
            _primitive,
            projectorName,
            options,
            logger);
    }
}

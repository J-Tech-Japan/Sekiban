using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleProjections;
namespace Sekiban.Testing.Projection;

public class
    MultiProjectionTestBase<TProjection, TProjectionPayload, TDependencyDefinition> : CommonMultiProjectionTestBase<
        TProjection, TProjectionPayload, TDependencyDefinition> where TProjection : MultiProjectionBase<TProjectionPayload>, new()
    where TProjectionPayload : IMultiProjectionPayload, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    public MultiProjectionTestBase()
    {
    }
    public MultiProjectionTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public sealed override IMultiProjectionTestHelper<TProjection, TProjectionPayload> WhenProjection()
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider not set");
        }
        var multipleProjectionService
            = _serviceProvider.GetRequiredService(typeof(IMultiProjectionService)) as IMultiProjectionService;
        if (multipleProjectionService is null) { throw new Exception("Failed to get multipleProjectionService "); }
        try
        {
            State = multipleProjectionService.GetMultiProjectionAsync<TProjection, TProjectionPayload>().Result;
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        ;
        foreach (var checker in _queryCheckers)
        {
            checker.RegisterState(State);
        }
        return this;
    }
}

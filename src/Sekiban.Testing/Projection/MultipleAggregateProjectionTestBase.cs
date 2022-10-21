using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query.MultipleAggregate;
namespace Sekiban.Testing.Projection;

public class
    MultipleAggregateProjectionTestBase<TProjection, TProjectionContents, TDependencyDefinition> : CommonMultipleAggregateProjectionTestBase<
        TProjection, TProjectionContents, TDependencyDefinition> where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
    where TDependencyDefinition : IDependencyDefinition, new()
{
    public MultipleAggregateProjectionTestBase()
    {
    }
    public MultipleAggregateProjectionTestBase(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    public sealed override IMultipleAggregateProjectionTestHelper<TProjection, TProjectionContents> WhenProjection()
    {
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("Service provider not set");
        }
        var multipleProjectionService
            = _serviceProvider.GetRequiredService(typeof(IMultipleAggregateProjectionService)) as IMultipleAggregateProjectionService;
        if (multipleProjectionService is null) { throw new Exception("Failed to get multipleProjectionService "); }
        try
        {
            Dto = multipleProjectionService.GetProjectionAsync<TProjection, TProjectionContents>().Result;
        }
        catch (Exception ex)
        {
            _latestException = ex;
            return this;
        }
        ;
        foreach (var checker in _queryFilterCheckers)
        {
            checker.RegisterDto(Dto);
        }
        return this;
    }
}

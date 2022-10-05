using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public class
    MultipleAggregateProjectionTestBase<TProjection, TProjectionContents> : CommonMultipleAggregateProjectionTestBase<TProjection,
        TProjectionContents> where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()
{
    public MultipleAggregateProjectionTestBase(SekibanDependencyOptions options) : base(options)
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

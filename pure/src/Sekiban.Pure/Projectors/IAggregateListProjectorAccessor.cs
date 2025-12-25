using Sekiban.Pure.Aggregates;
namespace Sekiban.Pure.Projectors;

public interface IAggregateListProjectorAccessor
{
    public IList<Aggregate> GetAggregates();
}

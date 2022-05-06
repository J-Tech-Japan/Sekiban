namespace Sekiban.EventSourcing.Queries;

public interface IMultipleAggregateProjection<TParameter> : IProjection where TParameter : IMultipleAggregateParameter
{
    public TParameter Parameter { get; set; }
    void ApplyEvent(AggregateEvent ev);
}

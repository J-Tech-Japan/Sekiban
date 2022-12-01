using System.Reflection;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Types;

namespace Sekiban.Core.Aggregate;

public class SekibanAggregateTypes
{
    private readonly List<SingleProjectionAggregateType> _registeredCustomProjectorTypes = new();
    private readonly List<DefaultAggregateType> _registeredTypes = new();

    public SekibanAggregateTypes(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var aggregates = assembly.DefinedTypes.GetAggregateTypes();

            foreach (var type in aggregates)
            {
                var baseProjector = typeof(DefaultSingleProjector<>);
                var p = baseProjector.MakeGenericType(type);
                var aggregateType = new DefaultAggregateType(type, p);
                if (_registeredTypes.Contains(aggregateType)) continue;
                _registeredTypes.Add(aggregateType);
            }

            var customProjectors = assembly.DefinedTypes.GetSingleProjectorTypes();
            foreach (var type in customProjectors)
            {
                var projectorType = new SingleProjectionAggregateType(
                    type.GetOriginalTypeFromSingleProjectionPayload(),
                    type.GetProjectionTypeFromSingleProjection(),
                    type);
                if (_registeredCustomProjectorTypes.Contains(projectorType)) continue;
                _registeredCustomProjectorTypes.Add(projectorType);
            }
        }

        AggregateTypes = _registeredTypes.AsReadOnly();
        SingleProjectionTypes = _registeredCustomProjectorTypes.AsReadOnly();
    }

    public IReadOnlyCollection<DefaultAggregateType> AggregateTypes { get; }
    public IReadOnlyCollection<SingleProjectionAggregateType> SingleProjectionTypes { get; }

    public record DefaultAggregateType(Type Aggregate, Type Projection);

    public record SingleProjectionAggregateType(Type Aggregate, Type Projection, Type PayloadType) :
        DefaultAggregateType(
            Aggregate,
            Projection);
}

using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     System use Aggregate type list
///     Application developer can define your context types by inheriting from DomainDependencyDefinitionBase
/// </summary>
public class SekibanAggregateTypes
{
    private readonly List<SingleProjectionAggregateType> _registeredCustomProjectorTypes = [];
    private readonly List<DefaultAggregateType> _registeredTypes = [];

    public IReadOnlyCollection<DefaultAggregateType> AggregateTypes { get; }
    public IReadOnlyCollection<SingleProjectionAggregateType> SingleProjectionTypes { get; }

    public SekibanAggregateTypes(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var aggregates = assembly.DefinedTypes.GetAggregatePayloadWithoutSubtypeTypes();

            foreach (var type in aggregates)
            {
                var aggregateType = new DefaultAggregateType(type);
                if (_registeredTypes.Contains(aggregateType))
                {
                    continue;
                }
                _registeredTypes.Add(aggregateType);
            }

            var customProjectors = assembly.DefinedTypes.GetSingleProjectorTypes();
            foreach (var type in customProjectors)
            {
                var projectorType = new SingleProjectionAggregateType(type.GetAggregatePayloadTypeFromSingleProjectionPayload(), type);
                if (_registeredCustomProjectorTypes.Contains(projectorType))
                {
                    continue;
                }
                _registeredCustomProjectorTypes.Add(projectorType);
            }
        }

        AggregateTypes = _registeredTypes.AsReadOnly();
        SingleProjectionTypes = _registeredCustomProjectorTypes.AsReadOnly();
    }

    public record DefaultAggregateType(Type Aggregate);

    public record SingleProjectionAggregateType(Type OriginalAggregate, Type SingleProjectionPayloadType);
}

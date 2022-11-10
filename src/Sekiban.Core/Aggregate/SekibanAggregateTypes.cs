using Sekiban.Core.Query.SingleProjections;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

public class SekibanAggregateTypes
{
    private readonly List<SingleProjectionAggregateType> _registeredCustomProjectorTypes = new();
    private readonly List<DefaultAggregateType> _registeredTypes = new();
    public SekibanAggregateTypes(params Assembly[] assemblies)
    {
        var attributeType = typeof(IAggregatePayload);
        foreach (var assembly in assemblies)
        {
            var aggregates = assembly.DefinedTypes.Where(
                x => x.IsClass &&
                    x.ImplementedInterfaces.Contains(attributeType) &&
                    !x.ImplementedInterfaces.Contains(typeof(ISingleProjectionPayload)));

            foreach (var type in aggregates)
            {
                var baseProjector = typeof(DefaultSingleProjector<>);
                var p = baseProjector.MakeGenericType(type);
                _registeredTypes.Add(new DefaultAggregateType(type, p));
            }
            var projectorBase = typeof(SingleProjectionPayloadBase<,>);
            var projectorBaseDeletable = typeof(DeletableSingleProjectionPayloadBase<,>);
            var customProjectors = assembly.DefinedTypes.Where(
                x => x.IsClass &&
                    new[] { projectorBase.Name, projectorBaseDeletable.Name }.Contains(x.BaseType?.Name) &&
                    !x.IsGenericType &&
                    x.BaseType?.Namespace == projectorBase.Namespace);
            foreach (var type in customProjectors)
            {
                var baseType = type.BaseType;
                if (baseType is null) { continue; }
                var singleProjectionBase = typeof(SingleProjection<>);
                var singleProjection = singleProjectionBase.MakeGenericType(type);
                var original = baseType.GenericTypeArguments[0];
                _registeredCustomProjectorTypes.Add(new SingleProjectionAggregateType(original, singleProjection, type));
            }
        }
        AggregateTypes = _registeredTypes.AsReadOnly();
        SingleProjectionTypes = _registeredCustomProjectorTypes.AsReadOnly();
    }

    public IReadOnlyCollection<DefaultAggregateType> AggregateTypes { get; }
    public IReadOnlyCollection<SingleProjectionAggregateType> SingleProjectionTypes { get; }
    public record DefaultAggregateType(Type Aggregate, Type Projection);
    public record SingleProjectionAggregateType(Type Aggregate, Type Projection, Type PayloadType) : DefaultAggregateType(
        Aggregate,
        Projection);
}

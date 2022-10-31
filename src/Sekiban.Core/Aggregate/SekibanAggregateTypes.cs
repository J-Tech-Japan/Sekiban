using Sekiban.Core.Query.SingleProjections;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

public class SekibanAggregateTypes
{
    private readonly List<ProjectionAggregateType> _registeredCustomProjectorTypes = new();
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
            var projectorBase = typeof(SingleProjectionBase<,,>);
            var customProjectors = assembly.DefinedTypes.Where(
                x => x.IsClass && x.BaseType?.Name == projectorBase.Name && x.BaseType?.Namespace == projectorBase.Namespace);
            foreach (var type in customProjectors)
            {
                var baseType = type.BaseType;
                if (baseType is null) { continue; }
                var projectionPayloadType = baseType.GenericTypeArguments[2];
                var instance = (dynamic?)Activator.CreateInstance(type);
                var original = instance?.OriginalAggregateType();
                if (original is null) { continue; }
                _registeredCustomProjectorTypes.Add(new ProjectionAggregateType(original, type, projectionPayloadType));
            }
        }
        AggregateTypes = _registeredTypes.AsReadOnly();
        ProjectionAggregateTypes = _registeredCustomProjectorTypes.AsReadOnly();
    }

    public IReadOnlyCollection<DefaultAggregateType> AggregateTypes { get; }
    public IReadOnlyCollection<ProjectionAggregateType> ProjectionAggregateTypes { get; }
    public record DefaultAggregateType(Type Aggregate, Type Projection);
    public record ProjectionAggregateType(Type Aggregate, Type Projection, Type PayloadType) : DefaultAggregateType(
        Aggregate,
        Projection);
}

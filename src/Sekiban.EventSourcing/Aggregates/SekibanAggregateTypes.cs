using Sekiban.EventSourcing.Queries.SingleAggregates;
using System.Reflection;
namespace Sekiban.EventSourcing.Aggregates;

public class SekibanAggregateTypes
{
    private readonly List<ProjectionAggregateType> _registeredCustomProjectorTypes = new();
    private readonly List<DefaultAggregateType> _registeredTypes = new();

    public IReadOnlyCollection<DefaultAggregateType> AggregateTypes { get; }
    public IReadOnlyCollection<ProjectionAggregateType> ProjectionAggregateTypes { get; }
    public SekibanAggregateTypes(params Assembly[] assemblies)
    {
        var attributeType = typeof(TransferableAggregateBase<>);
        foreach (var assembly in assemblies)
        {
            var aggregates = assembly.DefinedTypes.Where(
                x => x.IsClass && x.BaseType?.Name == attributeType.Name && x.BaseType?.Namespace == attributeType.Namespace);

            foreach (var type in aggregates)
            {
                var dto = type.BaseType?.GetGenericArguments()?.First();
                if (dto == null) { continue; }
                var baseProjector = typeof(DefaultSingleAggregateProjector<>);
                var p = baseProjector.MakeGenericType(type);
                _registeredTypes.Add(new DefaultAggregateType(type, dto, p));
            }
            var projectorBase = typeof(SingleAggregateProjectionBase<>);
            var customProjectors = assembly.DefinedTypes.Where(
                x => x.IsClass && x.BaseType?.Name == projectorBase.Name && x.BaseType?.Namespace == projectorBase.Namespace);
            foreach (var type in customProjectors)
            {
                var instance = (dynamic?)Activator.CreateInstance(type);
                var original = instance?.OriginalAggregateType();
                if (original == null) { continue; }
                _registeredCustomProjectorTypes.Add(new ProjectionAggregateType(type, type, type, original));
            }
        }
        AggregateTypes = _registeredTypes.AsReadOnly();
        ProjectionAggregateTypes = _registeredCustomProjectorTypes.AsReadOnly();
    }
    public record DefaultAggregateType(Type Aggregate, Type Dto, Type Projection);
    public record ProjectionAggregateType(Type Aggregate, Type Dto, Type Projection, Type OriginalType) : DefaultAggregateType(
        Aggregate,
        Dto,
        Projection);
}
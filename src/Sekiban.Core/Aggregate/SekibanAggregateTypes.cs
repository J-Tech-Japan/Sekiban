using Sekiban.Core.Query.SingleAggregate;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

public class SekibanAggregateTypes
{
    private readonly List<ProjectionAggregateType> _registeredCustomProjectorTypes = new();
    private readonly List<DefaultAggregateType> _registeredTypes = new();

    public IReadOnlyCollection<DefaultAggregateType> AggregateTypes { get; }
    public IReadOnlyCollection<ProjectionAggregateType> ProjectionAggregateTypes { get; }
    public SekibanAggregateTypes(params Assembly[] assemblies)
    {
        var attributeType = typeof(AggregateBase<>);
        foreach (var assembly in assemblies)
        {
            var aggregates = assembly.DefinedTypes.Where(
                x => x.IsClass && x.BaseType?.Name == attributeType.Name && x.BaseType?.Namespace == attributeType.Namespace);

            foreach (var type in aggregates)
            {
                var dto = type.BaseType?.GetGenericArguments()?.First();
                if (dto is null) { continue; }
                var baseProjector = typeof(DefaultSingleAggregateProjector<>);
                var p = baseProjector.MakeGenericType(type);
                _registeredTypes.Add(new DefaultAggregateType(type, dto, p));
            }
            var projectorBase = typeof(SingleAggregateProjectionBase<,,>);
            var customProjectors = assembly.DefinedTypes.Where(
                x => x.IsClass && x.BaseType?.Name == projectorBase.Name && x.BaseType?.Namespace == projectorBase.Namespace);
            foreach (var type in customProjectors)
            {
                var baseType = type.BaseType;
                if (baseType is null) { continue; }
                var tProjectionContents = baseType.GenericTypeArguments[2];
                var instance = (dynamic?)Activator.CreateInstance(type);
                var original = instance?.OriginalAggregateType();
                if (original is null) { continue; }
                _registeredCustomProjectorTypes.Add(new ProjectionAggregateType(type, tProjectionContents, type, original));
            }
        }
        AggregateTypes = _registeredTypes.AsReadOnly();
        ProjectionAggregateTypes = _registeredCustomProjectorTypes.AsReadOnly();
    }
    public record DefaultAggregateType(Type Aggregate, Type Dto, Type Projection);
    public record ProjectionAggregateType(Type Aggregate, Type DtoContents, Type Projection, Type OriginalType) : DefaultAggregateType(
        Aggregate,
        DtoContents,
        Projection);
}

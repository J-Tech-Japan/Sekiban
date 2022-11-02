using Sekiban.Core.Query.MultProjections;
namespace Sekiban.Core.Aggregate;

[AttributeUsage(AttributeTargets.Class)]
public class AggregateContainerGroupAttribute : Attribute
{
    public AggregateContainerGroupAttribute(AggregateContainerGroup group = AggregateContainerGroup.Default) => Group = group;
    public AggregateContainerGroup Group { get; init; }
    public static AggregateContainerGroup FindAggregateContainerGroup(Type type)
    {
        if (type.CustomAttributes.Any(a => a.AttributeType == typeof(AggregateContainerGroupAttribute)))
        {
            var attributes = (AggregateContainerGroupAttribute[])type.GetCustomAttributes(typeof(AggregateContainerGroupAttribute), true);
            var max = attributes.Max(m => m.Group);
            return max;
        }
        if (type.Name.Equals(typeof(SingleProjectionListProjector<,,>).Name))
        {
            var projectorType = type.GetGenericArguments()[2];
            var projector = Activator.CreateInstance(projectorType) as dynamic;
            var aggregateType = projector?.OriginalAggregateType() as Type;
            if (aggregateType == null)
            {
                return AggregateContainerGroup.Default;
            }
            return FindAggregateContainerGroup(aggregateType);
        }
        return AggregateContainerGroup.Default;
    }
}
public enum AggregateContainerGroup
{
    Default = 0, Dissolvable = 1, InMemoryContainer = 10
}

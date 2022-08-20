using Sekiban.EventSourcing.Queries.MultipleAggregates;
namespace Sekiban.EventSourcing.Aggregates
{
    [AttributeUsage(AttributeTargets.Class)]
    public class AggregateContainerGroupAttribute : Attribute
    {
        public AggregateContainerGroup Group { get; init; }
        public AggregateContainerGroupAttribute(AggregateContainerGroup group = AggregateContainerGroup.Default) =>
            Group = group;
        public static AggregateContainerGroup FindAggregateContainerGroup(Type type)
        {
            if (type.CustomAttributes.Any(a => a.AttributeType == typeof(AggregateContainerGroupAttribute)))
            {
                var attributes = (AggregateContainerGroupAttribute[])type.GetCustomAttributes(typeof(AggregateContainerGroupAttribute), true);
                var max = attributes.Max(m => m.Group);
                return max;
            }
            if (type.Name.Equals(typeof(SingleAggregateListProjector<,,>).Name))
            {
                return FindAggregateContainerGroup(type.GenericTypeArguments.First());
            }
            return AggregateContainerGroup.Default;
        }
    }
    public enum AggregateContainerGroup
    {
        Default = 0,
        Dissolvable = 1,
        InMemoryContainer = 10
    }
}

using Sekiban.Core.Types;

namespace Sekiban.Core.Aggregate;

[AttributeUsage(AttributeTargets.Class)]
public class AggregateContainerGroupAttribute : Attribute
{
    public AggregateContainerGroupAttribute(AggregateContainerGroup group = AggregateContainerGroup.Default)
    {
        Group = group;
    }

    public AggregateContainerGroup Group { get; init; }

    public static AggregateContainerGroup FindAggregateContainerGroup(Type type)
    {
        if (type.CustomAttributes.All(a => a.AttributeType != typeof(AggregateContainerGroupAttribute)))
        {
            if (type.IsSingleProjectionPayloadType())
                return FindAggregateContainerGroup(type.GetOriginalTypeFromSingleProjectionPayload());
            if (type.IsSingleProjectorType())
                return FindAggregateContainerGroup(type.GetOriginalAggregateTypeFromSingleProjectionListProjector());

            return AggregateContainerGroup.Default;
        }

        var attributes =
            (AggregateContainerGroupAttribute[])type.GetCustomAttributes(typeof(AggregateContainerGroupAttribute),
                true);
        var max = attributes.Max(m => m.Group);
        return max;
    }
}

public enum AggregateContainerGroup
{
    Default = 0,
    Dissolvable = 1,
    InMemoryContainer = 10
}

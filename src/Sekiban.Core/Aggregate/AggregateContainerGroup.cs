using Sekiban.Core.Types;
namespace Sekiban.Core.Aggregate;

/// <summary>
///     defines the container that aggregate and projections uses
///     to use : apply this attribute to AggregatePayload or ProjectionPayload
///     [AggregateContainerGroup(AggregateContainerGroup.InMemory)]
///     public record RecentInMemoryActivity(...) : IAggregatePayload
///     Default : DataStore - Regular Container
///     Dissolvable : DataStore - Dissolvable Container
///     InMemory : InMemory - only lives during runtime
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class AggregateContainerGroupAttribute : Attribute
{

    public AggregateContainerGroup Group { get; init; }
    public AggregateContainerGroupAttribute(AggregateContainerGroup group = AggregateContainerGroup.Default) => Group = group;

    public static AggregateContainerGroup FindAggregateContainerGroup(Type type)
    {
        while (true)
        {
            if (type.CustomAttributes.All(a => a.AttributeType != typeof(AggregateContainerGroupAttribute)))
            {
                if (type.IsSingleProjectionPayloadType())
                {
                    type = type.GetOriginalTypeFromSingleProjectionPayload();
                    continue;
                }
                if (type.IsSingleProjectorType())
                {
                    type = type.GetOriginalAggregatePayloadTypeFromSingleProjectionListProjector();
                    continue;
                }
                if (!type.IsMultiProjectionType())
                {
                    return AggregateContainerGroup.Default;
                }
                type = type.GetMultiProjectionPayloadTypeFromMultiProjection();
                continue;
            }

            var attributes = (AggregateContainerGroupAttribute[])type.GetCustomAttributes(typeof(AggregateContainerGroupAttribute), true);
            var max = attributes.Max(m => m.Group);
            return max;
        }
    }
}
public enum AggregateContainerGroup
{
    Default = 0, Dissolvable = 1, InMemory = 10
}

using ResultBoxes;
namespace Sekiban.Pure.Aggregates;

public interface IAggregateTypes
{
    public ResultBox<IAggregate> ToTypedPayload(Aggregate aggregate);
    public List<Type> GetAggregateTypes();

    /// <summary>
    ///     Gets the type from the payload type name。
    /// </summary>
    /// <param name="payloadTypeName">Name of the payload type</param>
    /// <returns>The found type, or null if not found</returns>
    public Type? GetPayloadTypeByName(string payloadTypeName);
}

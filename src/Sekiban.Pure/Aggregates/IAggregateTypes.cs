using ResultBoxes;
namespace Sekiban.Pure.Aggregates;

public interface IAggregateTypes
{
    public ResultBox<IAggregate> ToTypedPayload(Aggregate aggregate);
    public List<Type> GetAggregateTypes();
    
    /// <summary>
    /// ペイロード型名から型を取得します。
    /// </summary>
    /// <param name="payloadTypeName">ペイロード型の名前</param>
    /// <returns>見つかった型、または見つからない場合はnull</returns>
    public Type? GetPayloadTypeByName(string payloadTypeName);
}

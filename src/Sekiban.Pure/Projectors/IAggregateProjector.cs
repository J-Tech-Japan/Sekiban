using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using System;
namespace Sekiban.Pure.Projectors;

public interface IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev);
    public virtual string GetVersion() => "initial";
    
    /// <summary>
    /// 型名からペイロードの型を取得します。
    /// </summary>
    /// <param name="payloadTypeName">ペイロード型の名前</param>
    /// <returns>見つかった型、または見つからない場合はnull</returns>
    public virtual Type? GetPayloadTypeByName(string payloadTypeName)
    {
        // 実装クラスのアセンブリ内で型名を検索
        return GetType().Assembly.GetTypes()
            .FirstOrDefault(t => t.Name == payloadTypeName && 
                            typeof(IAggregatePayload).IsAssignableFrom(t));
    }
}

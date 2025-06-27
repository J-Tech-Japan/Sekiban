using Dapr.Actors;

namespace DaprSample2;

/// <summary>
/// カウンターActorのインターフェース
/// </summary>
public interface ICounterActor : IActor
{
    /// <summary>
    /// カウンターの値を取得
    /// </summary>
    Task<int> GetCountAsync();

    /// <summary>
    /// カウンターをインクリメント
    /// </summary>
    Task IncrementAsync();

    /// <summary>
    /// カウンターをリセット
    /// </summary>
    Task ResetAsync();
}

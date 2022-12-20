namespace Sekiban.Core.Setting;

public interface IAggregateSettings
{
    public bool ShouldTakeSnapshotForType(Type aggregatePayloadType);

    /// <summary>
    ///     タイプがハイブリッドを使うことができるか？
    ///     ハイブリッドを使うと、生成したイベントをインメモリにも保管する
    ///     使うことができるのは同じインスタンスで全てのイベントが発生することが確定している集約のみ
    /// </summary>
    /// <param name="aggregatePayloadType"></param>
    /// <returns></returns>
    public bool CanUseHybrid(Type aggregatePayloadType);

    public int SnapshotFrequencyForType(Type aggregatePayloadType);
    public int SnapshotOffsetForType(Type aggregatePayloadType);
    public bool UseUpdateMarkerForType(string aggregatePayloadTypeName);
}

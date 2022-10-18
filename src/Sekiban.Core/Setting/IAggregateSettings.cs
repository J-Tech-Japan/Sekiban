namespace Sekiban.Core.Setting;

public interface IAggregateSettings
{
    public bool ShouldTakeSnapshotForType(Type originalType);
    /// <summary>
    ///     タイプがハイブリッドを使うことができるか？
    ///     ハイブリッドを使うと、生成したイベントをインメモリにも保管する
    ///     使うことができるのは同じインスタンスで全てのイベントが発生することが確定している集約のみ
    /// </summary>
    /// <param name="originalType"></param>
    /// <returns></returns>
    public bool CanUseHybrid(Type originalType);
    public int SnapshotFrequencyForType(Type originalType);
    public int SnapshotOffsetForType(Type originalType);
    public bool UseUpdateMarkerForType(string originalType);
}

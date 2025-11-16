namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     マルチプロジェクション状態をシリアライズ可能な形で保持するコア領域専用のレコードです。
/// </summary>
public record SerializableMultiProjectionState
{
    public byte[] Payload { get; init; }
    public string MultiProjectionPayloadType { get; init; }
    public string ProjectorName { get; init; }
    public string ProjectorVersion { get; init; }
    public string LastSortableUniqueId { get; init; }
    public Guid LastEventId { get; init; }
    public int Version { get; init; }
    public bool IsCatchedUp { get; init; } = true;
    public bool IsSafeState { get; init; } = true;

    public SerializableMultiProjectionState(
        byte[] payload,
        string multiProjectionPayloadType,
        string projectorName,
        string projectorVersion,
        string lastSortableUniqueId,
        Guid lastEventId,
        int version,
        bool isCatchedUp = true,
        bool isSafeState = true)
    {
        Payload = payload;
        MultiProjectionPayloadType = multiProjectionPayloadType;
        ProjectorName = projectorName;
        ProjectorVersion = projectorVersion;
        LastSortableUniqueId = lastSortableUniqueId;
        LastEventId = lastEventId;
        Version = version;
        IsCatchedUp = isCatchedUp;
        IsSafeState = isSafeState;
    }
}

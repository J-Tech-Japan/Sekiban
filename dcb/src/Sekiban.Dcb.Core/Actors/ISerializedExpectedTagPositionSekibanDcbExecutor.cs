using ResultBoxes;
using Sekiban.Dcb.Commands;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     OPTIONAL additive WASM-boundary capability for the V2, store-enforced expected-tag-position commit envelope.
///     Kept separate from <see cref="ISerializedSekibanDcbExecutor" /> so existing serialized executors remain source and
///     binary compatible; acceptance feature-detects it and fails closed before the old executor or provider write path.
/// </summary>
public interface ISerializedExpectedTagPositionSekibanDcbExecutor
{
    Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsWithExpectedTagPositionsAsync(
        VersionedExpectedTagPositionSerializedCommitRequest request,
        CancellationToken cancellationToken = default);
}

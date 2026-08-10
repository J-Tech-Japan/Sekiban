using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     SEK-G17 default two-phase implementation of <see cref="ISerializedCommitAcceptor" />.
///     <para>
///         Phase 1 (<see cref="SerializedCommitVersionDiscriminator" />) reads the raw <c>version</c> discriminator. An
///         unsupported version fails closed with <see cref="UnsupportedSerializedCommitEnvelopeVersionException" /> and a
///         structurally invalid envelope with <see cref="MalformedSerializedCommitException" /> — BOTH before any typed
///         binding, base64 decode, tag reservation, EventId allocation, or executor/store call.
///     </para>
///     <para>
///         Phase 2 binds only the resolved shape: a missing version is the legacy official shape (lifted losslessly to V1
///         via <see cref="LegacyUnversionedSerializedCommitAdapter" />); a known version binds
///         <see cref="VersionedSerializedCommitRequest" />. Either way the same event candidates + consistency tags are
///         handed to <see cref="ISerializedSekibanDcbExecutor.CommitSerializableEventsAsync" /> with identical semantics
///         (heterogeneous per-event tags preserved). A binding failure becomes a SANITIZED typed shape error — the raw
///         System.Text.Json exception is discarded, never attached — so hostile request content cannot leak through the
///         error surface, and a null-reference is never surfaced.
///     </para>
/// </summary>
public sealed class SerializedCommitAcceptor : ISerializedCommitAcceptor
{
    private readonly ISerializedSekibanDcbExecutor _executor;

    public SerializedCommitAcceptor(ISerializedSekibanDcbExecutor executor) => _executor = executor;

    public Task<ResultBox<SerializedCommitResult>> AcceptAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default)
    {
        // Phase 1: raw version discrimination — no typed binding / no side effect yet.
        var discrimination = SerializedCommitVersionDiscriminator.Read(utf8Json.Span);
        switch (discrimination.Kind)
        {
            case SerializedCommitVersionKind.Malformed:
                return Malformed(discrimination.ShapeError ?? SerializedCommitShapeError.UnreadableJson);

            case SerializedCommitVersionKind.UnsupportedVersion:
                return Task.FromResult(
                    ResultBox.Error<SerializedCommitResult>(
                        new UnsupportedSerializedCommitEnvelopeVersionException(
                            discrimination.Version!.Value, VersionedSerializedCommitRequest.CurrentVersion)));

            case SerializedCommitVersionKind.LegacyUnversioned:
                return BindLegacyThenExecuteAsync(utf8Json, cancellationToken);

            case SerializedCommitVersionKind.KnownVersion:
                return BindVersionedThenExecuteAsync(utf8Json, cancellationToken);

            default:
                return Malformed(SerializedCommitShapeError.UnreadableJson);
        }
    }

    private Task<ResultBox<SerializedCommitResult>> BindLegacyThenExecuteAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken)
    {
        SerializedCommitRequest? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<SerializedCommitRequest>(
                utf8Json.Span, SerializedCommitWireContract.Options);
        }
        catch (JsonException)
        {
            return Malformed(SerializedCommitShapeError.LegacyPayloadInvalid); // raw cause discarded (secret-safe)
        }

        if (legacy is null)
        {
            return Malformed(SerializedCommitShapeError.LegacyPayloadInvalid);
        }
        if (legacy.ConsistencyTags?.Any(entry => entry.LastSortableUniqueId is null) == true)
        {
            return Malformed(SerializedCommitShapeError.LegacyPayloadInvalid);
        }

        // Lift losslessly to V1 (per-event tags preserved) before execution — the legacy path is not a shortcut.
        var envelope = LegacyUnversionedSerializedCommitAdapter.ToVersionedV1(legacy);
        return ExecuteAsync(envelope, cancellationToken);
    }

    private Task<ResultBox<SerializedCommitResult>> BindVersionedThenExecuteAsync(
        ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken)
    {
        VersionedSerializedCommitRequest? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VersionedSerializedCommitRequest>(
                utf8Json.Span, SerializedCommitWireContract.Options);
        }
        catch (JsonException)
        {
            return Malformed(SerializedCommitShapeError.VersionedPayloadInvalid); // raw cause discarded (secret-safe)
        }

        if (envelope is null)
        {
            return Malformed(SerializedCommitShapeError.VersionedPayloadInvalid);
        }
        if (envelope.ConsistencyTags?.Any(entry => entry.LastSortableUniqueId is null) == true)
        {
            return Malformed(SerializedCommitShapeError.VersionedPayloadInvalid);
        }

        return ExecuteAsync(envelope, cancellationToken);
    }

    private Task<ResultBox<SerializedCommitResult>> ExecuteAsync(
        VersionedSerializedCommitRequest envelope,
        CancellationToken cancellationToken)
    {
        // Route to the existing executor with identical semantics. Absent arrays coalesce to empty (a valid empty commit),
        // so a missing collection is never a null-reference failure.
        var request = new SerializedCommitRequest(
            envelope.EventCandidates ?? Array.Empty<SerializableEventCandidate>(),
            envelope.ConsistencyTags ?? Array.Empty<ConsistencyTagEntry>());
        return _executor.CommitSerializableEventsAsync(request, cancellationToken);
    }

    private static Task<ResultBox<SerializedCommitResult>> Malformed(SerializedCommitShapeError reason) =>
        Task.FromResult(ResultBox.Error<SerializedCommitResult>(new MalformedSerializedCommitException(reason)));
}

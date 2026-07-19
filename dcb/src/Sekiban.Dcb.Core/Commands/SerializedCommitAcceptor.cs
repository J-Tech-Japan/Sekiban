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
///         (heterogeneous per-event tags preserved). A binding failure is reported as a typed shape error, never a
///         null-reference.
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
                return Task.FromResult(
                    ResultBox.Error<SerializedCommitResult>(
                        new MalformedSerializedCommitException(discrimination.Detail ?? "invalid envelope")));

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
                return Task.FromResult(
                    ResultBox.Error<SerializedCommitResult>(
                        new MalformedSerializedCommitException("unrecognized discrimination result")));
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
        catch (JsonException ex)
        {
            return Malformed("legacy unversioned payload could not be bound", ex);
        }

        if (legacy is null)
        {
            return Malformed("legacy unversioned payload bound to null");
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
        catch (JsonException ex)
        {
            return Malformed("known-version (V1) payload could not be bound", ex);
        }

        if (envelope is null)
        {
            return Malformed("known-version (V1) payload bound to null");
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

    private static Task<ResultBox<SerializedCommitResult>> Malformed(string detail) =>
        Task.FromResult(ResultBox.Error<SerializedCommitResult>(new MalformedSerializedCommitException(detail)));

    private static Task<ResultBox<SerializedCommitResult>> Malformed(string detail, Exception inner) =>
        Task.FromResult(ResultBox.Error<SerializedCommitResult>(new MalformedSerializedCommitException(detail, inner)));
}

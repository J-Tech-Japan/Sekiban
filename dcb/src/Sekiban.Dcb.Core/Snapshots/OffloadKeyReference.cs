using Sekiban.Dcb.Storage.Checkpoints;

namespace Sekiban.Dcb.Snapshots;

/// <summary>The location of a snapshot reference, or a failure to inspect it.</summary>
public enum OffloadKeyReferenceKind { Row, Envelope, Undecodable }

/// <summary>An observed snapshot reference. Undecodable means a GC must delete nothing.</summary>
public sealed record OffloadKeyReference(
    string ProjectorName,
    string ProjectorVersion,
    OffloadKeyReferenceKind Kind,
    string? OffloadKey,
    string? StorageProvider,
    CheckpointLifecycle? Lifecycle,
    string? Detail);

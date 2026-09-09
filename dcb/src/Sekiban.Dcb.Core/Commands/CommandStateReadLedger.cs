using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Commands;

/// <summary>
///     Attempt-local state-read evidence. It is intentionally internal: the handler can receive state, but cannot
///     manufacture or replace the evidence used by the derived expected-position writer.
/// </summary>
internal sealed class CommandStateReadLedger
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly string _serviceId;

    public CommandStateReadLedger(string serviceId)
    {
        _serviceId = serviceId;
    }

    public void RecordFailure(ITag tag)
    {
        var key = LogicalTag(tag);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry(key, _serviceId);
            _entries.Add(key, entry);
        }

        entry.HadFailedAttempt = true;
    }

    public void RecordSuccess(
        ITag requestedTag,
        SerializableTagState state,
        string expectedProjector,
        string expectedProjectorVersion)
    {
        var key = LogicalTag(requestedTag);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new Entry(key, _serviceId);
            _entries.Add(key, entry);
        }

        var position = state.LastSortedUniqueId;
        if (position is null)
        {
            entry.IsMalformed = true;
            position = string.Empty;
        }

        if (entry.Position is not null && !string.Equals(entry.Position, position, StringComparison.Ordinal))
        {
            entry.HasConflictingPositions = true;
        }
        else
        {
            entry.Position ??= position;
        }

        entry.HadSuccessfulRead = true;
        // Different projectors may legitimately read the same logical tag at the same durable position. Preserve their
        // raw identity/version as validated provenance without making the expected-position decision depend on which
        // projector happened to be read first.
        _ = expectedProjector;
        _ = expectedProjectorVersion;
        entry.IsMalformed |= state.Version < 0 ||
                             (state.Version == 0 && position.Length > 0) ||
                             (state.Version > 0 && position.Length == 0);
        entry.IsMalformed |= !string.Equals(state.TagGroup, requestedTag.GetTagGroup(), StringComparison.Ordinal) ||
                             !string.Equals(state.TagContent, requestedTag.GetTagContent(), StringComparison.Ordinal) ||
                             string.IsNullOrWhiteSpace(state.TagProjector) ||
                             string.IsNullOrWhiteSpace(state.ProjectorVersion);
    }

    public bool TryGet(string tag, out CommandStateReadEvidence evidence)
    {
        if (_entries.TryGetValue(tag, out var entry))
        {
            evidence = new CommandStateReadEvidence(
                entry.ServiceId,
                entry.Tag,
                entry.Position ?? string.Empty,
                entry.HadSuccessfulRead,
                entry.HadFailedAttempt,
                entry.HasConflictingPositions,
                entry.IsMalformed);
            return true;
        }

        evidence = default;
        return false;
    }

    internal static string LogicalTag(ITag tag)
    {
        var inner = tag is ConsistencyTag consistencyTag ? consistencyTag.InnerTag : tag;
        return inner.GetTag();
    }

    private sealed class Entry(string tag, string serviceId)
    {
        public string Tag { get; } = tag;
        public string ServiceId { get; } = serviceId;
        public string? Position { get; set; }
        public bool HadSuccessfulRead { get; set; }
        public bool HadFailedAttempt { get; set; }
        public bool HasConflictingPositions { get; set; }
        public bool IsMalformed { get; set; }
    }
}

internal readonly record struct CommandStateReadEvidence(
    string ServiceId,
    string Tag,
    string Position,
    bool HadSuccessfulRead,
    bool HadFailedAttempt,
    bool HasConflictingPositions,
    bool IsMalformed)
{
    public bool IsUsable => HadSuccessfulRead &&
                            !HadFailedAttempt &&
                            !HasConflictingPositions &&
                            !IsMalformed;
}

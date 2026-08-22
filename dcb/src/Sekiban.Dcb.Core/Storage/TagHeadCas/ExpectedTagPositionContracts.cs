using ResultBoxes;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Storage;

/// <summary>
///     The total, explicitly discriminated expected-position state for one consistency tag.  The discriminator, not a
///     nullable position, determines the requested behaviour: a missing / null expectation is therefore never silently
///     interpreted as an unconditional write.
/// </summary>
public enum TagHeadExpectationKind
{
    /// <summary>Invalid / unspecified. Rejected before a store write.</summary>
    Unknown = 0,

    /// <summary>Do not compare this tag's head, but still create, lock, reconcile and advance it.</summary>
    NoEnforcement = 1,

    /// <summary>Require the durably reconciled head to be proven empty.</summary>
    AssertEmpty = 2,

    /// <summary>Require the durably reconciled head to equal <see cref="TagHeadExpectation.Position" />.</summary>
    Exact = 3
}

/// <summary>
///     One branch of the expected-tag-head protocol.  <see cref="Kind" /> is always required.  <see cref="Position" />
///     is required only for <see cref="TagHeadExpectationKind.Exact" /> and is forbidden for the other branches.
/// </summary>
public sealed record TagHeadExpectation(TagHeadExpectationKind Kind, string? Position = null)
{
    public static TagHeadExpectation NoEnforcement() => new(TagHeadExpectationKind.NoEnforcement);
    public static TagHeadExpectation AssertEmpty() => new(TagHeadExpectationKind.AssertEmpty);
    public static TagHeadExpectation Exact(string position) => new(TagHeadExpectationKind.Exact, position);

    internal void Validate()
    {
        if (Kind == TagHeadExpectationKind.NoEnforcement && Position is null)
        {
            return;
        }
        if (Kind == TagHeadExpectationKind.AssertEmpty && Position is null)
        {
            return;
        }
        if (Kind == TagHeadExpectationKind.Exact && !string.IsNullOrWhiteSpace(Position))
        {
            return;
        }

        switch (Kind)
        {
            case TagHeadExpectationKind.AssertEmpty:
                throw new TagHeadExpectationValidationException(
                    "AssertEmpty must not carry a position. Use the explicit Exact branch for a position.");

            case TagHeadExpectationKind.NoEnforcement:
                throw new TagHeadExpectationValidationException(
                    "NoEnforcement must not carry a position. Use the explicit Exact branch for a position.");

            case TagHeadExpectationKind.Exact:
                throw new TagHeadExpectationValidationException("Exact requires a non-empty position.");

            default:
                throw new TagHeadExpectationValidationException("An expected tag-head entry has an unknown discriminator.");
        }
    }
}

/// <summary>A service-scoped expected-head entry. Each affected consistency tag must occur exactly once.</summary>
public sealed record TagHeadExpectationEntry(string ServiceId, string Tag, TagHeadExpectation Expectation);

/// <summary>
///     A complete request-side specification for the consistency tags derived by an executor.  It is deliberately an
///     additive object rather than a nullable expected-position parameter on an existing public method.
/// </summary>
public sealed record ExpectedTagPositionSpecification(IReadOnlyList<TagHeadExpectationEntry> Entries)
{
    /// <summary>True when at least one tag requests a durable comparison rather than NoEnforcement.</summary>
    public bool RequiresEnforcement => Entries?.Any(e => e.Expectation?.Kind is not TagHeadExpectationKind.NoEnforcement) == true;

    /// <summary>
    ///     Validates the total request against the exact affected consistency-tag set. This happens before reservation,
    ///     event allocation, or store mutation.
    /// </summary>
    internal IReadOnlyDictionary<string, TagHeadExpectation> ValidateFor(
        string serviceId,
        IEnumerable<string> affectedConsistencyTags)
    {
        ValidateEntryShapes(serviceId);

        var expectedTags = new HashSet<string>(affectedConsistencyTags, StringComparer.Ordinal);
        var result = new Dictionary<string, TagHeadExpectation>(StringComparer.Ordinal);

        foreach (var entry in Entries)
        {
            if (!result.TryAdd(entry.Tag, entry.Expectation))
            {
                throw new TagHeadExpectationValidationException($"Duplicate expected tag-head entry for '{entry.Tag}'.");
            }
        }

        if (!result.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedTags))
        {
            var missing = expectedTags.Except(result.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var unknown = result.Keys.Except(expectedTags, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            throw new TagHeadExpectationValidationException(
                $"Expected tag-head entries must match the derived consistency tags exactly. Missing: {string.Join(", ", missing)}. " +
                $"Unknown: {string.Join(", ", unknown)}.");
        }

        return result;
    }

    /// <summary>Validates the entry-level total/discriminated shape without deriving a command's tag set.</summary>
    internal void ValidateEntryShapes(string serviceId)
    {
        if (Entries is null)
        {
            throw new TagHeadExpectationValidationException("Expected tag-head entries are required; use an empty array for no affected tags.");
        }

        foreach (var entry in Entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.ServiceId) || string.IsNullOrWhiteSpace(entry.Tag) ||
                entry.Expectation is null)
            {
                throw new TagHeadExpectationValidationException("Every expected tag-head entry needs serviceId, tag, and an explicit expectation.");
            }
            if (!string.Equals(entry.ServiceId, serviceId, StringComparison.Ordinal))
            {
                throw new TagHeadExpectationValidationException(
                    $"Expected tag-head entry for '{entry.Tag}' belongs to service '{entry.ServiceId}', not the current service.");
            }
            entry.Expectation.Validate();
        }

        if (Entries.GroupBy(e => e.Tag, StringComparer.Ordinal).Any(g => g.Count() > 1))
        {
            var duplicate = Entries.GroupBy(e => e.Tag, StringComparer.Ordinal).First(g => g.Count() > 1).Key;
            throw new TagHeadExpectationValidationException($"Duplicate expected tag-head entry for '{duplicate}'.");
        }
    }
}

/// <summary>One complete expected/observed pair returned on a durable expected-head conflict.</summary>
public sealed record TagHeadExpectedObserved(
    string ServiceId,
    string Tag,
    TagHeadExpectation Expected,
    string? ObservedPosition);

/// <summary>
///     Typed optimistic-concurrency failure.  The list is complete for the request's affected consistency-tag set, not
///     merely the first stale entry, so callers can deterministically refresh all heads in one round trip.
/// </summary>
public sealed class ExpectedTagPositionConflictException : Exception
{
    public ExpectedTagPositionConflictException(IReadOnlyList<TagHeadExpectedObserved> pairs)
        : base("One or more expected tag positions do not match the durable PostgreSQL heads.") => Pairs = pairs;

    public IReadOnlyList<TagHeadExpectedObserved> Pairs { get; }
}

/// <summary>Typed malformed-request failure raised before a write or lazy head creation.</summary>
public sealed class TagHeadExpectationValidationException : ArgumentException
{
    public TagHeadExpectationValidationException(string message) : base(message) { }
}

/// <summary>Typed position invariant failure raised before command rows are materialized.</summary>
public sealed class TagHeadPositionValidationException : ArgumentException
{
    public TagHeadPositionValidationException(string message) : base(message) { }
}

/// <summary>
///     The durable expected-position fence is unavailable until an operator has provisioned the schema, drained every
///     pre-epoch PostgreSQL writer, and set the enablement epoch marker for this service.
/// </summary>
public sealed class TagHeadEnforcementNotEnabledException : InvalidOperationException
{
    public TagHeadEnforcementNotEnabledException(string serviceId)
        : base($"Expected tag-position enforcement is not enabled for service '{serviceId}'. Provision, drain pre-10.19 writers, then set the enablement epoch before requesting enforcement.")
    {
        ServiceId = serviceId;
    }

    public string ServiceId { get; }
}

/// <summary>Result returned by the optional store-enforced expected-position writer.</summary>
public sealed record ExpectedTagPositionWriteResult(
    IReadOnlyList<SerializableEvent> Events,
    IReadOnlyList<Sekiban.Dcb.Tags.TagWriteResult> TagWrites);

/// <summary>
///     OPTIONAL additive store capability for PostgreSQL's durable multi-tag expected-position protocol. It is kept off
///     <see cref="IEventStore" /> so existing providers remain binary/source compatible. A supporting store must advertise
///     <c>WriteConditionKind.ExpectedTagPosition</c>; callers feature-detect it and fail closed before any provider write.
/// </summary>
public interface IExpectedTagPositionEventStore
{
    /// <summary>Checks the service's provisioning-plane enablement epoch without creating or advancing a head.</summary>
    Task<ResultBox<bool>> EnsureExpectedTagPositionEnforcementEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Writes a serialized batch through the canonical head protocol. NoEnforcement entries still create, lock,
    ///     reconcile and advance durable heads; they only skip the expected-head comparison.
    /// </summary>
    Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableEventsWithExpectedTagPositionsAsync(
        IReadOnlyList<SerializableEvent> events,
        ExpectedTagPositionSpecification specification,
        CancellationToken cancellationToken = default);
}

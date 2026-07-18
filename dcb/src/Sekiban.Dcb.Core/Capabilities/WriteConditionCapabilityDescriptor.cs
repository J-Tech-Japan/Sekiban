namespace Sekiban.Dcb.Capabilities;

/// <summary>
///     The kinds of write-condition an event store can enforce at append time. Mirrors the fail-closed convention of the
///     other capability enums: <see cref="Unknown" /> is <c>0</c> and means "nothing promised". A store that says nothing
///     supports nothing — silence is never read as a capability.
/// </summary>
public enum WriteConditionKind
{
    /// <summary>Nothing promised. Never treated as support.</summary>
    Unknown = 0,

    /// <summary>
    ///     Single-event conditional append keyed by a caller-supplied idempotency key (the SEK-G15 contract): at most one
    ///     durable claim per key, with same-operation retries recognised and different-operation reuse rejected.
    /// </summary>
    SingleEventUniqueKey = 1

    // Future kinds (NOT part of this contract): BatchUniqueKey, ExpectedPosition. They are separate capability kinds so
    // a store can support one without implying the others; add them here and have supporting stores list them.
}

/// <summary>
///     What write-conditions the ACTUALLY-RESOLVED event store can enforce, mirroring the G10 capability-descriptor
///     pattern (see <see cref="StorageDurabilityDescriptor" />). Carries the set of supported <see cref="WriteConditionKind" />
///     and a human-readable provider name for banners/errors — never a connection string. A store that supports nothing
///     returns <see cref="None" />; there is no "unknown = maybe" state, only the empty set (fail-closed).
/// </summary>
public sealed record WriteConditionCapabilityDescriptor(
    IReadOnlySet<WriteConditionKind> SupportedKinds,
    string ProviderName)
{
    /// <summary>True only when <paramref name="kind" /> is a real (non-<see cref="WriteConditionKind.Unknown" />) kind and it is supported.</summary>
    public bool Supports(WriteConditionKind kind) =>
        kind != WriteConditionKind.Unknown && SupportedKinds.Contains(kind);

    /// <summary>A descriptor that supports nothing — the fail-closed default for a store that does not implement the capability.</summary>
    public static WriteConditionCapabilityDescriptor None(string providerName) =>
        new(new HashSet<WriteConditionKind>(), providerName);

    /// <summary>A descriptor supporting exactly the given kinds.</summary>
    public static WriteConditionCapabilityDescriptor Supporting(string providerName, params WriteConditionKind[] kinds) =>
        new(new HashSet<WriteConditionKind>(kinds.Where(k => k != WriteConditionKind.Unknown)), providerName);

    /// <summary>
    ///     The propagation rule for a COMPOSITE store that fans a write out to several underlying stores: a kind is
    ///     supported only when EVERY underlying store supports it (the intersection). Re-labels with
    ///     <paramref name="providerName" />. A decorator that forwards to a single inner store (e.g. Hybrid → hot store)
    ///     should instead reuse the inner descriptor's kinds directly.
    /// </summary>
    public static WriteConditionCapabilityDescriptor Intersect(
        string providerName,
        IReadOnlyCollection<WriteConditionCapabilityDescriptor> underlying)
    {
        if (underlying.Count == 0)
        {
            return None(providerName);
        }

        var intersection = underlying
            .Select(d => (ISet<WriteConditionKind>)new HashSet<WriteConditionKind>(d.SupportedKinds))
            .Aggregate((acc, next) =>
            {
                acc.IntersectWith(next);
                return acc;
            });
        return new WriteConditionCapabilityDescriptor(new HashSet<WriteConditionKind>(intersection), providerName);
    }

    public override string ToString() =>
        SupportedKinds.Count == 0
            ? $"{ProviderName} (no write conditions)"
            : $"{ProviderName} ({string.Join(", ", SupportedKinds)})";
}

/// <summary>
///     Implemented by an event store (or a decorator/factory/composite over one) to declare which write-conditions it can
///     actually enforce. Resolved from the LIVE instance the container built, never from a type name — mirrors
///     <see cref="IStorageDurabilityDescriptorProvider" />.
/// </summary>
public interface IWriteConditionCapabilityProvider
{
    WriteConditionCapabilityDescriptor DescribeWriteConditions();
}

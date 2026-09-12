using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using ResultBoxes;

namespace Sekiban.Dcb.Subscriptions;

/// <summary>How the first durable cursor is selected.</summary>
public enum DurableSubscriptionStartPolicy
{
    /// <summary>Start at the current safe tail and process only events written afterwards.</summary>
    FromNow,

    /// <summary>Start at the beginning of the service event stream.</summary>
    FromBeginning
}

/// <summary>Controls whether the provider may create its durable state table at runtime.</summary>
public enum DurableSubscriptionProvisioningMode
{
    LegacyAutoProvision,
    PreProvisioned
}

/// <summary>Durable lifecycle phases persisted by the provider.</summary>
public enum DurableSubscriptionPhase
{
    Uninitialized,
    CatchingUp,
    Idle,
    Retrying,
    Standby,
    Halted
}

/// <summary>Normalized service/name identity for a durable subscriber.</summary>
public sealed record DurableSubscriptionIdentity
{
    public DurableSubscriptionIdentity(string serviceId, string name)
    {
        ServiceId = ServiceIdValidator.NormalizeAndValidate(serviceId);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Durable subscription name must not be empty.", nameof(name));
        }

        if (name.Length > 128)
        {
            throw new ArgumentException("Durable subscription name must be at most 128 characters.", nameof(name));
        }

        Name = name.Trim();
        if (Name.Length == 0 || Name.Any(char.IsControl))
        {
            throw new ArgumentException("Durable subscription name contains invalid characters.", nameof(name));
        }
    }

    public string ServiceId { get; }
    public string Name { get; }
}

/// <summary>Validated bounds and timing for a durable subscriber.</summary>
public sealed class DurableSubscriptionOptions
{
    public string ServiceId { get; set; } = DefaultServiceIdProvider.DefaultServiceId;
    public string Name { get; set; } = "subscription";
    public DurableSubscriptionStartPolicy StartPolicy { get; set; } = DurableSubscriptionStartPolicy.FromNow;
    public DurableSubscriptionProvisioningMode ProvisioningMode { get; set; } = DurableSubscriptionProvisioningMode.LegacyAutoProvision;
    public TimeSpan SafeWindow { get; set; } = TimeSpan.FromMilliseconds(SortableUniqueId.SafeMilliseconds);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxHandlerAttempts { get; set; } = 4;
    public int MaxBatchSize { get; set; } = 256;

    public DurableSubscriptionIdentity Identity => new(ServiceId, Name);

    public void Validate()
    {
        _ = Identity;
        if (SafeWindow < TimeSpan.Zero || SafeWindow > TimeSpan.FromDays(7))
        {
            throw new ArgumentOutOfRangeException(nameof(SafeWindow), "SafeWindow must be between zero and seven days.");
        }

        if (LeaseDuration < TimeSpan.FromSeconds(1) || LeaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration), "LeaseDuration must be between one second and one hour.");
        }

        if (PollInterval <= TimeSpan.Zero || PollInterval > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), "PollInterval must be positive and at most one hour.");
        }

        if (RetryDelay <= TimeSpan.Zero || RetryDelay > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(RetryDelay), "RetryDelay must be positive and at most one hour.");
        }

        if (MaxHandlerAttempts < 1 || MaxHandlerAttempts > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHandlerAttempts), "MaxHandlerAttempts must be between one and sixteen.");
        }

        if (MaxBatchSize < 1 || MaxBatchSize > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBatchSize), "MaxBatchSize must be between one and 10,000.");
        }
    }
}

/// <summary>Durable state returned by a provider.</summary>
public sealed record DurableSubscriptionState(
    DurableSubscriptionIdentity Identity,
    bool Initialized,
    string? AcknowledgedPosition,
    DurableSubscriptionPhase Phase,
    string? ActiveFailurePosition,
    int ActiveFailureCount,
    string? ActiveFailureReason,
    DateTimeOffset? NextAttemptAtUtc,
    string? LastHaltPosition,
    DateTimeOffset? LastHaltAtUtc,
    string? LastHaltReason,
    string? OwnerId,
    long OwnerGeneration,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Lease acquisition result; non-owners receive a state instead of a false healthy result.</summary>
public sealed record DurableSubscriptionLease(
    bool Acquired,
    DurableSubscriptionState State);

/// <summary>Provider-owned state boundary for a durable subscription.</summary>
public interface IDurableSubscriptionStore
{
    Task<ResultBox<DurableSubscriptionState>> InitializeOrGetAsync(
        DurableSubscriptionIdentity identity,
        DurableSubscriptionStartPolicy startPolicy,
        string safeTail,
        CancellationToken cancellationToken = default);

    Task<ResultBox<DurableSubscriptionState>> ReadAsync(
        DurableSubscriptionIdentity identity,
        CancellationToken cancellationToken = default);

    Task<ResultBox<DurableSubscriptionLease>> TryAcquireAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<ResultBox<bool>> RenewAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        long ownerGeneration,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<ResultBox<DurableSubscriptionState>> AcknowledgeAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        long ownerGeneration,
        string position,
        CancellationToken cancellationToken = default);

    Task<ResultBox<DurableSubscriptionState>> RecordFailureAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        long ownerGeneration,
        string position,
        string reason,
        bool conversionFailure,
        int maxHandlerAttempts,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);

    Task<ResultBox<DurableSubscriptionState>> ResumeAsync(
        DurableSubscriptionIdentity identity,
        CancellationToken cancellationToken = default);

    Task<ResultBox<DurableSubscriptionState>> HaltAsync(
        DurableSubscriptionIdentity identity,
        string reason,
        CancellationToken cancellationToken = default);

    Task<ResultBox<DurableSubscriptionState>> MarkIdleAsync(
        DurableSubscriptionIdentity identity,
        string ownerId,
        long ownerGeneration,
        CancellationToken cancellationToken = default);
}

/// <summary>Orleans or another delivery system can wake a durable runner; it never supplies authoritative data.</summary>
public interface IDurableSubscriptionNudgeFactory
{
    IDurableSubscriptionNudge Create(DurableSubscriptionIdentity identity, Func<ValueTask> onNudge);
}

/// <summary>Disposable best-effort wake subscription.</summary>
public interface IDurableSubscriptionNudge : IAsyncDisposable
{
}

/// <summary>Delegate used by a durable runner after storage conversion succeeds.</summary>
public delegate Task DurableSubscriptionHandler(Event @event, CancellationToken cancellationToken);

/// <summary>Registration object used by provider DI extensions.</summary>
public sealed record DurableSubscriptionRegistration(
    DurableSubscriptionOptions Options,
    DurableSubscriptionHandler Handler);

/// <summary>Public handle for an opt-in durable runner.</summary>
public interface IDurableSubscriptionHandle : IAsyncDisposable
{
    DurableSubscriptionIdentity Identity { get; }
    Task<DurableSubscriptionState> GetStateAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task HaltAsync(string reason, CancellationToken cancellationToken = default);
}

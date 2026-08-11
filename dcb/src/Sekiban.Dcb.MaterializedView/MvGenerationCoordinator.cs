using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView;

/// <summary>Hosted-process generation lifecycle and switch boundary for one exact service/view.</summary>
public interface IMvGenerationCoordinator
{
    Task PrepareGenerationAsync(
        IMvApplyHost candidate,
        string? serviceId = null,
        CancellationToken cancellationToken = default);

    Task<MvActivationResult> SwitchAsync(
        IMvApplyHost candidate,
        string? serviceId = null,
        CancellationToken cancellationToken = default,
        MvProjectionStatusPublisherKind publisherKind = MvProjectionStatusPublisherKind.HostedWorker);

    Task<MvActivationResult> ForceReverseAsync(
        IMvApplyHost retainedCandidate,
        int expectedActiveVersion,
        long expectedActiveGeneration,
        string reason,
        string? serviceId = null,
        CancellationToken cancellationToken = default,
        MvProjectionStatusPublisherKind publisherKind = MvProjectionStatusPublisherKind.HostedWorker);

    Task<MvActiveEntry?> GetActiveAsync(
        string viewName,
        string? serviceId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Provider-neutral coordinator. Process-local gates provide bounded single-flight behavior while provider CAS
///     remains the restart-safe and cross-process authority.
/// </summary>
public sealed class MvGenerationCoordinator : IMvGenerationCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lanes = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _capacity;
    private readonly IMvExecutor _executor;
    private readonly IMvRegistryStore _registry;
    private readonly MvOptions _options;
    private readonly IServiceIdProvider? _serviceIdProvider;
    private readonly MvProjectionStatusPublisher? _statusPublisher;

    public MvGenerationCoordinator(
        IMvExecutor executor,
        IMvRegistryStore registry,
        IOptions<MvOptions> options,
        IServiceIdProvider? serviceIdProvider = null,
        MvProjectionStatusPublisher? statusPublisher = null)
    {
        _executor = executor;
        _registry = registry;
        _options = options.Value;
        _capacity = new SemaphoreSlim(Math.Max(1, _options.CatchUpMaxConcurrentBatches));
        _serviceIdProvider = serviceIdProvider;
        _statusPublisher = statusPublisher;
    }

    public async Task PrepareGenerationAsync(
        IMvApplyHost candidate,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateCandidate(candidate);
        var exactServiceId = ValidateServiceId(serviceId);
        await InLaneAsync(
            exactServiceId,
            candidate.ViewName,
            async () =>
            {
                await _executor.InitializeAsync(candidate, exactServiceId, cancellationToken).ConfigureAwait(false);
                await _executor.CaptureTargetCheckpointAsync(candidate, exactServiceId, cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MvActivationResult> SwitchAsync(
        IMvApplyHost candidate,
        string? serviceId = null,
        CancellationToken cancellationToken = default,
        MvProjectionStatusPublisherKind publisherKind = MvProjectionStatusPublisherKind.HostedWorker)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateCandidate(candidate);
        var exactServiceId = ValidateServiceId(serviceId);
        return await InLaneAsync(
            exactServiceId,
            candidate.ViewName,
            async () =>
            {
                var result = await _executor.TryActivateAsync(candidate, exactServiceId, cancellationToken)
                    .ConfigureAwait(false);
                if (result.Succeeded)
                {
                    try
                    {
                        await PublishActiveSwitchAsync(
                                exactServiceId,
                                candidate.ViewName,
                                candidate.ViewVersion,
                                publisherKind,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // The provider CAS is authoritative. Observation is bounded best effort and never reverses
                        // or obscures a successfully committed pointer transition.
                    }
                }

                return result;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MvActivationResult> ForceReverseAsync(
        IMvApplyHost retainedCandidate,
        int expectedActiveVersion,
        long expectedActiveGeneration,
        string reason,
        string? serviceId = null,
        CancellationToken cancellationToken = default,
        MvProjectionStatusPublisherKind publisherKind = MvProjectionStatusPublisherKind.HostedWorker)
    {
        ArgumentNullException.ThrowIfNull(retainedCandidate);
        var exactServiceId = ValidateServiceId(serviceId);
        var inputRejection = ValidateForcedReverseInput(
            retainedCandidate,
            expectedActiveVersion,
            expectedActiveGeneration,
            reason,
            out var normalizedReason);
        if (inputRejection is not null)
        {
            return inputRejection;
        }

        return await InLaneAsync(
            exactServiceId,
            retainedCandidate.ViewName,
            () => ForceReverseInLaneAsync(
                retainedCandidate,
                expectedActiveVersion,
                expectedActiveGeneration,
                normalizedReason,
                exactServiceId,
                publisherKind,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<MvActivationResult> ForceReverseInLaneAsync(
        IMvApplyHost retainedCandidate,
        int expectedActiveVersion,
        long expectedActiveGeneration,
        string normalizedReason,
        string exactServiceId,
        MvProjectionStatusPublisherKind publisherKind,
        CancellationToken cancellationToken)
    {
        var entries = await _registry.GetEntriesAsync(
                exactServiceId,
                retainedCandidate.ViewName,
                retainedCandidate.ViewVersion,
                cancellationToken)
            .ConfigureAwait(false);
        var candidateRejection = ValidateForcedCandidate(entries, retainedCandidate, exactServiceId);
        if (candidateRejection is not null)
        {
            return candidateRejection;
        }

        var active = await _registry.GetActiveAsync(exactServiceId, retainedCandidate.ViewName, cancellationToken)
            .ConfigureAwait(false);
        if (active is null ||
            active.ActiveVersion != expectedActiveVersion ||
            active.Generation != expectedActiveGeneration)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ExpectedActiveConflict,
                "The active pointer no longer matches the forced-reverse fence.");
        }

        var switchedAt = PersistenceTimestampNow();
        var result = await _registry.TryForceReverseAsync(
                new MvForcedReverseRequest(
                    exactServiceId,
                    retainedCandidate.ViewName,
                    retainedCandidate.ViewVersion,
                    expectedActiveVersion,
                    expectedActiveGeneration,
                    entries.Count,
                    entries[0].Status,
                    normalizedReason,
                    switchedAt),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await PublishForcedSwitchAsync(
            result,
            entries,
            retainedCandidate,
            exactServiceId,
            normalizedReason,
            switchedAt,
            publisherKind,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

    public Task<MvActiveEntry?> GetActiveAsync(
        string viewName,
        string? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        var exactServiceId = ValidateServiceId(serviceId);
        return _registry.GetActiveAsync(exactServiceId, viewName, cancellationToken);
    }

    private string ValidateServiceId(string? serviceId) =>
        MvServiceIdValidation.Validate(serviceId, _options, _serviceIdProvider, nameof(MvGenerationCoordinator));

    private static DateTimeOffset PersistenceTimestampNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - (now.Ticks % 10), TimeSpan.Zero);
    }

    private static MvActivationResult? ValidateForcedReverseInput(
        IMvApplyHost candidate,
        int expectedActiveVersion,
        long expectedActiveGeneration,
        string reason,
        out string normalizedReason)
    {
        normalizedReason = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate.ViewName) || candidate.ViewVersion < 0)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.IdentityMismatch,
                "Forced reverse requires an exact view identity and a non-negative retained version.");
        }
        if (expectedActiveVersion <= candidate.ViewVersion)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.IdentityMismatch,
                "Forced switching is reverse-only: the retained candidate version must precede the expected active version.");
        }
        if (expectedActiveGeneration < 0)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.ExpectedGenerationConflict,
                "The expected active generation cannot be negative.");
        }
        if (normalizedReason.Length == 0)
        {
            throw new ArgumentException("A non-empty operator reason is required for forced reverse.", nameof(reason));
        }
        if (normalizedReason.Length > 1024)
        {
            throw new ArgumentException("The forced-reverse reason cannot exceed 1024 characters.", nameof(reason));
        }
        if (normalizedReason.Any(char.IsControl))
        {
            throw new ArgumentException("The forced-reverse reason cannot contain control characters.", nameof(reason));
        }

        return null;
    }

    private static MvActivationResult? ValidateForcedCandidate(
        IReadOnlyList<MvRegistryEntry> entries,
        IMvApplyHost candidate,
        string exactServiceId)
    {
        if (entries.Count == 0)
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.CandidateMissing,
                "The forced-reverse candidate generation does not exist.");
        }
        if (entries.Any(entry =>
                !string.Equals(entry.ServiceId, exactServiceId, StringComparison.Ordinal) ||
                !string.Equals(entry.ViewName, candidate.ViewName, StringComparison.Ordinal) ||
                entry.ViewVersion != candidate.ViewVersion))
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.IdentityMismatch,
                "The forced-reverse candidate identity does not match the exact service, view, and version.");
        }
        if (entries.Any(entry => entry.Status is not (MvStatus.Ready or MvStatus.Active)))
        {
            return MvActivationResult.Rejected(
                MvActivationFailureReason.UnsafeLifecycle,
                "Forced reverse waives checkpoint truth only; the retained generation must remain Ready or Active.");
        }

        return null;
    }

    private async Task PublishForcedSwitchAsync(
        MvActivationResult result,
        IReadOnlyList<MvRegistryEntry> entries,
        IMvApplyHost candidate,
        string exactServiceId,
        string reason,
        DateTimeOffset switchedAt,
        MvProjectionStatusPublisherKind publisherKind,
        CancellationToken cancellationToken)
    {
        if (!result.Succeeded || _statusPublisher is null)
        {
            return;
        }

        var snapshot = MvProjectionStatusSnapshot.FromEntries(entries) with
        {
            Status = MvStatus.Active,
            SwitchKind = MvSwitchKind.Forced,
            SwitchReason = reason,
            SwitchedAtUtc = switchedAt
        };
        try
        {
            await _statusPublisher.PublishSwitchAsync(
                    exactServiceId,
                    candidate.ViewName,
                    candidate.ViewVersion,
                    snapshot,
                    publisherKind,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Switching is durable before observation. A status failure cannot propagate into MV work.
        }
    }

    private static void ValidateCandidate(IMvApplyHost candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate.ViewName);
        if (candidate.ViewVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), "A materialized-view version cannot be negative.");
        }
    }

    private async Task PublishActiveSwitchAsync(
        string serviceId,
        string viewName,
        int viewVersion,
        MvProjectionStatusPublisherKind publisherKind,
        CancellationToken cancellationToken)
    {
        if (_statusPublisher is null)
        {
            return;
        }

        var entries = await _registry.GetEntriesAsync(serviceId, viewName, viewVersion, cancellationToken)
            .ConfigureAwait(false);
        var active = await _registry.GetActiveAsync(serviceId, viewName, cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0 || active is null || active.ActiveVersion != viewVersion)
        {
            return;
        }

        await _statusPublisher.PublishSwitchAsync(
                serviceId,
                viewName,
                viewVersion,
                MvProjectionStatusSnapshot.FromEntries(entries) with
                {
                    Status = MvStatus.Active,
                    SwitchKind = active.SwitchKind,
                    SwitchReason = active.SwitchReason,
                    SwitchedAtUtc = active.SwitchedAtUtc
                },
                publisherKind,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<T> InLaneAsync<T>(
        string serviceId,
        string viewName,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var lane = _lanes.GetOrAdd(string.Join('|', serviceId, viewName), _ => new SemaphoreSlim(1, 1));
        await lane.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _capacity.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await action().ConfigureAwait(false);
            }
            finally
            {
                _capacity.Release();
            }
        }
        finally
        {
            lane.Release();
        }
    }
}

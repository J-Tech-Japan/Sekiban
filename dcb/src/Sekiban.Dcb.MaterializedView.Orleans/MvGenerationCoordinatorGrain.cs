using Orleans.Concurrency;
using Sekiban.Dcb.Orleans.ServiceId;

namespace Sekiban.Dcb.MaterializedView.Orleans;

[GenerateSerializer, Immutable]
public sealed record MvGenerationSwitchResult(
    [property: Id(0)] bool Succeeded,
    [property: Id(1)] int FailureReason,
    [property: Id(2)] string Message,
    [property: Id(3)] long? NewGeneration)
{
    internal static MvGenerationSwitchResult From(MvActivationResult result) =>
        new(result.Succeeded, (int)result.FailureReason, result.Message, result.NewGeneration);
}

[GenerateSerializer, Immutable]
public sealed record MvActiveGenerationStatus(
    [property: Id(0)] string ServiceId,
    [property: Id(1)] string ViewName,
    [property: Id(2)] int ActiveVersion,
    [property: Id(3)] long Generation,
    [property: Id(4)] int SwitchKind,
    [property: Id(5)] string? SwitchReason,
    [property: Id(6)] DateTimeOffset? SwitchedAtUtc);

public interface IMvGenerationCoordinatorGrain : IGrainWithStringKey
{
    Task PrepareGenerationAsync(int viewVersion);
    Task<MvGenerationSwitchResult> SwitchAsync(int viewVersion);
    Task<MvGenerationSwitchResult> ForceReverseAsync(
        int retainedVersion,
        int expectedActiveVersion,
        long expectedActiveGeneration,
        string reason);

    [AlwaysInterleave]
    Task<MvActiveGenerationStatus?> GetActiveAsync();
}

public static class MvGenerationCoordinatorGrainKey
{
    private const string Prefix = "mv-coordinator::";

    public static string Build(string serviceId, string viewName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewName);
        return ServiceIdGrainKey.Build(serviceId, Prefix + viewName);
    }

    internal static (string ServiceId, string ViewName) Parse(string key)
    {
        var (serviceId, raw) = ServiceIdGrainKey.Parse(key);
        if (!raw.StartsWith(Prefix, StringComparison.Ordinal) || raw.Length == Prefix.Length)
        {
            throw new ArgumentException($"Invalid MV generation coordinator key '{key}'.", nameof(key));
        }

        return (serviceId, raw[Prefix.Length..]);
    }
}

/// <summary>Orleans single-flight facade over the same provider-neutral hosted coordinator.</summary>
public sealed class MvGenerationCoordinatorGrain(
    IMvGenerationCoordinator coordinator,
    IMvApplyHostFactory hostFactory) : Grain, IMvGenerationCoordinatorGrain
{
    private string _serviceId = string.Empty;
    private string _viewName = string.Empty;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        (_serviceId, _viewName) = MvGenerationCoordinatorGrainKey.Parse(this.GetPrimaryKeyString());
        return base.OnActivateAsync(cancellationToken);
    }

    public async Task PrepareGenerationAsync(int viewVersion)
    {
        var host = CreateHost(viewVersion);
        await coordinator.PrepareGenerationAsync(host, _serviceId).ConfigureAwait(false);
        var worker = GrainFactory.GetGrain<IMaterializedViewGrain>(MvGrainKey.Build(_serviceId, _viewName, viewVersion));
        await worker.EnsureStartedAsync().ConfigureAwait(false);
    }

    public async Task<MvGenerationSwitchResult> SwitchAsync(int viewVersion) =>
        MvGenerationSwitchResult.From(await coordinator.SwitchAsync(
                CreateHost(viewVersion),
                _serviceId,
                publisherKind: MvProjectionStatusPublisherKind.Orleans)
            .ConfigureAwait(false));

    public async Task<MvGenerationSwitchResult> ForceReverseAsync(
        int retainedVersion,
        int expectedActiveVersion,
        long expectedActiveGeneration,
        string reason) =>
        MvGenerationSwitchResult.From(await coordinator.ForceReverseAsync(
                CreateHost(retainedVersion),
                expectedActiveVersion,
                expectedActiveGeneration,
                reason,
                _serviceId,
                publisherKind: MvProjectionStatusPublisherKind.Orleans)
            .ConfigureAwait(false));

    public async Task<MvActiveGenerationStatus?> GetActiveAsync()
    {
        var active = await coordinator.GetActiveAsync(_viewName, _serviceId).ConfigureAwait(false);
        return active is null
            ? null
            : new MvActiveGenerationStatus(
                active.ServiceId,
                active.ViewName,
                active.ActiveVersion,
                active.Generation,
                (int)active.SwitchKind,
                active.SwitchReason,
                active.SwitchedAtUtc);
    }

    private IMvApplyHost CreateHost(int version)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        return hostFactory.Create(_viewName, version);
    }
}

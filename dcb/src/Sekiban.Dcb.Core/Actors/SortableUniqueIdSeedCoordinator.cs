using System.Collections.Concurrent;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Actors;

/// <summary>Typed failure raised when an executor cannot establish its service's persisted SortableUniqueId floor.</summary>
public sealed class SortableUniqueIdSeedException : InvalidOperationException
{
    public SortableUniqueIdSeedException(string serviceId, string message, Exception? innerException = null)
        : base(message, innerException) => ServiceId = serviceId;

    public string ServiceId { get; }
}

/// <summary>Coordinates one retryable, single-flight store-head seed per normalized service id.</summary>
public sealed class SortableUniqueIdSeedCoordinator
{
    private readonly ConcurrentDictionary<string, Lazy<Task>> _seeds = new(StringComparer.Ordinal);
    private readonly ISortableUniqueIdGenerator _generator;

    public SortableUniqueIdSeedCoordinator(ISortableUniqueIdGenerator generator) =>
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));

    public async Task EnsureSeededAsync(
        string serviceId,
        IEventStore eventStore,
        CancellationToken cancellationToken = default)
    {
        var normalizedServiceId = ServiceIdValidator.NormalizeAndValidate(serviceId);
        ArgumentNullException.ThrowIfNull(eventStore);
        cancellationToken.ThrowIfCancellationRequested();

        var seed = _seeds.GetOrAdd(
            normalizedServiceId,
            _ => new Lazy<Task>(
                () => SeedCoreAsync(normalizedServiceId, eventStore, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            await seed.Value.ConfigureAwait(false);
        }
        catch
        {
            _seeds.TryRemove(new KeyValuePair<string, Lazy<Task>>(normalizedServiceId, seed));
            throw;
        }
    }

    private async Task SeedCoreAsync(string serviceId, IEventStore eventStore, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ResultBox<string> result;
        try
        {
            result = await eventStore.GetLatestSortableUniqueIdAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new SortableUniqueIdSeedException(
                serviceId,
                $"Failed to read the persisted SortableUniqueId head for service '{serviceId}'.",
                exception);
        }

        if (!result.IsSuccess)
        {
            throw new SortableUniqueIdSeedException(
                serviceId,
                $"Failed to read the persisted SortableUniqueId head for service '{serviceId}'.",
                result.GetException());
        }

        var head = result.GetValue();
        if (string.IsNullOrEmpty(head))
        {
            return;
        }

        if (!SortableUniqueId.TryParse(head, out _) ||
            !long.TryParse(
                head.AsSpan(0, SortableUniqueId.TickNumberOfLength),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var ticks))
        {
            throw new SortableUniqueIdSeedException(
                serviceId,
                $"The persisted SortableUniqueId head for service '{serviceId}' is malformed.");
        }

        _generator.Seed(ticks);
    }
}

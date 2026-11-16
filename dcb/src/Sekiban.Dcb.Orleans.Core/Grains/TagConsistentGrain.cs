using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Common;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans grain implementation for tag consistency management
///     Delegates to GeneralTagConsistentActor for actual functionality
/// </summary>
public class TagConsistentGrain : Grain, ITagConsistentGrain
{
    private readonly ITagTypes _tagTypes;
    private readonly IEventStore _eventStore;
    private readonly TagConsistentActorOptions _options;
    private GeneralTagConsistentActor? _actor;

    public TagConsistentGrain(
        IEventStore eventStore,
        ITagTypes tagTypes,
        TagConsistentActorOptions? options = null)
    {
        _eventStore = eventStore;
        _tagTypes = tagTypes ?? throw new ArgumentNullException(nameof(tagTypes));
        _options = options ?? new TagConsistentActorOptions();
    }

    public Task<string> GetTagActorIdAsync()
    {
        if (_actor == null)
        {
            return Task.FromResult(string.Empty);
        }

        return _actor.GetTagActorIdAsync();
    }

    public Task<string> GetLatestSortableUniqueIdAsync()
    {
        if (_actor == null)
        {
            return Task.FromException<string>(new InvalidOperationException("Actor not initialized"));
        }

        return GrainFailureTransport.PrepareAsync(_actor.GetLatestSortableUniqueIdAsync(), () => ServiceProvider);
    }

    public async Task<string?> TryGetLatestSortableUniqueIdAsync()
    {
        if (_actor == null)
        {
            return null;
        }

        // A failure the actor reports means the latest position is unknown; it is not sent over the wire.
        var (latest, failure) = await TaskFailureCapture.CaptureAsync(_actor.GetLatestSortableUniqueIdAsync());
        return failure is null ? latest : null;
    }

    public Task<TagWriteReservation> MakeReservationAsync(string? lastSortableUniqueId)
    {
        if (_actor == null)
        {
            return Task.FromException<TagWriteReservation>(new InvalidOperationException("Actor not initialized"));
        }

        return GrainFailureTransport.PrepareAsync(_actor.MakeReservationAsync(lastSortableUniqueId), () => ServiceProvider);
    }

    public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation)
    {
        if (_actor == null)
        {
            return Task.FromResult(false);
        }

        return _actor.ConfirmReservationAsync(reservation);
    }

    public Task<bool> CancelReservationAsync(TagWriteReservation reservation)
    {
        if (_actor == null)
        {
            return Task.FromResult(false);
        }

        return _actor.CancelReservationAsync(reservation);
    }

    public Task NotifyEventWrittenAsync()
    {
        if (_actor == null)
        {
            return Task.CompletedTask;
        }
        return _actor.NotifyEventWrittenAsync();
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Extract tag name from grain key
        var tagName = ServiceIdGrainKey.Strip(this.GetPrimaryKeyString());

        // Create the actor instance
        _actor = new GeneralTagConsistentActor(tagName, _eventStore, _options, _tagTypes);

        return base.OnActivateAsync(cancellationToken);
    }
}

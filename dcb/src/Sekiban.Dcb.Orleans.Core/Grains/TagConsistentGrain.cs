using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans grain implementation for tag consistency management
///     Delegates to GeneralTagConsistentActor for actual functionality
/// </summary>
public class TagConsistentGrain : Grain, ITagConsistentGrain
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly TagConsistentActorOptions _options;
    private GeneralTagConsistentActor? _actor;

    public TagConsistentGrain(
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        TagConsistentActorOptions? options = null)
    {
        _eventStore = eventStore;
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
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

    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
    {
        if (_actor == null)
        {
            return Task.FromResult(ResultBox.Error<string>(new InvalidOperationException("Actor not initialized")));
        }

        return _actor.GetLatestSortableUniqueIdAsync();
    }

    public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string lastSortableUniqueId)
    {
        if (_actor == null)
        {
            return Task.FromResult(
                ResultBox.Error<TagWriteReservation>(new InvalidOperationException("Actor not initialized")));
        }

        return _actor.MakeReservationAsync(lastSortableUniqueId);
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
        var tagName = this.GetPrimaryKeyString();

        // Create the actor instance
        _actor = new GeneralTagConsistentActor(tagName, _eventStore, _options, _domainTypes);

        return base.OnActivateAsync(cancellationToken);
    }
}

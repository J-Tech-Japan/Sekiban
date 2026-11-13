using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Tags;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;

namespace Sekiban.Dcb.Tests;

public class SekG161AbsentTagTests
{
    [Fact]
    public async Task NeverWrittenTag_ReturnsEmptyState()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        ITag tag = new StudentTag(Guid.NewGuid());
        var result = await store.GetLatestTagAsync(tag);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.GetException().Message);
        var state = result.GetValue();
        Assert.Equal(0, state.Version);
        Assert.IsType<EmptyTagStatePayload>(state.Payload);
        Assert.Empty(state.TagProjector);
        Assert.Empty(state.ProjectorVersion);
        Assert.Equal(string.Empty, state.LastSortedUniqueId);
        Assert.Equal(tag.GetTagGroup(), state.TagGroup);
        Assert.Equal(tag.GetTag()[(tag.GetTagGroup().Length + 1)..], state.TagContent);
        Assert.False((await store.TagExistsAsync(tag)).GetValue());
    }

    [Fact]
    public async Task NeverWrittenTag_ReservationAfterConfirmSucceeds()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        ITag tag = new StudentTag(Guid.NewGuid());
        var actor = new GeneralTagConsistentActor(tag.GetTag(), store, new TagConsistentActorOptions(), domain.TagTypes);
        var first = await actor.MakeReservationAsync(string.Empty);
        Assert.True(first.IsSuccess);
        Assert.True(await actor.ConfirmReservationAsync(first.GetValue()));
        var second = await actor.MakeReservationAsync(string.Empty);
        Assert.True(second.IsSuccess, second.IsSuccess ? null : second.GetException().Message);
        Assert.Single(await actor.GetActiveReservationsAsync());
    }
}

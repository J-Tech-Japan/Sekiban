using Dcb.Domain.Weather;
using Microsoft.EntityFrameworkCore;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     Provider-backed decision-read CAS evidence. The tests deliberately keep the handler, durable writer, and actor
///     refresh schedules separate: a frozen read ledger is not allowed to become a post-decision head lookup.
/// </summary>
public sealed class DerivedExpectedTagPositionTests : PostgresTestBase
{
    private const string ServiceId = DefaultServiceIdProvider.DefaultServiceId;

    public DerivedExpectedTagPositionTests(PostgresTestFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DerivedRead_UsesOneHandlerAndFreezesTheSuccessfulPostgresCursor()
    {
        var tag = new WeatherForecastTag(Guid.CreateVersion7());
        var initialPosition = NewPosition(-120);
        await EnableEpochAsync();
        await WriteAsync(initialPosition, new WeatherForecastCreated(
            tag.ForecastId,
            "Tokyo",
            new DateOnly(2026, 9, 9),
            20,
            "initial"), tag);

        var executor = new CoreGeneralSekibanExecutor(Fixture.EventStore, Fixture.ActorAccessor, Fixture.DomainTypes);
        ExpectedTagPositionSpecification? frozen = null;
        var handlerInvocations = 0;
        executor.DerivedExpectedTagPositionFrozen = specification => frozen = specification;

        var result = await executor.ExecuteAsync(
            new DerivedWeatherCommand(),
            async (_, context) =>
            {
                handlerInvocations++;
                var state = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(tag);
                Assert.True(state.IsSuccess, state.IsSuccess ? string.Empty : state.GetException().ToString());
                return await context.AppendEvent(
                    new WeatherForecastUpdated(
                        tag.ForecastId,
                        "Osaka",
                        new DateOnly(2026, 9, 9),
                        21,
                        "derived"),
                    tag);
            },
            new CommandExecutionOptions { DeriveExpectedTagPositionsFromStateReads = true });

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(1, handlerInvocations);
        var frozenSpecification = frozen ?? throw new InvalidOperationException("The derived specification was not observed.");
        var entry = Assert.Single(frozenSpecification.Entries);
        Assert.Equal(ServiceId, entry.ServiceId);
        Assert.Equal(tag.GetTag(), entry.Tag);
        Assert.Equal(TagHeadExpectationKind.Exact, entry.Expectation.Kind);
        Assert.Equal(initialPosition, entry.Expectation.Position);

        var latest = await Fixture.EventStore.GetLatestTagAsync(tag);
        Assert.True(latest.IsSuccess, latest.IsSuccess ? string.Empty : latest.GetException().ToString());
        Assert.Equal(result.GetValue().SortableUniqueId, latest.GetValue().LastSortedUniqueId);
    }

    [Fact]
    public async Task StoreRefresh_RealPostgresRejectsFrozenReads_AndCompletesEveryTagInvalidation()
    {
        var firstTag = new WeatherForecastTag(Guid.CreateVersion7());
        var secondTag = new WeatherForecastTag(Guid.CreateVersion7());
        var firstPosition = NewPosition(-120);
        var secondPosition = NewPosition(-119);
        var firstLaterPosition = NewPosition(-60);
        var secondLaterPosition = NewPosition(-59);
        await EnableEpochAsync();
        await WriteAsync(firstPosition, new WeatherForecastCreated(
            firstTag.ForecastId,
            "Tokyo",
            new DateOnly(2026, 9, 9),
            20,
            "first"), firstTag);
        await WriteAsync(secondPosition, new WeatherForecastCreated(
            secondTag.ForecastId,
            "Kyoto",
            new DateOnly(2026, 9, 9),
            22,
            "second"), secondTag);

        var accessor = new CountingActorAccessor(Fixture.ActorAccessor, null);
        var executor = new CoreGeneralSekibanExecutor(Fixture.EventStore, accessor, Fixture.DomainTypes);
        executor.BeforeDerivedExpectedTagPositionFreeze = async _ =>
        {
            // This is the independent durable-writer schedule. It advances both PostgreSQL heads after the handler
            // read but before the attempt-local specification is frozen.
            await WriteAsync(firstLaterPosition, new WeatherForecastUpdated(
                firstTag.ForecastId,
                "Nara",
                new DateOnly(2026, 9, 9),
                23,
                "first-later"), firstTag);
            await WriteAsync(secondLaterPosition, new WeatherForecastUpdated(
                secondTag.ForecastId,
                "Kobe",
                new DateOnly(2026, 9, 9),
                24,
                "second-later"), secondTag);
        };

        var handlerInvocations = 0;
        var result = await executor.ExecuteAsync(
            new DerivedWeatherCommand(),
            async (_, context) =>
            {
                handlerInvocations++;
                var first = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(firstTag);
                var second = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(secondTag);
                Assert.True(first.IsSuccess, first.IsSuccess ? string.Empty : first.GetException().ToString());
                Assert.True(second.IsSuccess, second.IsSuccess ? string.Empty : second.GetException().ToString());
                return await context.AppendEvent(
                    new WeatherForecastUpdated(
                        firstTag.ForecastId,
                        "Sapporo",
                        new DateOnly(2026, 9, 9),
                        25,
                        "would-conflict"),
                    firstTag,
                    secondTag);
            },
            new CommandExecutionOptions { DeriveExpectedTagPositionsFromStateReads = true });

        var conflict = Assert.IsType<ExpectedTagPositionConflictException>(result.GetException());
        Assert.Equal(StateReadRecoveryStatus.InvalidationCompleted, conflict.StateReadRecovery);
        Assert.Equal(1, handlerInvocations);
        Assert.Equal(1, accessor.NotificationCount(firstTag.GetTag()));
        Assert.Equal(1, accessor.NotificationCount(secondTag.GetTag()));
        Assert.Equal(firstLaterPosition, conflict.Pairs.Single(pair => pair.Tag == firstTag.GetTag()).ObservedPosition);
        Assert.Equal(secondLaterPosition, conflict.Pairs.Single(pair => pair.Tag == secondTag.GetTag()).ObservedPosition);

        var allEvents = await Fixture.EventStore.ReadAllSerializableEventsAsync();
        Assert.True(allEvents.IsSuccess, allEvents.IsSuccess ? string.Empty : allEvents.GetException().ToString());
        Assert.Equal(4, allEvents.GetValue().Count());
    }

    [Fact]
    public async Task NotificationFailure_ContinuesDistinctTagRecovery_AndPreservesTheOriginalConflict()
    {
        var firstTag = new WeatherForecastTag(Guid.CreateVersion7());
        var secondTag = new WeatherForecastTag(Guid.CreateVersion7());
        var firstPosition = NewPosition(-120);
        var secondPosition = NewPosition(-119);
        var firstLaterPosition = NewPosition(-60);
        var secondLaterPosition = NewPosition(-59);
        await EnableEpochAsync();
        await WriteAsync(firstPosition, new WeatherForecastCreated(
            firstTag.ForecastId,
            "Tokyo",
            new DateOnly(2026, 9, 9),
            20,
            "first"), firstTag);
        await WriteAsync(secondPosition, new WeatherForecastCreated(
            secondTag.ForecastId,
            "Kyoto",
            new DateOnly(2026, 9, 9),
            22,
            "second"), secondTag);

        var accessor = new CountingActorAccessor(Fixture.ActorAccessor, secondTag.GetTag());
        var executor = new CoreGeneralSekibanExecutor(Fixture.EventStore, accessor, Fixture.DomainTypes);
        executor.BeforeDerivedExpectedTagPositionFreeze = async _ =>
        {
            await WriteAsync(firstLaterPosition, new WeatherForecastUpdated(
                firstTag.ForecastId,
                "Nara",
                new DateOnly(2026, 9, 9),
                23,
                "first-later"), firstTag);
            await WriteAsync(secondLaterPosition, new WeatherForecastUpdated(
                secondTag.ForecastId,
                "Kobe",
                new DateOnly(2026, 9, 9),
                24,
                "second-later"), secondTag);
        };

        var result = await executor.ExecuteAsync(
            new DerivedWeatherCommand(),
            async (_, context) =>
            {
                var first = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(firstTag);
                var second = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(secondTag);
                Assert.True(first.IsSuccess);
                Assert.True(second.IsSuccess);
                return await context.AppendEvent(
                    new WeatherForecastUpdated(firstTag.ForecastId, "Sapporo", new DateOnly(2026, 9, 9), 25, "blocked"),
                    firstTag,
                    secondTag);
            },
            new CommandExecutionOptions { DeriveExpectedTagPositionsFromStateReads = true });

        var conflict = Assert.IsType<ExpectedTagPositionConflictException>(result.GetException());
        Assert.Equal(StateReadRecoveryStatus.InvalidationIncomplete, conflict.StateReadRecovery);
        Assert.Equal(1, accessor.NotificationCount(firstTag.GetTag()));
        Assert.Equal(1, accessor.NotificationCount(secondTag.GetTag()));
        Assert.Equal(2, conflict.Pairs.Count);
        Assert.Contains(conflict.Pairs, pair => pair.Tag == firstTag.GetTag() && pair.ObservedPosition == firstLaterPosition);
        Assert.Contains(conflict.Pairs, pair => pair.Tag == secondTag.GetTag() && pair.ObservedPosition == secondLaterPosition);
    }

    [Fact]
    public async Task ActorRefresh_RealPostgresDoesNotReplaceTheFrozenSuccessfulRead()
    {
        var tag = new WeatherForecastTag(Guid.CreateVersion7());
        var initialPosition = NewPosition(-120);
        await EnableEpochAsync();
        await WriteAsync(initialPosition, new WeatherForecastCreated(
            tag.ForecastId,
            "Tokyo",
            new DateOnly(2026, 9, 9),
            20,
            "initial"), tag);

        var refreshExecutor = new CoreGeneralSekibanExecutor(Fixture.EventStore, Fixture.ActorAccessor, Fixture.DomainTypes);
        var executor = new CoreGeneralSekibanExecutor(Fixture.EventStore, Fixture.ActorAccessor, Fixture.DomainTypes);
        ExpectedTagPositionSpecification? frozen = null;
        executor.DerivedExpectedTagPositionFrozen = specification => frozen = specification;
        executor.BeforeDerivedExpectedTagPositionFreeze = async _ =>
        {
            // The second executor is the same actor/store composition. It commits H2 and confirms the shared actor has
            // refreshed, while the first attempt must continue using its already-successful H evidence.
            var refresh = await refreshExecutor.ExecuteAsync(
                new RefreshWeatherCommand(),
                (_, context) => context.AppendEvent(
                    new WeatherForecastUpdated(
                        tag.ForecastId,
                        "Osaka",
                        new DateOnly(2026, 9, 9),
                        21,
                        "refresh"),
                    tag));
            Assert.True(refresh.IsSuccess, refresh.IsSuccess ? string.Empty : refresh.GetException().ToString());
            var actor = await Fixture.ActorAccessor.GetActorAsync<ITagConsistentActorCommon>(tag.GetTag());
            Assert.True(actor.IsSuccess, actor.IsSuccess ? string.Empty : actor.GetException().ToString());
            var current = await actor.GetValue().GetLatestSortableUniqueIdAsync();
            Assert.True(current.IsSuccess, current.IsSuccess ? string.Empty : current.GetException().ToString());
            Assert.Equal(refresh.GetValue().SortableUniqueId, current.GetValue());
        };

        var result = await executor.ExecuteAsync(
            new DerivedWeatherCommand(),
            async (_, context) =>
            {
                var state = await context.GetStateAsync<WeatherForecastState, WeatherForecastProjector>(tag);
                Assert.True(state.IsSuccess, state.IsSuccess ? string.Empty : state.GetException().ToString());
                return await context.AppendEvent(
                    new WeatherForecastUpdated(tag.ForecastId, "Nagoya", new DateOnly(2026, 9, 9), 22, "stale"),
                    tag);
            },
            new CommandExecutionOptions { DeriveExpectedTagPositionsFromStateReads = true });

        var frozenSpecification = frozen ?? throw new InvalidOperationException("The derived specification was not observed.");
        var entry = Assert.Single(frozenSpecification.Entries);
        Assert.Equal(initialPosition, entry.Expectation.Position);
        Assert.False(result.IsSuccess);
        Assert.IsNotType<ExpectedTagPositionConflictException>(result.GetException());
        Assert.Equal(2, (await Fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue().Count());
    }

    private async Task EnableEpochAsync()
    {
        await using var context = await Fixture.GetDbContextAsync();
        context.TagHeadEnablementEpochs.Add(new DbTagHeadEnablementEpoch
        {
            ServiceId = ServiceId,
            EnabledAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private async Task WriteAsync(string position, IEventPayload payload, params ITag[] tags)
    {
        var ev = new Event(
            payload,
            position,
            payload.GetType().Name,
            Guid.CreateVersion7(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "g65-test"),
            tags.Select(tag => tag.GetTag()).ToList());
        var result = await Fixture.EventStore.WriteSerializableEventsAsync([ev.ToSerializableEvent(Fixture.DomainTypes.EventTypes)]);
        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
    }

    private static string NewPosition(int secondsFromNow) =>
        SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(secondsFromNow), Guid.CreateVersion7());

    private sealed record DerivedWeatherCommand : ICommand;
    private sealed record RefreshWeatherCommand : ICommand;

    private sealed class CountingActorAccessor : IActorObjectAccessor
    {
        private readonly IActorObjectAccessor _inner;
        private readonly string? _failingTag;
        private readonly Dictionary<string, int> _notifications = new(StringComparer.Ordinal);

        public CountingActorAccessor(IActorObjectAccessor inner, string? failingTag)
        {
            _inner = inner;
            _failingTag = failingTag;
        }

        public int NotificationCount(string tag) => _notifications.TryGetValue(tag, out var count) ? count : 0;

        public async Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
        {
            var actor = await _inner.GetActorAsync<T>(actorId);
            if (!actor.IsSuccess || typeof(T) != typeof(ITagConsistentActorCommon))
            {
                return actor;
            }

            var wrapped = new CountingTagConsistentActor(
                (ITagConsistentActorCommon)(object)actor.GetValue(),
                actorId,
                this);
            return ResultBox.FromValue((T)(object)wrapped);
        }

        public Task<bool> ActorExistsAsync(string actorId) => _inner.ActorExistsAsync(actorId);

        private async Task NotifyAsync(string actorId, ITagConsistentActorCommon actor)
        {
            _notifications[actorId] = NotificationCount(actorId) + 1;
            if (_failingTag is not null && string.Equals(actorId, _failingTag, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Injected notification failure for G65 all-tag recovery proof.");
            }

            await actor.NotifyEventWrittenAsync();
        }

        private sealed class CountingTagConsistentActor : ITagConsistentActorCommon
        {
            private readonly ITagConsistentActorCommon _inner;
            private readonly string _actorId;
            private readonly CountingActorAccessor _owner;

            public CountingTagConsistentActor(
                ITagConsistentActorCommon inner,
                string actorId,
                CountingActorAccessor owner)
            {
                _inner = inner;
                _actorId = actorId;
                _owner = owner;
            }

            public Task<string> GetTagActorIdAsync() => _inner.GetTagActorIdAsync();
            public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();
            public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId) =>
                _inner.MakeReservationAsync(lastSortableUniqueId);
            public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) => _inner.ConfirmReservationAsync(reservation);
            public Task<bool> CancelReservationAsync(TagWriteReservation reservation) => _inner.CancelReservationAsync(reservation);
            public Task NotifyEventWrittenAsync() => _owner.NotifyAsync(_actorId, _inner);
        }
    }
}

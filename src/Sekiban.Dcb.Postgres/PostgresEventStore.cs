using Microsoft.EntityFrameworkCore;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Postgres;

public class PostgresEventStore : IEventStore
{
    private readonly IDbContextFactory<SekibanDcbDbContext> _contextFactory;
    private readonly DcbDomainTypes _domainTypes;

    public PostgresEventStore(IDbContextFactory<SekibanDcbDbContext> contextFactory, DcbDomainTypes domainTypes)
    {
        _contextFactory = contextFactory;
        _domainTypes = domainTypes;
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null)
    {
        try
        {
            Console.WriteLine($"[PostgresEventStore] ReadAllEventsAsync called, since: {since?.Value ?? "null"}");
            
            await using var context = await _contextFactory.CreateDbContextAsync();

            var query = context.Events.AsQueryable();

            if (since != null)
            {
                query = query.Where(e => string.Compare(e.SortableUniqueId, since.Value) > 0);
            }

            var dbEvents = await query.OrderBy(e => e.SortableUniqueId).ToListAsync();
            
            Console.WriteLine($"[PostgresEventStore] Found {dbEvents.Count} events in database");

            var events = new List<Event>();
            foreach (var dbEvent in dbEvents)
            {
                var payloadResult = DeserializeEventPayload(dbEvent.EventType, dbEvent.Payload);
                if (!payloadResult.IsSuccess)
                {
                    return ResultBox.Error<IEnumerable<Event>>(payloadResult.GetException());
                }

                events.Add(dbEvent.ToEvent(payloadResult.GetValue()));
            }

            return ResultBox.FromValue<IEnumerable<Event>>(events);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tagString = tag.GetTag();
            var tagJson = $"\"{tagString}\"";
            var query = context.Events.Where(e => EF.Functions.JsonContains(e.Tags, tagJson));

            if (since != null)
            {
                query = query.Where(e => string.Compare(e.SortableUniqueId, since.Value) > 0);
            }

            var dbEvents = await query.OrderBy(e => e.SortableUniqueId).ToListAsync();

            var events = new List<Event>();
            foreach (var dbEvent in dbEvents)
            {
                var payloadResult = DeserializeEventPayload(dbEvent.EventType, dbEvent.Payload);
                if (!payloadResult.IsSuccess)
                {
                    return ResultBox.Error<IEnumerable<Event>>(payloadResult.GetException());
                }

                events.Add(dbEvent.ToEvent(payloadResult.GetValue()));
            }

            return ResultBox.FromValue<IEnumerable<Event>>(events);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<Event>>(ex);
        }
    }

    public async Task<ResultBox<Event>> ReadEventAsync(Guid eventId)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var dbEvent = await context.Events.FirstOrDefaultAsync(e => e.Id == eventId);

            if (dbEvent == null)
            {
                return ResultBox.Error<Event>(new Exception($"Event with ID {eventId} not found"));
            }

            var payloadResult = DeserializeEventPayload(dbEvent.EventType, dbEvent.Payload);
            if (!payloadResult.IsSuccess)
            {
                return ResultBox.Error<Event>(payloadResult.GetException());
            }

            return ResultBox.FromValue(dbEvent.ToEvent(payloadResult.GetValue()));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<Event>(ex);
        }
    }

    public async Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteEventsAsync(IEnumerable<Event> events)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Use the execution strategy for retry logic
            var strategy = context.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(async () =>
            {
                // Begin transaction inside the execution strategy
                await using var transaction = await context.Database.BeginTransactionAsync();

                var eventsList = events.ToList();
                var writtenEvents = new List<Event>();
                var tagWriteResults = new List<TagWriteResult>();

                // Process each event
                foreach (var ev in eventsList)
                {
                    // Serialize the event payload
                    var serializedPayload = SerializeEventPayload(ev.Payload);

                    // Create and add the database event
                    var dbEvent = DbEvent.FromEvent(ev, serializedPayload);
                    context.Events.Add(dbEvent);
                    writtenEvents.Add(ev);

                    // Process tags associated with this event
                    foreach (var tagString in ev.Tags)
                    {
                        // Extract tag group from tag string (format: "group:content")
                        var tagGroup = tagString.Contains(':') ? tagString.Split(':')[0] : tagString;
                        
                        // Create a tag entry for this event
                        var dbTag = DbTag.FromEventTag(tagString, tagGroup, ev.SortableUniqueIdValue, ev.Id, ev.EventType);
                        context.Tags.Add(dbTag);

                        tagWriteResults.Add(
                            new TagWriteResult(
                                tagString,
                                1, // Version placeholder
                                DateTimeOffset.UtcNow));
                    }
                }

                // Save changes
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                return ResultBox.FromValue(
                    (Events: (IReadOnlyList<Event>)writtenEvents,
                        TagWrites: (IReadOnlyList<TagWriteResult>)tagWriteResults));
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WriteEventsAsync failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(ex);
        }
    }

    public async Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tagString = tag.GetTag();

            // Get all tag entries for this tag
            var tags = await context.Tags.Where(t => t.Tag == tagString).OrderBy(t => t.SortableUniqueId).ToListAsync();

            var tagStreams = new List<TagStream>();
            foreach (var dbTag in tags)
            {
                tagStreams.Add(new TagStream(dbTag.Tag, dbTag.EventId, dbTag.SortableUniqueId));
            }

            return ResultBox.FromValue<IEnumerable<TagStream>>(tagStreams);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEnumerable<TagStream>>(ex);
        }
    }

    public async Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tagString = tag.GetTag();
            var tagGroup = tag.GetTagGroup();
            var tagContent = tagString.Substring(tagGroup.Length + 1);

            // Get the latest tag entry
            var dbTag = await context
                .Tags
                .Where(t => t.Tag == tagString)
                .OrderByDescending(t => t.SortableUniqueId)
                .FirstOrDefaultAsync();

            if (dbTag == null)
            {
                // Return empty tag state
                return ResultBox.FromValue(
                    new TagState(
                        new EmptyTagStatePayload(),
                        0,
                        string.Empty,
                        tagGroup,
                        tagContent,
                        string.Empty,
                        string.Empty));
            }

            // Return a simple tag state with just the latest sortable unique ID
            // The actual state would be computed by projectors
            return ResultBox.FromValue(
                new TagState(
                    new EmptyTagStatePayload(),
                    0,
                    dbTag.SortableUniqueId,
                    tagGroup,
                    tagContent,
                    string.Empty,
                    string.Empty));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TagState>(ex);
        }
    }

    public async Task<ResultBox<bool>> TagExistsAsync(ITag tag)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var tagString = tag.GetTag();
            var exists = await context.Tags.AnyAsync(t => t.Tag == tagString);

            return ResultBox.FromValue(exists);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    // Note: Tag state is not stored in the database
    // Tags table only tracks tag-to-event relationships
    // Tag state should be computed by projectors when needed

    private string SerializeEventPayload(IEventPayload payload) =>
        _domainTypes.EventTypes.SerializeEventPayload(payload);

    private ResultBox<IEventPayload> DeserializeEventPayload(string eventType, string json)
    {
        try
        {
            var payload = _domainTypes.EventTypes.DeserializeEventPayload(eventType, json);
            if (payload == null)
            {
                return ResultBox.Error<IEventPayload>(
                    new Exception(
                        $"Failed to deserialize event payload of type {eventType}. Make sure the event type is registered."));
            }

            return ResultBox.FromValue(payload);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IEventPayload>(ex);
        }
    }
}

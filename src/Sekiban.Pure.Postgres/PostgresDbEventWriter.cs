using Sekiban.Pure.Events;
using Sekiban.Pure.Serialize;
namespace Sekiban.Pure.Postgres;

public class PostgresDbEventWriter : IEventWriter, IEventRemover
{
    private readonly PostgresDbFactory _dbFactory;
    private readonly IEventTypes _eventTypes;
    private readonly ISekibanSerializer _serializer;

    public PostgresDbEventWriter(PostgresDbFactory dbFactory, SekibanDomainTypes sekibanDomainTypes)
    {
        _dbFactory = dbFactory;
        _eventTypes = sekibanDomainTypes.EventTypes;
        _serializer = sekibanDomainTypes.Serializer;
    }

    public async Task SaveEvents<TEvent>(IEnumerable<TEvent> events) where TEvent : IEvent
    {
        await _dbFactory.DbActionAsync(
            async dbContext =>
            {
                var dbEvents = events
                    .Select(ev => DbEvent.FromEvent(ev, _serializer, _eventTypes))
                    .ToList();

                await dbContext.Events.AddRangeAsync(dbEvents);
                await dbContext.SaveChangesAsync();
            });
    }
    
    /// <summary>
    /// Removes all events from the PostgreSQL event table
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task RemoveAllEvents()
    {
        return _dbFactory.DeleteAllFromEventContainer();
    }
}

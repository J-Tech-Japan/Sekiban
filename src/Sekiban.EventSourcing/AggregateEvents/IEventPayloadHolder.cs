namespace Sekiban.EventSourcing.AggregateEvents
{
    public interface IEventPayloadHolder
    {
        public IEventPayload GetPayload();
    }
}

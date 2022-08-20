namespace Sekiban.EventSourcing.Documents
{
    public enum DocumentType
    {
        AggregateCommand = 1,
        AggregateEvent = 2,
        AggregateSnapshot = 3,
        MultipleAggregateSnapshot = 4
    }
}

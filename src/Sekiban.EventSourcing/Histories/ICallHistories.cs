namespace Sekiban.EventSourcing.Histories
{
    public interface ICallHistories
    {
        List<CallHistory> CallHistories { get; init; }
    }
}

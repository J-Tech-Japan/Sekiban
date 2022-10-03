namespace Sekiban.EventSourcing.TestHelpers;

public interface ITestHelperEventSubscriber
{
    public Action<IAggregateEvent> OnEvent { get; }
}

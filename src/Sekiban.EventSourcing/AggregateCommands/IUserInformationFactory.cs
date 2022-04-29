namespace Sekiban.EventSourcing.AggregateCommands;

public interface IUserInformationFactory
{
    string GetCurrentUserInformation();
}

namespace Sekiban.EventSourcing.AggregateCommands.UserInformations;

public interface IUserInformationFactory
{
    string GetCurrentUserInformation();
}

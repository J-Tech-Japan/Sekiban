using Sekiban.Core.Aggregate;
namespace Sekiban.Web.Authorizations.Definitions;

public class CreateCommandsForAggregate<TAggregatePayload> : IAuthorizationDefinitionType where TAggregatePayload : IAggregatePayloadCommon
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        typeof(TAggregatePayload).FullName == aggregateType.FullName && authorizeMethodType == AuthorizeMethodType.CreateCommand;
}

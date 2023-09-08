using Sekiban.Core.Aggregate;
namespace Sekiban.Web.Authorizations.Definitions;

/// <summary>
///     Authorize group for get for aggregate
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public class GetForAggregate<TAggregatePayload> : IAuthorizationDefinitionType where TAggregatePayload : IAggregatePayloadCommon
{
    public bool IsMatches(AuthorizeMethodType authorizeMethodType, Type aggregateType, Type? commandType) =>
        typeof(TAggregatePayload).FullName == aggregateType.FullName && authorizeMethodType == AuthorizeMethodType.Get;
}

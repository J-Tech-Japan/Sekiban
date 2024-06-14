using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.TenantUsers;

public record TenantUser(string Name, string Email) : IAggregatePayload<TenantUser>
{
    public static TenantUser CreateInitialPayload(TenantUser? _) => new(string.Empty, string.Empty);
}
public record TenantUserCreated(string Name, string Email) : IEventPayload<TenantUser, TenantUserCreated>
{
    public static TenantUser OnEvent(TenantUser aggregatePayload, Event<TenantUserCreated> ev) =>
        aggregatePayload with { Name = ev.Payload.Name, Email = ev.Payload.Email };
}
public record CreateTenantUser(string Name, string Email, string TenantId) : ITenantCommandWithHandlerAsync<TenantUser, CreateTenantUser>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public static async Task<ResultBox<UnitValue>> HandleCommandAsync(CreateTenantUser command, ICommandContext<TenantUser> context) =>
        await context.GetRequiredService<IQueryExecutor>()
            .Conveyor(
                queryExecutor => queryExecutor.ExecuteWithResultAsync(new TenantUserDuplicateEmailQuery.Parameter(command.TenantId, command.Email)))
            .Verify(response => response.IsDuplicate ? new ApplicationException("Duplicate email in Tenant") : ExceptionOrNone.None)
            .Conveyor(_ => context.AppendEvent(new TenantUserCreated(command.Name, command.Email)));
    public static ResultBox<UnitValue> HandleCommand(CreateTenantUser command, ICommandContext<TenantUser> context) =>
        throw new NotImplementedException();
}
public class TenantUserDuplicateEmailQuery : ITenantAggregateQuery<TenantUser, TenantUserDuplicateEmailQuery.Parameter,
    TenantUserDuplicateEmailQuery.Response>
{
    public Response HandleFilter(Parameter queryParam, IEnumerable<AggregateState<TenantUser>> list) =>
        list.Any(m => m.Payload.Email == queryParam.Email) ? new Response(true) : new Response(false);
    public record Parameter(string TenantId, string Email) : ITenantQueryParameter<Response>;
    public record Response(bool IsDuplicate) : IQueryResponse;
}

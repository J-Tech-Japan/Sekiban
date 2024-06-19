using ResultBoxes;
using Sekiban.Core.Command;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.TenantUsers;

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
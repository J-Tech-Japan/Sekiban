using ResultBoxes;
using Sekiban.Core.Command;
namespace FeatureCheck.Domain.Aggregates.TenantUsers;

public record CreateTenantUser(string Name, string Email, string TenantId)
    : ITenantCommandWithHandlerAsync<TenantUser, CreateTenantUser>
{
    public static async Task<ResultBox<UnitValue>> HandleCommandAsync(
        CreateTenantUser command,
        ICommandContext<TenantUser> context)
    {
        return await context
            .ExecuteQueryAsync(new TenantUserDuplicateEmailQuery.Parameter(command.TenantId, command.Email))
            .Verify(
                response => response.IsDuplicate
                    ? new ApplicationException("Duplicate email in Tenant")
                    : ExceptionOrNone.None)
            .Conveyor(_ => context.AppendEvent(new TenantUserCreated(command.Name, command.Email)));
    }
    public static Guid SpecifyAggregateId(CreateTenantUser command) => Guid.NewGuid();
    public static string GetTenantId(CreateTenantUser command) => command.TenantId;
}

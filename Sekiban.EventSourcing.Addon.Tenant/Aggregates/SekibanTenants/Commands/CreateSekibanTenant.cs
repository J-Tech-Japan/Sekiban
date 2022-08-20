using Sekiban.EventSourcing.AggregateCommands;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.Commands
{
    public record CreateSekibanTenant(Guid TenantId, string TenantName, string TenantCode) : ICreateAggregateCommand<SekibanTenant>
    {
        public CreateSekibanTenant() : this(Guid.Empty, string.Empty, string.Empty) { }
    }
    public class CreateSekibanTenantHandler : CreateAggregateCommandHandlerBase<SekibanTenant, CreateSekibanTenant>
    {
        public override Guid GenerateAggregateId(CreateSekibanTenant command) =>
            command.TenantId;
        protected override async Task ExecCreateCommandAsync(SekibanTenant aggregate, CreateSekibanTenant command)
        {
            await Task.CompletedTask;
            aggregate.CreateSekibanTenant(command.TenantName, command.TenantCode);
        }
    }
}

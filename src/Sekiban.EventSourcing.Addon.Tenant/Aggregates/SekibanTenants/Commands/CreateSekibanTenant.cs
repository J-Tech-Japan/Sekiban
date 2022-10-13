using Sekiban.EventSourcing.Addon.Tenant.Validations;
using Sekiban.EventSourcing.AggregateCommands;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.Commands;

public record CreateSekibanTenant : ICreateAggregateCommand<SekibanTenant>
{
    [GuidRequired]
    public Guid TenantId
    {
        get;
        init;
    }
    [Required]
    [MaxLength(30)]
    public string TenantName
    {
        get;
        init;
    }
    [RegularExpression(@"^[a-zA-Z0-9\-_]+$", ErrorMessage = "Only alphanumeric characters, dashes and underscores are allowed")]
    public string TenantCode
    {
        get;
        init;
    }

    public CreateSekibanTenant() : this(Guid.Empty, string.Empty, string.Empty) { }
    public CreateSekibanTenant(Guid tenantId, string tenantName, string tenantCode)
    {
        TenantId = tenantId;
        TenantName = tenantName;
        TenantCode = tenantCode;
    }
}
public class CreateSekibanTenantHandler : CreateAggregateCommandHandlerBase<SekibanTenant, CreateSekibanTenant>
{
    public override Guid GenerateAggregateId(CreateSekibanTenant command)
    {
        return command.TenantId;
    }
    protected override async Task ExecCreateCommandAsync(SekibanTenant aggregate, CreateSekibanTenant command)
    {
        await Task.CompletedTask;
        aggregate.CreateSekibanTenant(command.TenantName, command.TenantCode);
    }
}

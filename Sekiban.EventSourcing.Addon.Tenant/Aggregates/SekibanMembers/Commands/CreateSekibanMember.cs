using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants;
using Sekiban.EventSourcing.Addon.Tenant.Exceptions;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.Commands;

public record CreateSekibanMember
    (Guid SekibanTenantId, Guid SekibanMemberId, string Name, string Email, string uniqueIdentifier) : ICreateAggregateCommand<SekibanMember>
{
    public CreateSekibanMember() : this(Guid.Empty, Guid.Empty, string.Empty, string.Empty, string.Empty) { }
}
public class CreateSekibanMemberHandler : CreateAggregateCommandHandlerBase<SekibanMember, CreateSekibanMember>
{
    private readonly ISingleAggregateService _singleAggregateService;

    public CreateSekibanMemberHandler(ISingleAggregateService singleAggregateService) =>
        _singleAggregateService = singleAggregateService;
    public override Guid GenerateAggregateId(CreateSekibanMember command) =>
        command.SekibanMemberId;
    protected override async Task ExecCreateCommandAsync(SekibanMember aggregate, CreateSekibanMember command)
    {
        var tenant = await _singleAggregateService.GetAggregateDtoAsync<SekibanTenant, SekibanTenantContents>(command.SekibanTenantId);
        if (tenant == null)
        {
            throw new SekibanTenantNotExistsException(command.SekibanTenantId);
        }
        aggregate.Create(command.Name, command.Email, command.uniqueIdentifier);
    }
}

using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants;
using Sekiban.EventSourcing.Addon.Tenant.Exceptions;
using Sekiban.EventSourcing.Addon.Tenant.Validations;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.Commands;

public record CreateSekibanMember : ICreateAggregateCommand<SekibanMember>
{
    [GuidRequired]
    public Guid SekibanTenantId { get; init; }
    [GuidRequired]
    public Guid SekibanMemberId { get; init; }
    [Required]
    public string Name { get; init; }
    [Required]
    [EmailAddress]
    public string Email { get; init; }
    [Required]
    [RegularExpression(@"^[a-zA-Z0-9\-_]+$", ErrorMessage = "Only alphanumeric characters, dashes and underscores are allowed")]
    public string UniqueIdentifier { get; init; }
    public CreateSekibanMember(Guid sekibanTenantId, Guid sekibanMemberId, string name, string email, string uniqueIdentifier)
    {
        SekibanTenantId = sekibanTenantId;
        SekibanMemberId = sekibanMemberId;
        Name = name;
        Email = email;
        UniqueIdentifier = uniqueIdentifier;
    }
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
        aggregate.Create(command.Name, command.Email, command.UniqueIdentifier);
    }
}

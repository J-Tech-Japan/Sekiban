using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using ResultBoxes;
using Sekiban.Core.Command;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record CreateClientR(
    [property: Required]
    Guid BranchId,
    [property: Required]
    string ClientName,
    [property: Required]
    string ClientEmail) : ICommandWithHandlerAsync<Client, CreateClientR>
{
    public static Task<ResultBox<EventOrNone<Client>>>
        HandleCommandAsync(CreateClientR command, ICommandContext<Client> context) => context
        .ExecuteQueryAsync(new BranchExistsQueryN(command.BranchId))
        .Verify(exists => exists ? ExceptionOrNone.None : new InvalidDataException("Branch not exists"))
        .Conveyor(_ => context.ExecuteQueryAsync(new ClientEmailExistQueryNext(command.ClientEmail)))
        .Verify(exists => exists ? new InvalidDataException("Email not exists") : ExceptionOrNone.None)
        .Conveyor(_ => EventOrNone.Event(new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail)));
    public static Guid SpecifyAggregateId(CreateClientR command) => Guid.NewGuid();
}

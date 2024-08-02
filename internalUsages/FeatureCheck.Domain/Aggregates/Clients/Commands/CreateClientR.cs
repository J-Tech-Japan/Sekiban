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
    public Guid GetAggregateId() => Guid.NewGuid();

    public static Task<ResultBox<UnitValue>>
        HandleCommandAsync(CreateClientR command, ICommandContext<Client> context) => context
        .ExecuteQueryAsync(new BranchExistsQueryN(command.BranchId))
        .Verify(exists => exists ? ExceptionOrNone.None : new InvalidDataException("Branch not exists"))
        .Conveyor(_ => context.ExecuteQueryAsync(new ClientEmailExistQueryNext(command.ClientEmail)))
        .Verify(exists => exists ? new InvalidDataException("Email not exists") : ExceptionOrNone.None)
        .Conveyor(
            _ => context.AppendEvent(new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail)));
}

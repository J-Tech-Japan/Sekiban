using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Shared.Exceptions;
using ResultBoxes;
using Sekiban.Core.Command;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record CreateClientWithResult(
    [property: Required]
    Guid BranchId,
    [property: Required]
    string ClientName,
    [property: Required]
    string ClientEmail) : ICommandWithHandlerAsync<Client, CreateClientWithResult>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public static async Task<ResultBox<UnitValue>> HandleCommandAsync(
        CreateClientWithResult command, 
        ICommandContext<Client> context) =>
        await context.GetRequiredService<IQueryExecutor>()
            .Conveyor(queryExecutor => queryExecutor.ExecuteWithResultAsync(new BranchExistsQuery.Parameter(command.BranchId)))
            .Verify(
                value => value.Exists
                    ? ExceptionOrNone.None
                    : new SekibanAggregateNotExistsException(
                        command.BranchId, nameof(Branch), 
                        (command as ICommandCommon).GetRootPartitionKey()))
            .Conveyor(_ => context.GetRequiredService<IQueryExecutor>())
            .Conveyor(
                queryExecutor => queryExecutor.ExecuteWithResultAsync(
                    new ClientEmailExistsQuery.Parameter(command.ClientEmail)
                    {
                        RootPartitionKey = (command as ICommandCommon).GetRootPartitionKey()
                    }))
            .Verify(response => response.Exists ? new SekibanEmailAlreadyRegistered() : ExceptionOrNone.None)
            .Conveyor(_ => context.AppendEvent(new ClientCreated(command.BranchId, command.ClientName, command.ClientEmail)));
}
